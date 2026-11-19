using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MineSyncInstaller
{
    public static class InstallerProgram
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        public static int Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }

            if (args != null && args.Length > 0 && string.Equals(args[0], "--silent", StringComparison.OrdinalIgnoreCase))
            {
                string defaultInstall = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MineSyncPro");
                string defaultWs = DetectDefaultServerDir();
                InstallEngine.PerformInstall(defaultInstall, defaultWs, true, true, true, null);
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerForm());
            return 0;
        }

        public static string DetectDefaultServerDir()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            if (File.Exists(Path.Combine(exeDir, "docker-compose.yml")) || Directory.Exists(Path.Combine(exeDir, "dados_mine")))
            {
                return exeDir;
            }
            string docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Minecraft-Server");
            if (Directory.Exists(docs)) return docs;
            return exeDir;
        }
    }

    public static class InstallEngine
    {
        public static void ExtractResource(string resourceName, string destinationPath)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream stream = asm.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException("Recurso embutido não encontrado: " + resourceName);
                }
                string parent = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                using (FileStream fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fs);
                }
            }
        }

        public static void CreateShortcut(string shortcutPath, string targetExe, string workingDir, string iconPath, string description)
        {
            string parent = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });

            Type scType = shortcut.GetType();
            scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetExe });
            scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
            if (File.Exists(iconPath))
            {
                scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconPath });
            }
            scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
            scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }

        public static void RegisterUninstaller(string installDir, string exePath, string iconPath)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MineSyncPro"))
            {
                if (key == null) return;
                long sizeKb = new FileInfo(exePath).Length / 1024;
                key.SetValue("DisplayName", "MineSync Pro");
                key.SetValue("DisplayVersion", "2.0.0");
                key.SetValue("Publisher", "MineSync Pro");
                key.SetValue("DisplayIcon", File.Exists(iconPath) ? iconPath : exePath);
                key.SetValue("InstallLocation", installDir);
                key.SetValue("UninstallString", string.Format("\"{0}\" --uninstall", exePath));
                key.SetValue("EstimatedSize", (int)Math.Max(100, sizeKb), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        public static void PerformInstall(
            string installDir,
            string serverDir,
            bool desktopShortcut,
            bool startMenuShortcut,
            bool registerWindows,
            Action<int, string> progressCallback)
        {
            Action<int, string> report = delegate(int pct, string msg)
            {
                if (progressCallback != null) progressCallback(pct, msg);
            };

            report(15, "Preparando diretório de instalação...");
            Directory.CreateDirectory(installDir);

            string installedExe = Path.Combine(installDir, "MineSync.exe");
            string installedIco = Path.Combine(installDir, "server.ico");

            report(35, "Extraindo MineSync.exe e recursos...");
            ExtractResource("MineSync.exe", installedExe);
            ExtractResource("server.ico", installedIco);

            report(55, "Configurando caminho do servidor Minecraft...");
            string appDataCfgDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MineSyncPro");
            Directory.CreateDirectory(appDataCfgDir);
            File.WriteAllText(Path.Combine(appDataCfgDir, "workspace.txt"), serverDir, Encoding.UTF8);

            if (Directory.Exists(serverDir) && !string.Equals(Path.GetFullPath(serverDir), Path.GetFullPath(installDir), StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string looseExe = Path.Combine(serverDir, "MineSync.exe");
                    if (File.Exists(looseExe))
                    {
                        File.Delete(looseExe);
                    }
                }
                catch
                {
                }
            }

            if (desktopShortcut)
            {
                report(70, "Criando atalho na Área de Trabalho...");
                string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MineSync Pro.lnk");
                CreateShortcut(desktopLnk, installedExe, serverDir, installedIco, "Gerenciador de Servidor Minecraft MineSync Pro");
            }

            if (startMenuShortcut)
            {
                report(82, "Criando atalho no Menu Iniciar...");
                string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "MineSync Pro");
                CreateShortcut(Path.Combine(startMenuDir, "MineSync Pro.lnk"), installedExe, serverDir, installedIco, "Gerenciador de Servidor Minecraft MineSync Pro");
                CreateShortcut(Path.Combine(startMenuDir, "Desinstalar MineSync Pro.lnk"), installedExe, installDir, installedIco, "Desinstalar MineSync Pro");
                // Fix arguments on uninstall shortcut
                SetShortcutArgs(Path.Combine(startMenuDir, "Desinstalar MineSync Pro.lnk"), "--uninstall");
            }

            if (registerWindows)
            {
                report(92, "Registrando aplicativo no Windows...");
                RegisterUninstaller(installDir, installedExe, installedIco);
            }

            report(100, "Instalação concluída com sucesso!");
        }

        private static void SetShortcutArgs(string shortcutPath, string args)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            Type scType = shortcut.GetType();
            scType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { args });
            scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
    }

    public class InstallerForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private readonly TextBox _txtInstallDir;
        private readonly TextBox _txtServerDir;
        private readonly CheckBox _chkDesktop;
        private readonly CheckBox _chkStartMenu;
        private readonly CheckBox _chkRegister;
        private readonly CheckBox _chkLaunchAfter;
        private readonly ProgressBar _progressBar;
        private readonly Label _lblStatus;
        private readonly Button _btnInstall;
        private readonly Button _btnCancel;

        public InstallerForm()
        {
            this.Text = "Instalador — MineSync Pro v2.0";
            this.Size = new Size(600, 470);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(11, 15, 23);
            this.ForeColor = Color.FromArgb(241, 245, 249);
            this.Font = new Font("Segoe UI", 9.5f);

            try
            {
                using (Stream icoStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("server.ico"))
                {
                    if (icoStream != null) this.Icon = new Icon(icoStream);
                }
            }
            catch { }

            try
            {
                int dark = 1;
                if (DwmSetWindowAttribute(this.Handle, 20, ref dark, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(this.Handle, 19, ref dark, sizeof(int));
                }
            }
            catch { }

            // Header Banner
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 78;
            header.BackColor = Color.FromArgb(16, 23, 34);

            Label lblTitle = new Label();
            lblTitle.Text = "MineSync Pro — Assistente de Instalação";
            lblTitle.Font = new Font("Segoe UI", 13.5f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(16, 185, 129);
            lblTitle.Location = new Point(20, 14);
            lblTitle.AutoSize = true;

            Label lblSub = new Label();
            lblSub.Text = "Instale o gerenciador nativo do seu servidor Minecraft com Docker, RCON e Google Drive";
            lblSub.Font = new Font("Segoe UI", 9f);
            lblSub.ForeColor = Color.FromArgb(148, 163, 184);
            lblSub.Location = new Point(21, 44);
            lblSub.AutoSize = true;

            header.Controls.Add(lblTitle);
            header.Controls.Add(lblSub);

            // Install Dir
            Label lblInst = new Label();
            lblInst.Text = "Pasta de Instalação do Aplicativo:";
            lblInst.Location = new Point(20, 96);
            lblInst.AutoSize = true;

            _txtInstallDir = new TextBox();
            _txtInstallDir.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MineSyncPro");
            _txtInstallDir.BackColor = Color.FromArgb(7, 10, 16);
            _txtInstallDir.ForeColor = Color.White;
            _txtInstallDir.BorderStyle = BorderStyle.FixedSingle;
            _txtInstallDir.Location = new Point(20, 118);
            _txtInstallDir.Size = new Size(445, 26);

            Button btnBrowseInst = CreateBtn("Alterar...", Color.FromArgb(26, 36, 52));
            btnBrowseInst.Location = new Point(475, 117);
            btnBrowseInst.Size = new Size(90, 27);
            btnBrowseInst.Click += delegate
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.SelectedPath = _txtInstallDir.Text;
                    if (fbd.ShowDialog(this) == DialogResult.OK) _txtInstallDir.Text = fbd.SelectedPath;
                }
            };

            // Server Dir
            Label lblServ = new Label();
            lblServ.Text = "Pasta do Servidor Minecraft (docker-compose.yml / dados_mine):";
            lblServ.Location = new Point(20, 156);
            lblServ.AutoSize = true;

            _txtServerDir = new TextBox();
            _txtServerDir.Text = InstallerProgram.DetectDefaultServerDir();
            _txtServerDir.BackColor = Color.FromArgb(7, 10, 16);
            _txtServerDir.ForeColor = Color.White;
            _txtServerDir.BorderStyle = BorderStyle.FixedSingle;
            _txtServerDir.Location = new Point(20, 178);
            _txtServerDir.Size = new Size(445, 26);

            Button btnBrowseServ = CreateBtn("Alterar...", Color.FromArgb(26, 36, 52));
            btnBrowseServ.Location = new Point(475, 177);
            btnBrowseServ.Size = new Size(90, 27);
            btnBrowseServ.Click += delegate
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.SelectedPath = _txtServerDir.Text;
                    if (fbd.ShowDialog(this) == DialogResult.OK) _txtServerDir.Text = fbd.SelectedPath;
                }
            };

            // Checkboxes
            _chkDesktop = CreateCheck("Criar atalho na Área de Trabalho (MineSync Pro)", 220);
            _chkStartMenu = CreateCheck("Criar atalho no Menu Iniciar", 246);
            _chkRegister = CreateCheck("Registrar nos Programas Instalados do Windows", 272);
            _chkLaunchAfter = CreateCheck("Iniciar o MineSync Pro ao concluir a instalação", 298);

            _lblStatus = new Label();
            _lblStatus.Text = "Pronto para instalar.";
            _lblStatus.ForeColor = Color.FromArgb(148, 163, 184);
            _lblStatus.Location = new Point(20, 334);
            _lblStatus.Size = new Size(545, 20);

            _progressBar = new ProgressBar();
            _progressBar.Location = new Point(20, 356);
            _progressBar.Size = new Size(545, 16);
            _progressBar.Maximum = 100;

            _btnInstall = CreateBtn("Instalar Agora", Color.FromArgb(5, 150, 105));
            _btnInstall.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _btnInstall.Location = new Point(315, 384);
            _btnInstall.Size = new Size(145, 36);
            _btnInstall.Click += async delegate { await StartInstallAsync(); };

            _btnCancel = CreateBtn("Cancelar", Color.FromArgb(26, 36, 52));
            _btnCancel.Location = new Point(470, 384);
            _btnCancel.Size = new Size(95, 36);
            _btnCancel.Click += delegate { this.Close(); };

            this.Controls.Add(header);
            this.Controls.Add(lblInst);
            this.Controls.Add(_txtInstallDir);
            this.Controls.Add(btnBrowseInst);
            this.Controls.Add(lblServ);
            this.Controls.Add(_txtServerDir);
            this.Controls.Add(btnBrowseServ);
            this.Controls.Add(_chkDesktop);
            this.Controls.Add(_chkStartMenu);
            this.Controls.Add(_chkRegister);
            this.Controls.Add(_chkLaunchAfter);
            this.Controls.Add(_lblStatus);
            this.Controls.Add(_progressBar);
            this.Controls.Add(_btnInstall);
            this.Controls.Add(_btnCancel);
        }

        private static CheckBox CreateCheck(string text, int y)
        {
            CheckBox cb = new CheckBox();
            cb.Text = text;
            cb.Checked = true;
            cb.Location = new Point(22, y);
            cb.AutoSize = true;
            return cb;
        }

        private static Button CreateBtn(string text, Color back)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.BackColor = back;
            btn.ForeColor = Color.White;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = ControlPaint.Light(back, 0.25f);
            btn.Cursor = Cursors.Hand;
            return btn;
        }

        private async Task StartInstallAsync()
        {
            string installDir = _txtInstallDir.Text.Trim();
            string serverDir = _txtServerDir.Text.Trim();
            bool desktop = _chkDesktop.Checked;
            bool startMenu = _chkStartMenu.Checked;
            bool register = _chkRegister.Checked;
            bool launch = _chkLaunchAfter.Checked;

            _btnInstall.Enabled = false;
            _btnCancel.Enabled = false;

            try
            {
                await Task.Run(delegate()
                {
                    InstallEngine.PerformInstall(
                        installDir,
                        serverDir,
                        desktop,
                        startMenu,
                        register,
                        delegate(int pct, string msg)
                        {
                            this.Invoke((MethodInvoker)delegate
                            {
                                _progressBar.Value = pct;
                                _lblStatus.Text = msg;
                            });
                        }
                    );
                });

                if (launch)
                {
                    string exePath = Path.Combine(installDir, "MineSync.exe");
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = exePath;
                    psi.WorkingDirectory = serverDir;
                    Process.Start(psi);
                    this.Close();
                }
                else
                {
                    MessageBox.Show("MineSync Pro instalado com sucesso!", "Instalação Concluída", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                _btnInstall.Enabled = true;
                _btnCancel.Enabled = true;
                MessageBox.Show("Erro durante a instalação: " + ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
