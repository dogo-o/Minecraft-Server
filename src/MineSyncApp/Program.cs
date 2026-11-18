using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using MineSyncApp.Core;
using MineSyncApp.UI;

namespace MineSyncApp
{
    public static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        [STAThread]
        public static int Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                if (string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
                {
                    AttachConsole(-1);
                    return RunSelfTest();
                }
                if (string.Equals(args[0], "--uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    return RunUninstall();
                }
            }

            try
            {
                SetProcessDPIAware();
            }
            catch
            {
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        private static int RunSelfTest()
        {
            try
            {
                ServerConfig cfg = new ServerConfig();
                Dictionary<string, string> props = cfg.ReadServerProperties();
                DockerSettings docker = cfg.ReadDockerConfig();
                PlayerAndBackupService svc = new PlayerAndBackupService(cfg);
                List<PlayerInfo> players = svc.GetPlayersAsync(false).GetAwaiter().GetResult();
                List<BackupEntry> backups = svc.ListLocalBackups();

                // Loopback RCON packet verification
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                System.Threading.Tasks.Task serverTask = System.Threading.Tasks.Task.Run(delegate()
                {
                    using (TcpClient client = listener.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    {
                        byte[] buf = new byte[512];
                        while (true)
                        {
                            int n = stream.Read(buf, 0, buf.Length);
                            if (n <= 0) break;
                            int id = BitConverter.ToInt32(buf, 4);
                            int type = BitConverter.ToInt32(buf, 8);
                            byte[] replyBody = Encoding.UTF8.GetBytes(type == 3 ? "" : "§aOK: pong");
                            int rlen = 4 + 4 + replyBody.Length + 2;
                            byte[] outBuf = new byte[4 + rlen];
                            Buffer.BlockCopy(BitConverter.GetBytes(rlen), 0, outBuf, 0, 4);
                            Buffer.BlockCopy(BitConverter.GetBytes(id), 0, outBuf, 4, 4);
                            Buffer.BlockCopy(BitConverter.GetBytes(type == 3 ? 2 : 0), 0, outBuf, 8, 4);
                            Buffer.BlockCopy(replyBody, 0, outBuf, 12, replyBody.Length);
                            stream.Write(outBuf, 0, outBuf.Length);
                        }
                    }
                    listener.Stop();
                });

                string rconOut = RconClient.SendCommandAsync("127.0.0.1", port, "secret", "ping", 3000).GetAwaiter().GetResult();
                serverTask.Wait(2000);

                StringBuilder report = new StringBuilder();
                report.AppendLine("SELF_TEST_OK");
                report.AppendLine("Workspace=" + cfg.WorkspaceDir);
                report.AppendLine("Docker=" + docker.Type + " " + docker.Version + " " + docker.Memory);
                string motd = props.ContainsKey("motd") ? props["motd"] : "";
                report.AppendLine("MOTD=" + motd);
                report.AppendLine("PlayersCount=" + players.Count);
                if (players.Count > 0)
                {
                    report.AppendLine("TopPlayer=" + players[0].Name + " (" + players[0].PlayTimeFormatted + ")");
                }
                report.AppendLine("BackupsCount=" + backups.Count);
                report.AppendLine("RconTest=" + rconOut);

                string outPath = Path.Combine(cfg.WorkspaceDir, "selftest_report.txt");
                File.WriteAllText(outPath, report.ToString(), Encoding.UTF8);
                Console.WriteLine(report.ToString());
                return rconOut == "OK: pong" ? 0 : 2;
            }
            catch (Exception ex)
            {
                Console.WriteLine("SELF_TEST_FAIL: " + ex);
                return 1;
            }
        }

        private static int RunUninstall()
        {
            DialogResult confirm = MessageBox.Show(
                "Deseja desinstalar o MineSync Pro?\n\nSeus arquivos de mundo (dados_mine) e backups NÃO serão removidos.",
                "Desinstalar MineSync Pro",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );
            if (confirm != DialogResult.Yes) return 0;

            try
            {
                string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MineSync Pro.lnk");
                if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

                string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "MineSync Pro");
                if (Directory.Exists(startMenuDir)) Directory.Delete(startMenuDir, true);

                if (Directory.Exists(ServerConfig.AppDataConfigDir))
                {
                    Directory.Delete(ServerConfig.AppDataConfigDir, true);
                }

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", true))
                {
                    if (key != null) key.DeleteSubKeyTree("MineSyncPro", false);
                }

                string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MineSyncPro");
                string exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
                if (string.Equals(Path.GetFullPath(exeDir), Path.GetFullPath(installDir), StringComparison.OrdinalIgnoreCase))
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = "cmd.exe";
                    psi.Arguments = string.Format("/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{0}\"", installDir);
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                }

                MessageBox.Show("MineSync Pro desinstalado com sucesso!", "Desinstalação Concluída", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao desinstalar: " + ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return 0;
        }
    }
}
