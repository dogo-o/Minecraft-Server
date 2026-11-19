using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MineSyncApp.Core
{
    public class PlayerInfo
    {
        public string Uuid { get; set; }
        public string Name { get; set; }
        public bool Online { get; set; }
        public bool IsOp { get; set; }
        public int OpLevel { get; set; }
        public bool Whitelisted { get; set; }
        public bool Banned { get; set; }
        public bool HasStats { get; set; }
        public long PlayTimeTicks { get; set; }
        public string PlayTimeFormatted { get; set; }
        public long Deaths { get; set; }
        public long MobKills { get; set; }
        public long BlocksMined { get; set; }
        public long ItemsCrafted { get; set; }
        public double DistanceKm { get; set; }

        public PlayerInfo()
        {
            Uuid = "";
            Name = "";
            PlayTimeFormatted = "0m";
        }
    }

    public class BackupEntry
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public long SizeBytes { get; set; }
        public string SizeFormatted { get; set; }
        public DateTime ModifiedAt { get; set; }
        public string Location { get; set; }

        public BackupEntry()
        {
            FileName = "";
            FullPath = "";
            SizeFormatted = "0 B";
            Location = "Local";
        }
    }

    public class PlayerAndBackupService
    {
        private readonly ServerConfig _config;

        public PlayerAndBackupService(ServerConfig config)
        {
            _config = config;
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 32 * 1024 * 1024;
            return ser;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            if (bytes < 1024L * 1024L * 1024L) return (bytes / (1024.0 * 1024.0)).ToString("0.00", CultureInfo.InvariantCulture) + " MB";
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }

        private static string FormatPlayTime(long ticks)
        {
            long totalSeconds = ticks / 20;
            long hours = totalSeconds / 3600;
            long minutes = (totalSeconds % 3600) / 60;
            if (hours > 0)
            {
                return string.Format("{0}h {1}m", hours, minutes);
            }
            return string.Format("{0}m", minutes);
        }

        private static List<Dictionary<string, object>> ReadJsonArray(string path)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            if (!File.Exists(path)) return result;

            try
            {
                string raw = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(raw)) return result;
                object parsed = CreateSerializer().DeserializeObject(raw);
                object[] arr = parsed as object[];
                if (arr != null)
                {
                    foreach (object item in arr)
                    {
                        Dictionary<string, object> dict = item as Dictionary<string, object>;
                        if (dict != null) result.Add(dict);
                    }
                }
            }
            catch
            {
            }
            return result;
        }

        private static void WriteJsonArray(string path, List<Dictionary<string, object>> items)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string json = CreateSerializer().Serialize(items);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        private static string GetStr(Dictionary<string, object> dict, string key)
        {
            if (dict == null) return "";
            object val;
            if (dict.TryGetValue(key, out val) && val != null)
            {
                return Convert.ToString(val, CultureInfo.InvariantCulture);
            }
            return "";
        }

        private static long GetLong(Dictionary<string, object> dict, string key)
        {
            if (dict == null) return 0;
            object val;
            if (dict.TryGetValue(key, out val) && val != null)
            {
                try
                {
                    return Convert.ToInt64(val, CultureInfo.InvariantCulture);
                }
                catch
                {
                    return 0;
                }
            }
            return 0;
        }

        private static long SumDictValues(Dictionary<string, object> dict)
        {
            if (dict == null) return 0;
            long total = 0;
            foreach (KeyValuePair<string, object> kv in dict)
            {
                if (kv.Value != null)
                {
                    try
                    {
                        total += Convert.ToInt64(kv.Value, CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                    }
                }
            }
            return total;
        }

        private void PopulateStats(PlayerInfo player)
        {
            string statsPath = Path.Combine(_config.WorldDir, "players", "stats", player.Uuid + ".json");
            if (!File.Exists(statsPath)) return;

            try
            {
                string raw = File.ReadAllText(statsPath, Encoding.UTF8);
                Dictionary<string, object> root = CreateSerializer().DeserializeObject(raw) as Dictionary<string, object>;
                if (root == null) return;

                object statsObj;
                if (!root.TryGetValue("stats", out statsObj)) return;
                Dictionary<string, object> stats = statsObj as Dictionary<string, object>;
                if (stats == null) return;

                player.HasStats = true;

                object customObj;
                if (stats.TryGetValue("minecraft:custom", out customObj))
                {
                    Dictionary<string, object> custom = customObj as Dictionary<string, object>;
                    if (custom != null)
                    {
                        player.PlayTimeTicks = GetLong(custom, "minecraft:play_time");
                        player.PlayTimeFormatted = FormatPlayTime(player.PlayTimeTicks);
                        player.Deaths = GetLong(custom, "minecraft:deaths");
                        player.MobKills = GetLong(custom, "minecraft:mob_kills");

                        long distCm =
                            GetLong(custom, "minecraft:walk_one_cm") +
                            GetLong(custom, "minecraft:sprint_one_cm") +
                            GetLong(custom, "minecraft:aviate_one_cm") +
                            GetLong(custom, "minecraft:boat_one_cm") +
                            GetLong(custom, "minecraft:swim_one_cm");
                        player.DistanceKm = Math.Round(distCm / 100000.0, 1);
                    }
                }

                object minedObj;
                if (stats.TryGetValue("minecraft:mined", out minedObj))
                {
                    player.BlocksMined = SumDictValues(minedObj as Dictionary<string, object>);
                }

                object craftedObj;
                if (stats.TryGetValue("minecraft:crafted", out craftedObj))
                {
                    player.ItemsCrafted = SumDictValues(craftedObj as Dictionary<string, object>);
                }
            }
            catch
            {
            }
        }

        public async Task<List<PlayerInfo>> GetPlayersAsync(bool queryOnlineRcon)
        {
            List<Dictionary<string, object>> usercache = ReadJsonArray(Path.Combine(_config.DadosMineDir, "usercache.json"));
            List<Dictionary<string, object>> ops = ReadJsonArray(Path.Combine(_config.DadosMineDir, "ops.json"));
            List<Dictionary<string, object>> whitelist = ReadJsonArray(Path.Combine(_config.DadosMineDir, "whitelist.json"));
            List<Dictionary<string, object>> banned = ReadJsonArray(Path.Combine(_config.DadosMineDir, "banned-players.json"));

            Dictionary<string, Dictionary<string, object>> opMap = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, object> o in ops) opMap[GetStr(o, "uuid")] = o;

            Dictionary<string, Dictionary<string, object>> wlMap = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, object> w in whitelist) wlMap[GetStr(w, "uuid")] = w;

            Dictionary<string, Dictionary<string, object>> banMap = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, object> b in banned) banMap[GetStr(b, "uuid")] = b;

            Dictionary<string, PlayerInfo> byUuid = new Dictionary<string, PlayerInfo>(StringComparer.OrdinalIgnoreCase);

            Action<string, string> ensure = delegate(string uuid, string name)
            {
                if (string.IsNullOrWhiteSpace(uuid)) return;
                PlayerInfo p;
                if (!byUuid.TryGetValue(uuid, out p))
                {
                    p = new PlayerInfo();
                    p.Uuid = uuid;
                    p.Name = !string.IsNullOrWhiteSpace(name) ? name : uuid.Substring(0, Math.Min(8, uuid.Length));
                    Dictionary<string, object> opInfo;
                    if (opMap.TryGetValue(uuid, out opInfo))
                    {
                        p.IsOp = true;
                        p.OpLevel = (int)GetLong(opInfo, "level");
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            string opName = GetStr(opInfo, "name");
                            if (!string.IsNullOrWhiteSpace(opName)) p.Name = opName;
                        }
                    }
                    p.Whitelisted = wlMap.ContainsKey(uuid);
                    p.Banned = banMap.ContainsKey(uuid);
                    PopulateStats(p);
                    byUuid[uuid] = p;
                }
            };

            foreach (Dictionary<string, object> u in usercache) ensure(GetStr(u, "uuid"), GetStr(u, "name"));
            foreach (Dictionary<string, object> o in ops) ensure(GetStr(o, "uuid"), GetStr(o, "name"));
            foreach (Dictionary<string, object> w in whitelist) ensure(GetStr(w, "uuid"), GetStr(w, "name"));
            foreach (Dictionary<string, object> b in banned) ensure(GetStr(b, "uuid"), GetStr(b, "name"));

            string statsDir = Path.Combine(_config.WorldDir, "players", "stats");
            if (Directory.Exists(statsDir))
            {
                foreach (string file in Directory.GetFiles(statsDir, "*.json"))
                {
                    ensure(Path.GetFileNameWithoutExtension(file), null);
                }
            }

            // Deduplicate stale legacy usercache entries with identical names
            Dictionary<string, PlayerInfo> byName = new Dictionary<string, PlayerInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (PlayerInfo p in byUuid.Values)
            {
                PlayerInfo existing;
                if (!byName.TryGetValue(p.Name, out existing))
                {
                    byName[p.Name] = p;
                }
                else
                {
                    double scoreNew = (p.HasStats ? 100 : 0) + (p.IsOp ? 50 : 0) + (p.Whitelisted ? 20 : 0) + (p.PlayTimeTicks / 100000.0);
                    double scoreOld = (existing.HasStats ? 100 : 0) + (existing.IsOp ? 50 : 0) + (existing.Whitelisted ? 20 : 0) + (existing.PlayTimeTicks / 100000.0);
                    if (scoreNew > scoreOld)
                    {
                        byName[p.Name] = p;
                    }
                }
            }

            List<PlayerInfo> players = new List<PlayerInfo>(byName.Values);

            if (queryOnlineRcon)
            {
                try
                {
                    string listResp = await RconClient.SendCommandAsync(_config, "list", 2500).ConfigureAwait(false);
                    Match m = Regex.Match(listResp, @"There are \d+ of a max(?:imum)? of \d+ players online:?(.*)", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        HashSet<string> onlineSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        string[] parts = m.Groups[1].Value.Split(',');
                        foreach (string part in parts)
                        {
                            string trimmed = part.Trim();
                            if (!string.IsNullOrEmpty(trimmed)) onlineSet.Add(trimmed);
                        }
                        foreach (PlayerInfo p in players)
                        {
                            p.Online = onlineSet.Contains(p.Name);
                        }
                    }
                }
                catch
                {
                }
            }

            players.Sort(delegate(PlayerInfo a, PlayerInfo b)
            {
                if (a.Online != b.Online) return a.Online ? -1 : 1;
                int cmpTime = b.PlayTimeTicks.CompareTo(a.PlayTimeTicks);
                if (cmpTime != 0) return cmpTime;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            return players;
        }

        public async Task<string> PerformPlayerActionAsync(string action, PlayerInfo player, string value, bool serverOnline)
        {
            if (player == null || string.IsNullOrWhiteSpace(player.Name))
            {
                throw new ArgumentException("Jogador inválido.");
            }

            if (action == "op")
            {
                if (serverOnline)
                {
                    return await RconClient.SendCommandAsync(_config, "op " + player.Name, 4000).ConfigureAwait(false);
                }
                string path = Path.Combine(_config.DadosMineDir, "ops.json");
                List<Dictionary<string, object>> ops = ReadJsonArray(path);
                bool exists = ops.Exists(delegate(Dictionary<string, object> o) { return string.Equals(GetStr(o, "uuid"), player.Uuid, StringComparison.OrdinalIgnoreCase); });
                if (!exists)
                {
                    Dictionary<string, object> entry = new Dictionary<string, object>();
                    entry["uuid"] = player.Uuid;
                    entry["name"] = player.Name;
                    entry["level"] = 4;
                    entry["bypassesPlayerLimit"] = false;
                    ops.Add(entry);
                    WriteJsonArray(path, ops);
                }
                return player.Name + " adicionado ao ops.json (servidor offline).";
            }

            if (action == "deop")
            {
                if (serverOnline)
                {
                    return await RconClient.SendCommandAsync(_config, "deop " + player.Name, 4000).ConfigureAwait(false);
                }
                string path = Path.Combine(_config.DadosMineDir, "ops.json");
                List<Dictionary<string, object>> ops = ReadJsonArray(path);
                ops.RemoveAll(delegate(Dictionary<string, object> o)
                {
                    return string.Equals(GetStr(o, "uuid"), player.Uuid, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(GetStr(o, "name"), player.Name, StringComparison.OrdinalIgnoreCase);
                });
                WriteJsonArray(path, ops);
                return player.Name + " removido do ops.json (servidor offline).";
            }

            if (action == "whitelist_add")
            {
                if (serverOnline)
                {
                    return await RconClient.SendCommandAsync(_config, "whitelist add " + player.Name, 4000).ConfigureAwait(false);
                }
                string path = Path.Combine(_config.DadosMineDir, "whitelist.json");
                List<Dictionary<string, object>> wl = ReadJsonArray(path);
                bool exists = wl.Exists(delegate(Dictionary<string, object> w) { return string.Equals(GetStr(w, "uuid"), player.Uuid, StringComparison.OrdinalIgnoreCase); });
                if (!exists)
                {
                    Dictionary<string, object> entry = new Dictionary<string, object>();
                    entry["uuid"] = player.Uuid;
                    entry["name"] = player.Name;
                    wl.Add(entry);
                    WriteJsonArray(path, wl);
                }
                return player.Name + " adicionado à whitelist.json (servidor offline).";
            }

            if (action == "whitelist_remove")
            {
                if (serverOnline)
                {
                    return await RconClient.SendCommandAsync(_config, "whitelist remove " + player.Name, 4000).ConfigureAwait(false);
                }
                string path = Path.Combine(_config.DadosMineDir, "whitelist.json");
                List<Dictionary<string, object>> wl = ReadJsonArray(path);
                wl.RemoveAll(delegate(Dictionary<string, object> w)
                {
                    return string.Equals(GetStr(w, "uuid"), player.Uuid, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(GetStr(w, "name"), player.Name, StringComparison.OrdinalIgnoreCase);
                });
                WriteJsonArray(path, wl);
                return player.Name + " removido da whitelist.json (servidor offline).";
            }

            if (!serverOnline)
            {
                throw new InvalidOperationException("Esta ação requer que o servidor Minecraft esteja ligado e pronto.");
            }

            if (action == "kick")
            {
                string reason = string.IsNullOrWhiteSpace(value) ? "Expulso pelo MineSync Pro" : value;
                return await RconClient.SendCommandAsync(_config, "kick " + player.Name + " " + reason, 4000).ConfigureAwait(false);
            }

            if (action == "gamemode")
            {
                return await RconClient.SendCommandAsync(_config, "gamemode " + value + " " + player.Name, 4000).ConfigureAwait(false);
            }

            if (action == "tell")
            {
                return await RconClient.SendCommandAsync(_config, "tell " + player.Name + " " + value, 4000).ConfigureAwait(false);
            }

            throw new ArgumentException("Ação desconhecida: " + action);
        }

        public List<BackupEntry> ListLocalBackups()
        {
            List<BackupEntry> list = new List<BackupEntry>();
            if (!Directory.Exists(_config.BackupsDir)) return list;

            foreach (string file in Directory.GetFiles(_config.BackupsDir, "*.zip"))
            {
                FileInfo fi = new FileInfo(file);
                BackupEntry entry = new BackupEntry();
                entry.FileName = fi.Name;
                entry.FullPath = fi.FullName;
                entry.SizeBytes = fi.Length;
                entry.SizeFormatted = FormatBytes(fi.Length);
                entry.ModifiedAt = fi.LastWriteTime;
                entry.Location = "Local";
                list.Add(entry);
            }

            list.Sort(delegate(BackupEntry a, BackupEntry b) { return b.ModifiedAt.CompareTo(a.ModifiedAt); });
            return list;
        }

        public Task<BackupEntry> CreateLocalBackupAsync(string prefix)
        {
            return CreateLocalBackupAsync(prefix, _config.BackupsDir);
        }

        public async Task<BackupEntry> CreateLocalBackupAsync(string prefix, string targetDirectory)
        {
            if (!Directory.Exists(_config.DadosMineDir))
            {
                throw new DirectoryNotFoundException("Diretório dados_mine não encontrado para backup.");
            }
            string outDir = string.IsNullOrWhiteSpace(targetDirectory) ? _config.BackupsDir : targetDirectory;
            Directory.CreateDirectory(outDir);

            string safePrefix = Regex.Replace(prefix ?? "Backup_Manual", @"[^a-zA-Z0-9_-]", "_");
            string fileName = string.Format("{0}_{1}.zip", safePrefix, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            string zipPath = Path.Combine(outDir, fileName);

            await Task.Run(delegate()
            {
                ZipFile.CreateFromDirectory(_config.DadosMineDir, zipPath, CompressionLevel.Optimal, false);
            }).ConfigureAwait(false);

            FileInfo fi = new FileInfo(zipPath);
            BackupEntry entry = new BackupEntry();
            entry.FileName = fi.Name;
            entry.FullPath = fi.FullName;
            entry.SizeBytes = fi.Length;
            entry.SizeFormatted = FormatBytes(fi.Length);
            entry.ModifiedAt = fi.LastWriteTime;
            entry.Location = "Local";
            return entry;
        }

        public async Task RestoreLocalBackupAsync(string zipFullPath, bool isServerRunning)
        {
            if (isServerRunning)
            {
                throw new InvalidOperationException("Pare o servidor Minecraft antes de restaurar um backup.");
            }
            if (string.IsNullOrWhiteSpace(zipFullPath) || !File.Exists(zipFullPath))
            {
                throw new FileNotFoundException("Arquivo de backup não encontrado.");
            }

            string targetRoot = Path.GetFullPath(_config.DadosMineDir);
            Directory.CreateDirectory(targetRoot);

            await Task.Run(delegate()
            {
                using (ZipArchive archive = ZipFile.OpenRead(zipFullPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;

                        string destPath = Path.GetFullPath(Path.Combine(targetRoot, entry.FullName));
                        if (!destPath.StartsWith(targetRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(destPath, targetRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        string parent = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(parent))
                        {
                            Directory.CreateDirectory(parent);
                        }
                        entry.ExtractToFile(destPath, true);
                    }
                }
            }).ConfigureAwait(false);
        }

        public void DeleteLocalBackup(string zipFullPath)
        {
            if (!string.IsNullOrWhiteSpace(zipFullPath) && File.Exists(zipFullPath))
            {
                File.Delete(zipFullPath);
            }
        }
    }
}
