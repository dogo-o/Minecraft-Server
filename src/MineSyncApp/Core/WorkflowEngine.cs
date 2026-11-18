using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MineSyncApp.Core
{
    public enum MapConflictResolution
    {
        Cancel,
        DownloadCloud,
        KeepLocal
    }

    public class DockerStatusInfo
    {
        public bool DockerRunning { get; set; }
        public string DockerVersion { get; set; }
        public bool McExists { get; set; }
        public bool McRunning { get; set; }
        public bool McReady { get; set; }
        public string McStatusText { get; set; }
        public string McCpu { get; set; }
        public string McMemory { get; set; }
        public bool PlayitExists { get; set; }
        public bool PlayitRunning { get; set; }
        public string PlayitStatusText { get; set; }
        public string PlayitAddress { get; set; }

        public DockerStatusInfo()
        {
            DockerVersion = "";
            McStatusText = "Offline";
            McCpu = "";
            McMemory = "";
            PlayitStatusText = "Offline";
            PlayitAddress = "";
        }
    }

    public class CloudStatusInfo
    {
        public string Remote { get; set; }
        public bool Authenticated { get; set; }
        public string AuthError { get; set; }
        public long QuotaTotal { get; set; }
        public long QuotaUsed { get; set; }
        public string QuotaUsedText { get; set; }
        public string QuotaTotalText { get; set; }
        public int QuotaPercent { get; set; }
        public bool Locked { get; set; }
        public string LockHost { get; set; }
        public string LockUser { get; set; }
        public string LockStartedAt { get; set; }
        public bool LocalExists { get; set; }
        public DateTime? LocalTime { get; set; }
        public bool CloudExists { get; set; }
        public DateTime? CloudTime { get; set; }
        public double DiffMinutes { get; set; }
        public string NewerSource { get; set; }
        public bool HasConflict { get; set; }

        public CloudStatusInfo()
        {
            Remote = "gdrive0";
            AuthError = "";
            QuotaUsedText = "—";
            QuotaTotalText = "—";
            LockHost = "";
            LockUser = "";
            LockStartedAt = "";
            NewerSource = "unknown";
        }
    }

    public class ProcessRunResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; }
        public string StdErr { get; set; }
        public bool Ok { get { return ExitCode == 0; } }

        public ProcessRunResult()
        {
            ExitCode = -1;
            StdOut = "";
            StdErr = "";
        }
    }

    public class WorkflowEngine
    {
        private const string CloudFolder = "MinecraftSync";
        private readonly ServerConfig _config;
        private readonly PlayerAndBackupService _backupService;

        public bool IsBusy { get; private set; }

        public event Action<string, string> OnLog;
        public event Action<int, int, string> OnProgress;

        public WorkflowEngine(ServerConfig config, PlayerAndBackupService backupService)
        {
            _config = config;
            _backupService = backupService;
        }

        private void Log(string msg, string level)
        {
            Action<string, string> handler = OnLog;
            if (handler != null && !string.IsNullOrWhiteSpace(msg))
            {
                handler(msg, level ?? "info");
            }
        }

        private void Step(int current, int total, string title)
        {
            Action<int, int, string> handler = OnProgress;
            if (handler != null)
            {
                handler(current, total, title);
            }
            Log(string.Format("[{0}/{1}] {2}", current, total, title), "step");
        }

        private Task<ProcessRunResult> RunProcessAsync(string fileName, string arguments, int timeoutMs, bool streamToLog)
        {
            return Task.Run(delegate()
            {
                ProcessRunResult res = new ProcessRunResult();
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = fileName;
                    psi.Arguments = arguments;
                    psi.WorkingDirectory = _config.WorkspaceDir;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                    psi.StandardErrorEncoding = Encoding.UTF8;

                    StringBuilder outSb = new StringBuilder();
                    StringBuilder errSb = new StringBuilder();

                    using (Process proc = new Process())
                    {
                        proc.StartInfo = psi;
                        proc.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                        {
                            if (e.Data != null)
                            {
                                outSb.AppendLine(e.Data);
                                if (streamToLog && !string.IsNullOrWhiteSpace(e.Data))
                                {
                                    Log(e.Data, "info");
                                }
                            }
                        };
                        proc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                        {
                            if (e.Data != null)
                            {
                                errSb.AppendLine(e.Data);
                                if (streamToLog && !string.IsNullOrWhiteSpace(e.Data))
                                {
                                    Log(e.Data, "warn");
                                }
                            }
                        };

                        proc.Start();
                        proc.BeginOutputReadLine();
                        proc.BeginErrorReadLine();

                        bool exited = proc.WaitForExit(timeoutMs);
                        if (!exited)
                        {
                            try { proc.Kill(); } catch { }
                            res.ExitCode = -1;
                            res.StdErr = "Timeout executando comando: " + fileName;
                        }
                        else
                        {
                            // Wait for async output buffers to drain
                            proc.WaitForExit();
                            res.ExitCode = proc.ExitCode;
                            res.StdOut = outSb.ToString();
                            res.StdErr = errSb.ToString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    res.ExitCode = -1;
                    res.StdErr = ex.Message;
                }
                return res;
            });
        }

        private Task<ProcessRunResult> RunRcloneAsync(string args, int timeoutMs, bool stream)
        {
            string exe = _config.GetRcloneExecutable();
            string cfgArgs = _config.GetRcloneConfigArgs();
            string fullArgs = string.IsNullOrEmpty(cfgArgs) ? args : (cfgArgs + " " + args);
            return RunProcessAsync(exe, fullArgs, timeoutMs, stream);
        }

        private static string FindDockerDesktopExe()
        {
            string[] candidates = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "Docker Desktop.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DockerDesktop", "Docker Desktop.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Docker", "Docker Desktop.exe")
            };
            foreach (string c in candidates)
            {
                if (!string.IsNullOrEmpty(c) && File.Exists(c))
                {
                    return c;
                }
            }
            return null;
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 16 * 1024 * 1024;
            return ser;
        }

        private static List<Dictionary<string, object>> ParseJsonLines(string text)
        {
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            JavaScriptSerializer ser = CreateSerializer();
            foreach (string line in text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    Dictionary<string, object> obj = ser.DeserializeObject(line.Trim()) as Dictionary<string, object>;
                    if (obj != null) list.Add(obj);
                }
                catch
                {
                }
            }
            return list;
        }

        private static string ExtractPlayitAddress(string logs)
        {
            if (string.IsNullOrEmpty(logs)) return "";
            MatchCollection matches = Regex.Matches(
                logs,
                @"([a-zA-Z0-9-]+(?:\.[a-zA-Z0-9-]+)*\.(?:joinmc\.link|at\.playit\.gg|playit\.plus|ply\.gg)(?::\d+)?)",
                RegexOptions.IgnoreCase
            );
            if (matches.Count > 0)
            {
                return matches[matches.Count - 1].Value;
            }
            return "";
        }

        public async Task<DockerStatusInfo> GetDockerStatusAsync()
        {
            DockerStatusInfo info = new DockerStatusInfo();
            ProcessRunResult verRes = await RunProcessAsync("docker", "info --format \"{{.ServerVersion}}\"", 5000, false).ConfigureAwait(false);
            if (!verRes.Ok)
            {
                info.DockerRunning = false;
                info.McStatusText = "Docker Desligado";
                info.PlayitStatusText = "Docker Desligado";
                return info;
            }

            info.DockerRunning = true;
            info.DockerVersion = verRes.StdOut.Trim();

            ProcessRunResult psRes = await RunProcessAsync("docker", "ps -a --filter name=mc_ --format \"{{json .}}\"", 6000, false).ConfigureAwait(false);
            List<Dictionary<string, object>> containers = ParseJsonLines(psRes.StdOut);

            foreach (Dictionary<string, object> c in containers)
            {
                object nameObj, stateObj, statusObj;
                string name = c.TryGetValue("Names", out nameObj) ? Convert.ToString(nameObj) : "";
                string state = c.TryGetValue("State", out stateObj) ? Convert.ToString(stateObj) : "";
                string status = c.TryGetValue("Status", out statusObj) ? Convert.ToString(statusObj) : "";

                if (string.Equals(name, "mc_server", StringComparison.OrdinalIgnoreCase))
                {
                    info.McExists = true;
                    info.McRunning = string.Equals(state, "running", StringComparison.OrdinalIgnoreCase);
                    info.McStatusText = status;
                }
                else if (string.Equals(name, "mc_playit", StringComparison.OrdinalIgnoreCase))
                {
                    info.PlayitExists = true;
                    info.PlayitRunning = string.Equals(state, "running", StringComparison.OrdinalIgnoreCase);
                    info.PlayitStatusText = status;
                }
            }

            List<Task> tasks = new List<Task>();
            if (info.McRunning)
            {
                tasks.Add(Task.Run(async delegate()
                {
                    ProcessRunResult statsRes = await RunProcessAsync("docker", "stats --no-stream --format \"{{json .}}\" mc_server", 6000, false).ConfigureAwait(false);
                    List<Dictionary<string, object>> parsed = ParseJsonLines(statsRes.StdOut);
                    if (parsed.Count > 0)
                    {
                        object cpuObj, memObj;
                        info.McCpu = parsed[0].TryGetValue("CPUPerc", out cpuObj) ? Convert.ToString(cpuObj) : "";
                        info.McMemory = parsed[0].TryGetValue("MemUsage", out memObj) ? Convert.ToString(memObj) : "";
                    }
                }));
                tasks.Add(Task.Run(async delegate()
                {
                    ProcessRunResult logRes = await RunProcessAsync("docker", "logs --tail 40 mc_server", 6000, false).ConfigureAwait(false);
                    string all = logRes.StdOut + "\n" + logRes.StdErr;
                    info.McReady = all.Contains("For help, type");
                }));
            }

            if (info.PlayitRunning)
            {
                tasks.Add(Task.Run(async delegate()
                {
                    ProcessRunResult logRes = await RunProcessAsync("docker", "logs --tail 40 mc_playit", 6000, false).ConfigureAwait(false);
                    info.PlayitAddress = ExtractPlayitAddress(logRes.StdOut + "\n" + logRes.StdErr);
                }));
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks.ToArray()).ConfigureAwait(false);
            }

            return info;
        }

        public async Task<string> GetContainerLogsAsync(string source, int tail)
        {
            if (source == "playit")
            {
                ProcessRunResult res = await RunProcessAsync("docker", string.Format("logs --tail {0} mc_playit", tail), 6000, false).ConfigureAwait(false);
                if (res.Ok) return (res.StdOut + res.StdErr).Trim();
                return "Container mc_playit não está ativo no momento.";
            }

            ProcessRunResult mcRes = await RunProcessAsync("docker", string.Format("logs --tail {0} mc_server", tail), 6000, false).ConfigureAwait(false);
            if (mcRes.Ok) return (mcRes.StdOut + mcRes.StdErr).Trim();

            string latestLog = Path.Combine(_config.DadosMineDir, "logs", "latest.log");
            if (File.Exists(latestLog))
            {
                string[] lines = File.ReadAllLines(latestLog, Encoding.UTF8);
                int start = Math.Max(0, lines.Length - tail);
                return string.Join(Environment.NewLine, lines, start, lines.Length - start);
            }
            return "Nenhum log do servidor disponível ainda.";
        }

        public async Task<CloudStatusInfo> GetCloudStatusAsync()
        {
            CloudStatusInfo info = new CloudStatusInfo();
            info.Remote = _config.GetRemoteName();
            string cloudBase = info.Remote + ":" + CloudFolder;

            if (File.Exists(_config.LevelDatFile))
            {
                info.LocalExists = true;
                info.LocalTime = File.GetLastWriteTime(_config.LevelDatFile);
            }

            ProcessRunResult aboutRes = await RunRcloneAsync(string.Format("about {0}: --json", info.Remote), 15000, false).ConfigureAwait(false);
            if (!aboutRes.Ok)
            {
                info.Authenticated = false;
                info.AuthError = !string.IsNullOrWhiteSpace(aboutRes.StdErr) ? aboutRes.StdErr.Trim() : "Token inválido ou rclone indisponível.";
                return info;
            }

            info.Authenticated = true;
            try
            {
                Dictionary<string, object> q = CreateSerializer().DeserializeObject(aboutRes.StdOut) as Dictionary<string, object>;
                if (q != null)
                {
                    object totObj, usedObj;
                    if (q.TryGetValue("total", out totObj) && totObj != null) info.QuotaTotal = Convert.ToInt64(totObj, CultureInfo.InvariantCulture);
                    if (q.TryGetValue("used", out usedObj) && usedObj != null) info.QuotaUsed = Convert.ToInt64(usedObj, CultureInfo.InvariantCulture);
                    if (info.QuotaTotal > 0)
                    {
                        info.QuotaTotalText = PlayerAndBackupService.FormatBytes(info.QuotaTotal);
                        info.QuotaUsedText = PlayerAndBackupService.FormatBytes(info.QuotaUsed);
                        info.QuotaPercent = Math.Min(100, (int)Math.Round((info.QuotaUsed * 100.0) / info.QuotaTotal));
                    }
                }
            }
            catch
            {
            }

            Task<ProcessRunResult> lockCheckTask = RunRcloneAsync(string.Format("lsjson \"{0}/lock.txt\"", cloudBase), 12000, false);
            Task<ProcessRunResult> levelCheckTask = RunRcloneAsync(string.Format("lsjson \"{0}/dados_mine/world/level.dat\"", cloudBase), 12000, false);
            await Task.WhenAll(lockCheckTask, levelCheckTask).ConfigureAwait(false);

            ProcessRunResult lockRes = lockCheckTask.Result;
            if (lockRes.Ok)
            {
                try
                {
                    object[] items = CreateSerializer().DeserializeObject(lockRes.StdOut) as object[];
                    if (items != null && items.Length > 0)
                    {
                        info.Locked = true;
                        ProcessRunResult catRes = await RunRcloneAsync(string.Format("cat \"{0}/lock.txt\"", cloudBase), 10000, false).ConfigureAwait(false);
                        if (catRes.Ok)
                        {
                            foreach (string line in catRes.StdOut.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                int eq = line.IndexOf('=');
                                if (eq <= 0) continue;
                                string k = line.Substring(0, eq).Trim().ToUpperInvariant();
                                string v = line.Substring(eq + 1).Trim();
                                if (k == "HOST") info.LockHost = v;
                                else if (k == "USER") info.LockUser = v;
                                else if (k == "STARTED_AT") info.LockStartedAt = v;
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            ProcessRunResult levelRes = levelCheckTask.Result;
            if (levelRes.Ok)
            {
                try
                {
                    object[] items = CreateSerializer().DeserializeObject(levelRes.StdOut) as object[];
                    if (items != null && items.Length > 0)
                    {
                        Dictionary<string, object> first = items[0] as Dictionary<string, object>;
                        object modObj;
                        if (first != null && first.TryGetValue("ModTime", out modObj) && modObj != null)
                        {
                            DateTime parsedTime;
                            if (DateTime.TryParse(Convert.ToString(modObj), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsedTime))
                            {
                                info.CloudExists = true;
                                info.CloudTime = parsedTime.ToLocalTime();
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            if (info.LocalExists && info.CloudExists && info.LocalTime.HasValue && info.CloudTime.HasValue)
            {
                double diffMin = Math.Abs((info.LocalTime.Value - info.CloudTime.Value).TotalMinutes);
                info.DiffMinutes = Math.Round(diffMin, 1);
                if (diffMin < 2.0)
                {
                    info.NewerSource = "equal";
                    info.HasConflict = false;
                }
                else
                {
                    info.NewerSource = info.LocalTime.Value > info.CloudTime.Value ? "local" : "cloud";
                    info.HasConflict = true;
                }
            }
            else if (info.LocalExists)
            {
                info.NewerSource = "local";
            }
            else if (info.CloudExists)
            {
                info.NewerSource = "cloud";
            }

            return info;
        }

        public async Task<List<BackupEntry>> ListCloudBackupsAsync()
        {
            List<BackupEntry> list = new List<BackupEntry>();
            string cloudBase = _config.GetRemoteName() + ":" + CloudFolder;
            ProcessRunResult res = await RunRcloneAsync(string.Format("lsjson \"{0}/Backups\"", cloudBase), 15000, false).ConfigureAwait(false);
            if (!res.Ok) return list;

            try
            {
                object[] items = CreateSerializer().DeserializeObject(res.StdOut) as object[];
                if (items != null)
                {
                    foreach (object item in items)
                    {
                        Dictionary<string, object> d = item as Dictionary<string, object>;
                        if (d == null) continue;
                        object isDirObj, nameObj, sizeObj, modObj;
                        bool isDir = d.TryGetValue("IsDir", out isDirObj) && Convert.ToBoolean(isDirObj);
                        string name = d.TryGetValue("Name", out nameObj) ? Convert.ToString(nameObj) : "";
                        if (isDir || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

                        BackupEntry b = new BackupEntry();
                        b.FileName = name;
                        b.SizeBytes = d.TryGetValue("Size", out sizeObj) ? Convert.ToInt64(sizeObj, CultureInfo.InvariantCulture) : 0;
                        b.SizeFormatted = PlayerAndBackupService.FormatBytes(b.SizeBytes);
                        DateTime dt;
                        if (d.TryGetValue("ModTime", out modObj) && DateTime.TryParse(Convert.ToString(modObj), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt))
                        {
                            b.ModifiedAt = dt.ToLocalTime();
                        }
                        b.Location = "Google Drive";
                        list.Add(b);
                    }
                }
                list.Sort(delegate(BackupEntry a, BackupEntry b) { return b.ModifiedAt.CompareTo(a.ModifiedAt); });
            }
            catch
            {
            }
            return list;
        }

        private async Task EnsureDockerRunningAsync()
        {
            ProcessRunResult check = await RunProcessAsync("docker", "info", 5000, false).ConfigureAwait(false);
            if (check.Ok)
            {
                Log("Docker Engine já está ativo e pronto.", "success");
                return;
            }

            string dockerExe = FindDockerDesktopExe();
            if (string.IsNullOrEmpty(dockerExe))
            {
                throw new InvalidOperationException("Docker Desktop não foi encontrado nos caminhos padrão. Abra o Docker Desktop manualmente.");
            }

            Log("Docker fechado. Iniciando Docker Desktop...", "info");
            Process.Start(dockerExe);

            int elapsed = 0;
            while (elapsed < 120000)
            {
                await Task.Delay(4000).ConfigureAwait(false);
                elapsed += 4000;
                ProcessRunResult ping = await RunProcessAsync("docker", "info", 5000, false).ConfigureAwait(false);
                if (ping.Ok)
                {
                    Log("Motor do Docker carregado com sucesso!", "success");
                    return;
                }
                Log("Aguardando motor do Docker carregar...", "info");
            }

            throw new TimeoutException("Tempo esgotado aguardando o Docker Desktop inicializar (120s).");
        }

        public async Task StartServerAsync(Func<CloudStatusInfo, MapConflictResolution> conflictPrompt, bool ignoreLock)
        {
            if (IsBusy) throw new InvalidOperationException("Já existe uma rotina em execução.");
            IsBusy = true;

            try
            {
                Step(1, 6, "Verificando autenticação do Google Drive");
                CloudStatusInfo cloud = await GetCloudStatusAsync().ConfigureAwait(false);
                if (!cloud.Authenticated)
                {
                    throw new InvalidOperationException("Token do Google Drive expirado ou inválido (" + cloud.AuthError + "). Use 'Reconectar Drive' na aba Backups & Nuvem.");
                }
                Log("Google Drive autenticado com sucesso.", "success");

                Step(2, 6, "Verificando motor do Docker");
                await EnsureDockerRunningAsync().ConfigureAwait(false);

                Step(3, 6, "Checando trava na nuvem e conflito de mapa");
                if (cloud.Locked && !ignoreLock)
                {
                    string owner = !string.IsNullOrEmpty(cloud.LockHost)
                        ? string.Format("{0} ({1})", cloud.LockHost, cloud.LockUser)
                        : "outro computador";
                    throw new InvalidOperationException("O servidor já está trancado em uso por " + owner + ". Se ninguém estiver jogando, remova a trava na aba Backups & Nuvem.");
                }

                bool keepLocal = false;
                if (cloud.HasConflict && conflictPrompt != null)
                {
                    Log("Diferença detectada entre save Local e save na Nuvem. Aguardando decisão...", "warn");
                    MapConflictResolution choice = conflictPrompt(cloud);
                    if (choice == MapConflictResolution.Cancel)
                    {
                        Log("Inicialização cancelada pelo usuário.", "warn");
                        return;
                    }
                    keepLocal = (choice == MapConflictResolution.KeepLocal);
                }

                Step(4, 6, "Limpando containers fantasmas e gravando trava lock.txt");
                await RunProcessAsync("docker", "compose down", 30000, true).ConfigureAwait(false);

                string lockBody = string.Format(
                    "HOST={0}\nUSER={1}\nSTARTED_AT={2}\nAPP=MineSyncPro\n",
                    Environment.MachineName,
                    Environment.UserName,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                );
                File.WriteAllText(_config.LockFile, lockBody, Encoding.UTF8);

                string cloudBase = cloud.Remote + ":" + CloudFolder;
                ProcessRunResult lockUp = await RunRcloneAsync(string.Format("copyto \"{0}\" \"{1}/lock.txt\"", _config.LockFile, cloudBase), 20000, true).ConfigureAwait(false);
                if (!lockUp.Ok)
                {
                    throw new InvalidOperationException("Falha ao enviar trava de segurança para o Google Drive: " + lockUp.StdErr);
                }
                Log("Trava lock.txt criada no Google Drive.", "success");

                Step(5, 6, "Sincronizando dados do mundo");
                if (keepLocal)
                {
                    Log("Download ignorado: carregando mapa LOCAL conforme sua escolha.", "warn");
                }
                else
                {
                    ProcessRunResult syncRes = await RunRcloneAsync(string.Format("sync \"{0}/dados_mine\" \"{1}\" -v", cloudBase, _config.DadosMineDir), 600000, true).ConfigureAwait(false);
                    if (!syncRes.Ok)
                    {
                        throw new InvalidOperationException("Falha ao baixar dados do mapa do Google Drive.");
                    }
                    Log("Mapa sincronizado da nuvem com sucesso.", "success");
                }

                Step(6, 6, "Subindo containers Docker (Minecraft + Playit)");
                ProcessRunResult upRes = await RunProcessAsync("docker", "compose up -d", 120000, true).ConfigureAwait(false);
                if (!upRes.Ok)
                {
                    throw new InvalidOperationException("Falha ao iniciar containers Docker: " + upRes.StdErr);
                }

                Log("CONTAINERS INICIADOS! Aguardando o servidor ficar pronto para jogar...", "success");
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task StopAndSaveServerAsync()
        {
            if (IsBusy) throw new InvalidOperationException("Já existe uma rotina em execução.");
            IsBusy = true;

            try
            {
                string remote = _config.GetRemoteName();
                string cloudBase = remote + ":" + CloudFolder;

                Step(1, 6, "Verificando autenticação do Google Drive");
                ProcessRunResult aboutRes = await RunRcloneAsync(string.Format("about {0}: --json", remote), 15000, false).ConfigureAwait(false);
                if (!aboutRes.Ok)
                {
                    throw new InvalidOperationException("Token do Google Drive expirado! O servidor CONTINUA LIGADO para proteger o progresso. Reconecte o Drive e tente parar novamente.");
                }

                Step(2, 6, "Salvando mundo e encerrando containers");
                DockerStatusInfo docker = await GetDockerStatusAsync().ConfigureAwait(false);
                if (docker.McRunning && docker.McReady)
                {
                    try
                    {
                        string rconRes = await RconClient.SendCommandAsync(_config, "save-all", 4000).ConfigureAwait(false);
                        Log("RCON save-all: " + rconRes, "info");
                        await Task.Delay(1500).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Log("Aviso RCON: " + ex.Message, "warn");
                    }
                }
                await RunProcessAsync("docker", "compose down", 60000, true).ConfigureAwait(false);

                Step(3, 6, "Enviando mapa atualizado para o Google Drive");
                ProcessRunResult syncRes = await RunRcloneAsync(string.Format("sync \"{0}\" \"{1}/dados_mine\" -v", _config.DadosMineDir, cloudBase), 600000, true).ConfigureAwait(false);
                if (!syncRes.Ok)
                {
                    throw new InvalidOperationException("Falha ao enviar o mapa para o Google Drive! Trava mantida por segurança.");
                }

                Step(4, 6, "Compactando backup histórico (.zip)");
                BackupEntry backup = await _backupService.CreateLocalBackupAsync("Backup_Mapa").ConfigureAwait(false);
                Log(string.Format("Backup criado: {0} ({1})", backup.FileName, backup.SizeFormatted), "success");

                Step(5, 6, "Enviando backup compactado para a nuvem");
                await RunRcloneAsync(string.Format("delete \"{0}/Backups/\" -v", cloudBase), 60000, true).ConfigureAwait(false);
                await RunRcloneAsync(string.Format("copy \"{0}\" \"{1}/Backups/\" -v", backup.FullPath, cloudBase), 300000, true).ConfigureAwait(false);

                Step(6, 6, "Removendo trava de segurança");
                await RunRcloneAsync(string.Format("delete \"{0}/lock.txt\"", cloudBase), 15000, true).ConfigureAwait(false);
                if (File.Exists(_config.LockFile)) File.Delete(_config.LockFile);

                Log("SINCRONIZACAO E BACKUP CONCLUIDOS!", "success");
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task EmergencySaveAsync()
        {
            if (IsBusy) throw new InvalidOperationException("Já existe uma rotina em execução.");
            IsBusy = true;

            try
            {
                string remote = _config.GetRemoteName();
                string cloudBase = remote + ":" + CloudFolder;

                Step(1, 4, "Verificando autenticação do Google Drive");
                ProcessRunResult aboutRes = await RunRcloneAsync(string.Format("about {0}: --json", remote), 15000, false).ConfigureAwait(false);
                if (!aboutRes.Ok)
                {
                    throw new InvalidOperationException("Token do Google Drive expirado! Reconecte antes de realizar o upload de emergência.");
                }

                Step(2, 4, "Verificando e derrubando containers ativos");
                DockerStatusInfo docker = await GetDockerStatusAsync().ConfigureAwait(false);
                if (docker.DockerRunning && (docker.McRunning || docker.PlayitRunning))
                {
                    await RunProcessAsync("docker", "compose down", 60000, true).ConfigureAwait(false);
                }

                Step(3, 4, "Forçando upload para o Drive e criando backup de emergência");
                ProcessRunResult syncRes = await RunRcloneAsync(string.Format("sync \"{0}\" \"{1}/dados_mine\" -v", _config.DadosMineDir, cloudBase), 600000, true).ConfigureAwait(false);
                if (!syncRes.Ok)
                {
                    throw new InvalidOperationException("Falha ao enviar dados para o Google Drive.");
                }

                BackupEntry backup = await _backupService.CreateLocalBackupAsync("Backup_Emergencia").ConfigureAwait(false);
                Log(string.Format("Backup de emergência criado: {0} ({1})", backup.FileName, backup.SizeFormatted), "success");

                await RunRcloneAsync(string.Format("delete \"{0}/Backups/\" -v", cloudBase), 60000, true).ConfigureAwait(false);
                await RunRcloneAsync(string.Format("copy \"{0}\" \"{1}/Backups/\" -v", backup.FullPath, cloudBase), 300000, true).ConfigureAwait(false);

                Step(4, 4, "Removendo travas de segurança");
                await RunRcloneAsync(string.Format("delete \"{0}/lock.txt\"", cloudBase), 15000, true).ConfigureAwait(false);
                if (File.Exists(_config.LockFile)) File.Delete(_config.LockFile);

                Log("SALVAMENTO FORCADO CONCLUIDO!", "success");
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task ForceKillAsync()
        {
            if (IsBusy) throw new InvalidOperationException("Já existe uma rotina em execução.");
            IsBusy = true;

            try
            {
                string remote = _config.GetRemoteName();
                string cloudBase = remote + ":" + CloudFolder;

                Step(1, 3, "Verificando conexão com Google Drive");
                ProcessRunResult aboutRes = await RunRcloneAsync(string.Format("about {0}:", remote), 10000, false).ConfigureAwait(false);
                if (!aboutRes.Ok)
                {
                    Log("Drive desconectado: a trava na nuvem poderá precisar ser removida manualmente.", "warn");
                }

                Step(2, 3, "Derrubando containers imediatamente sem salvar");
                await RunProcessAsync("docker", "compose down", 60000, true).ConfigureAwait(false);

                Step(3, 3, "Removendo travas de segurança");
                await RunRcloneAsync(string.Format("delete \"{0}/lock.txt\"", cloudBase), 15000, false).ConfigureAwait(false);
                if (File.Exists(_config.LockFile)) File.Delete(_config.LockFile);

                Log("SERVIDOR FECHADO! (Nenhum dado salvo)", "warn");
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task ForceUnlockCloudAsync()
        {
            string cloudBase = _config.GetRemoteName() + ":" + CloudFolder;
            ProcessRunResult res = await RunRcloneAsync(string.Format("delete \"{0}/lock.txt\"", cloudBase), 15000, true).ConfigureAwait(false);
            if (File.Exists(_config.LockFile)) File.Delete(_config.LockFile);
            if (!res.Ok)
            {
                throw new InvalidOperationException("Não foi possível remover a trava na nuvem: " + res.StdErr);
            }
            Log("Trava lock.txt removida com sucesso!", "success");
        }

        public void OpenDriveReconnectWindow()
        {
            string exe = _config.GetRcloneExecutable();
            string cfgArgs = _config.GetRcloneConfigArgs();
            string remote = _config.GetRemoteName();
            string args = string.IsNullOrEmpty(cfgArgs)
                ? string.Format("config reconnect {0}:", remote)
                : string.Format("{0} config reconnect {1}:", cfgArgs, remote);

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exe;
            psi.Arguments = args;
            psi.WorkingDirectory = _config.WorkspaceDir;
            psi.UseShellExecute = true;
            Process.Start(psi);
        }
    }
}
