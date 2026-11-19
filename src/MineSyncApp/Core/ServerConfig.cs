using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MineSyncApp.Core
{
    public class DockerSettings
    {
        public string Memory { get; set; }
        public string Type { get; set; }
        public string Version { get; set; }
        public string OnlineMode { get; set; }
        public bool PlayitConfigured { get; set; }
        public string PlayitKeyMasked { get; set; }
        public int PlayitPort { get; set; }

        public DockerSettings()
        {
            Memory = "4G";
            Type = "VANILLA";
            Version = "latest";
            OnlineMode = "TRUE";
            PlayitPort = 25565;
            PlayitKeyMasked = "";
        }
    }

    public class ServerConfig
    {
        public string WorkspaceDir { get; private set; }

        public static string AppDataConfigDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MineSyncPro"
                );
            }
        }

        public static string WorkspacePointerFile
        {
            get { return Path.Combine(AppDataConfigDir, "workspace.txt"); }
        }

        public static string BackupSettingsFile
        {
            get { return Path.Combine(AppDataConfigDir, "backup_settings.txt"); }
        }

        public string DadosMineDir { get { return Path.Combine(WorkspaceDir, "dados_mine"); } }
        public string WorldDir { get { return Path.Combine(DadosMineDir, "world"); } }
        public string LevelDatFile { get { return Path.Combine(WorldDir, "level.dat"); } }
        public string BackupsDir { get; private set; }
        public bool LocalBackupEnabled { get; private set; }
        public string PlayitTomlFile { get { return Path.Combine(WorkspaceDir, "playit_config", "playit.toml"); } }
        public string RcloneExe { get { return Path.Combine(WorkspaceDir, "rclone.exe"); } }
        public string RcloneConf { get { return Path.Combine(WorkspaceDir, "rclone.conf"); } }
        public string DockerComposeFile { get { return Path.Combine(WorkspaceDir, "docker-compose.yml"); } }
        public string EnvFile { get { return Path.Combine(WorkspaceDir, ".env"); } }
        public string ServerPropertiesFile { get { return Path.Combine(DadosMineDir, "server.properties"); } }
        public string LockFile { get { return Path.Combine(WorkspaceDir, "lock.txt"); } }

        public ServerConfig()
        {
            WorkspaceDir = DiscoverWorkspaceDir();
            LoadBackupPreferences();
        }

        public static string GetDefaultBackupsDir(string workspaceDir)
        {
            try
            {
                if (Directory.Exists(@"E:\"))
                {
                    return @"E:\BACKUPS SERVER MINE";
                }
            }
            catch
            {
            }
            return Path.Combine(workspaceDir, "Backups");
        }

        private void LoadBackupPreferences()
        {
            BackupsDir = GetDefaultBackupsDir(WorkspaceDir);
            LocalBackupEnabled = true;

            try
            {
                if (File.Exists(BackupSettingsFile))
                {
                    foreach (string line in File.ReadAllLines(BackupSettingsFile, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = line.Substring(0, eq).Trim().ToUpperInvariant();
                        string val = line.Substring(eq + 1).Trim();
                        if (key == "BACKUPS_DIR" && !string.IsNullOrEmpty(val))
                        {
                            BackupsDir = val;
                        }
                        else if (key == "LOCAL_BACKUP_ENABLED")
                        {
                            LocalBackupEnabled = !string.Equals(val, "false", StringComparison.OrdinalIgnoreCase) && val != "0";
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public void SetBackupPreferences(string backupsDir, bool localBackupEnabled)
        {
            if (!string.IsNullOrWhiteSpace(backupsDir))
            {
                BackupsDir = Path.GetFullPath(backupsDir.Trim());
            }
            LocalBackupEnabled = localBackupEnabled;

            try
            {
                Directory.CreateDirectory(AppDataConfigDir);
                string content = string.Format(
                    "BACKUPS_DIR={0}\r\nLOCAL_BACKUP_ENABLED={1}\r\n",
                    BackupsDir,
                    LocalBackupEnabled ? "true" : "false"
                );
                File.WriteAllText(BackupSettingsFile, content, Encoding.UTF8);
            }
            catch
            {
            }
        }

        public static bool IsValidWorkspace(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                return false;
            }
            return File.Exists(Path.Combine(dir, "docker-compose.yml")) ||
                   Directory.Exists(Path.Combine(dir, "dados_mine"));
        }

        public static string DiscoverWorkspaceDir()
        {
            List<string> candidates = new List<string>();
            candidates.Add(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/'));
            candidates.Add(Environment.CurrentDirectory.TrimEnd('\\', '/'));

            try
            {
                if (File.Exists(WorkspacePointerFile))
                {
                    string saved = File.ReadAllText(WorkspacePointerFile, Encoding.UTF8).Trim();
                    if (!string.IsNullOrEmpty(saved))
                    {
                        candidates.Add(saved);
                    }
                }
            }
            catch
            {
            }

            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            candidates.Add(Path.Combine(docs, "Minecraft-Server"));
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            candidates.Add(Path.Combine(desktop, "Minecraft-Server"));

            foreach (string c in candidates)
            {
                if (IsValidWorkspace(c))
                {
                    return Path.GetFullPath(c);
                }
            }

            return Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        }

        public void SetWorkspaceDir(string newDir)
        {
            string full = Path.GetFullPath(newDir);
            WorkspaceDir = full;
            SaveWorkspacePointer(full);
        }

        public static void SaveWorkspacePointer(string dir)
        {
            Directory.CreateDirectory(AppDataConfigDir);
            File.WriteAllText(WorkspacePointerFile, dir, Encoding.UTF8);
        }

        public string GetRemoteName()
        {
            try
            {
                if (File.Exists(RcloneConf))
                {
                    string content = File.ReadAllText(RcloneConf, Encoding.UTF8);
                    Match m = Regex.Match(content, @"^\s*\[([^\]]+)\]", RegexOptions.Multiline);
                    if (m.Success)
                    {
                        return m.Groups[1].Value.Trim();
                    }
                }
            }
            catch
            {
            }
            return "gdrive0";
        }

        public string GetRcloneExecutable()
        {
            return File.Exists(RcloneExe) ? RcloneExe : "rclone";
        }

        public string GetRcloneConfigArgs()
        {
            if (File.Exists(RcloneConf))
            {
                return string.Format("--config \"{0}\"", RcloneConf);
            }
            return "";
        }

        public Dictionary<string, string> ReadServerProperties()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(ServerPropertiesFile))
            {
                return map;
            }

            string[] lines = File.ReadAllLines(ServerPropertiesFile, Encoding.UTF8);
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                {
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1);
                map[key] = val;
            }
            return map;
        }

        public void WriteServerProperties(Dictionary<string, string> updates)
        {
            if (updates == null) return;

            bool exists = File.Exists(ServerPropertiesFile);
            string raw = exists ? File.ReadAllText(ServerPropertiesFile, Encoding.UTF8) : "#Minecraft server properties";
            string eol = raw.Contains("\r\n") ? "\r\n" : "\n";
            string[] lines = raw.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);

            Dictionary<string, string> remaining = new Dictionary<string, string>(updates, StringComparer.OrdinalIgnoreCase);
            List<string> output = new List<string>(lines.Length + remaining.Count);

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                {
                    output.Add(line);
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    output.Add(line);
                    continue;
                }
                string key = line.Substring(0, eq).Trim();
                string newVal;
                if (remaining.TryGetValue(key, out newVal))
                {
                    output.Add(key + "=" + (newVal ?? ""));
                    remaining.Remove(key);
                }
                else
                {
                    output.Add(line);
                }
            }

            foreach (KeyValuePair<string, string> kv in remaining)
            {
                output.Add(kv.Key + "=" + (kv.Value ?? ""));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ServerPropertiesFile));
            File.WriteAllText(ServerPropertiesFile, string.Join(eol, output.ToArray()), Encoding.UTF8);
        }

        private Dictionary<string, string> ReadEnvMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(EnvFile)) return map;

            foreach (string line in File.ReadAllLines(EnvFile, Encoding.UTF8))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;
                map[trimmed.Substring(0, eq).Trim()] = trimmed.Substring(eq + 1).Trim();
            }
            return map;
        }

        private static string ExtractComposeVar(string yaml, string envName, string propName, string fallback)
        {
            Match m1 = Regex.Match(yaml, string.Format(@"{0}:\s*[""']?\$\{{{1}:-([^}}""']+)[}}][""']?", propName, envName));
            if (m1.Success) return m1.Groups[1].Value.Trim();

            Match m2 = Regex.Match(yaml, string.Format(@"{0}:\s*[""']?([^\s""']+)[""']?", propName));
            if (m2.Success) return m2.Groups[1].Value.Trim();

            return fallback;
        }

        private static string ReplaceComposeVar(string yaml, string envName, string propName, string nextVal)
        {
            string pattern1 = string.Format(@"({0}:\s*[""']?\$\{{{1}:-)([^}}""']+)(}}?[""']?)", propName, envName);
            if (Regex.IsMatch(yaml, pattern1))
            {
                return Regex.Replace(yaml, pattern1, "${1}" + nextVal + "${3}");
            }
            string pattern2 = string.Format(@"({0}:\s*)([""']?)([^\s""']+)([""']?)", propName);
            if (Regex.IsMatch(yaml, pattern2))
            {
                return Regex.Replace(yaml, pattern2, "${1}\"" + nextVal + "\"");
            }
            return yaml;
        }

        public DockerSettings ReadDockerConfig()
        {
            DockerSettings settings = new DockerSettings();
            Dictionary<string, string> env = ReadEnvMap();
            string yaml = File.Exists(DockerComposeFile)
                ? File.ReadAllText(DockerComposeFile, Encoding.UTF8)
                : "";

            string val;
            settings.Memory = env.TryGetValue("MC_MEMORY", out val) ? val : ExtractComposeVar(yaml, "MC_MEMORY", "MEMORY", "4G");
            settings.Type = env.TryGetValue("MC_TYPE", out val) ? val : ExtractComposeVar(yaml, "MC_TYPE", "TYPE", "VANILLA");
            settings.Version = env.TryGetValue("MC_VERSION", out val) ? val : ExtractComposeVar(yaml, "MC_VERSION", "VERSION", "latest");
            settings.OnlineMode = env.TryGetValue("MC_ONLINE_MODE", out val) ? val : ExtractComposeVar(yaml, "MC_ONLINE_MODE", "ONLINE_MODE", "TRUE");

            if (File.Exists(PlayitTomlFile))
            {
                string toml = File.ReadAllText(PlayitTomlFile, Encoding.UTF8);
                Match km = Regex.Match(toml, @"secret_key\s*=\s*""([^""]+)""");
                Match pm = Regex.Match(toml, @"local_port\s*=\s*(\d+)");
                if (km.Success)
                {
                    string rawKey = km.Groups[1].Value;
                    settings.PlayitConfigured = !string.IsNullOrEmpty(rawKey);
                    settings.PlayitKeyMasked = rawKey.Length > 12
                        ? rawKey.Substring(0, 6) + "..." + rawKey.Substring(rawKey.Length - 6)
                        : rawKey;
                }
                int portParsed;
                if (pm.Success && int.TryParse(pm.Groups[1].Value, out portParsed))
                {
                    settings.PlayitPort = portParsed;
                }
            }

            return settings;
        }

        public void WriteDockerConfig(DockerSettings settings)
        {
            if (settings == null) return;

            if (File.Exists(DockerComposeFile))
            {
                string yaml = File.ReadAllText(DockerComposeFile, Encoding.UTF8);
                yaml = ReplaceComposeVar(yaml, "MC_MEMORY", "MEMORY", settings.Memory.Trim());
                yaml = ReplaceComposeVar(yaml, "MC_TYPE", "TYPE", settings.Type.Trim());
                yaml = ReplaceComposeVar(yaml, "MC_VERSION", "VERSION", settings.Version.Trim());
                yaml = ReplaceComposeVar(yaml, "MC_ONLINE_MODE", "ONLINE_MODE", settings.OnlineMode.Trim());
                File.WriteAllText(DockerComposeFile, yaml, Encoding.UTF8);
            }

            if (File.Exists(EnvFile))
            {
                string raw = File.ReadAllText(EnvFile, Encoding.UTF8);
                string eol = raw.Contains("\r\n") ? "\r\n" : "\n";
                string[] lines = raw.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
                Dictionary<string, string> updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                updates["MC_MEMORY"] = settings.Memory.Trim();
                updates["MC_TYPE"] = settings.Type.Trim();
                updates["MC_VERSION"] = settings.Version.Trim();
                updates["MC_ONLINE_MODE"] = settings.OnlineMode.Trim();

                List<string> nextLines = new List<string>();
                foreach (string line in lines)
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        nextLines.Add(line);
                        continue;
                    }
                    string k = line.Substring(0, eq).Trim();
                    string v;
                    if (updates.TryGetValue(k, out v))
                    {
                        nextLines.Add(k + "=" + v);
                        updates.Remove(k);
                    }
                    else
                    {
                        nextLines.Add(line);
                    }
                }
                foreach (KeyValuePair<string, string> kv in updates)
                {
                    nextLines.Add(kv.Key + "=" + kv.Value);
                }
                File.WriteAllText(EnvFile, string.Join(eol, nextLines.ToArray()), Encoding.UTF8);
            }
        }
    }
}
