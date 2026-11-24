using System;
using System.Collections.Generic;
using System.IO;

namespace MiniFanControl
{
    /// <summary>
    /// Data-driven external hardware profile registry loading community-contributed JSON profiles
    /// from the 'profiles/' directory (next to the binary or under %LocalAppData%\MiniFanControl\profiles).
    /// Enforces fail-closed DMI binding and register map validation; safety blocklists always take precedence.
    /// </summary>
    public static class ExternalProfileRegistry
    {
        public const string PROFILES_DIR_NAME = "profiles";

        private static readonly object _sync = new object();
        private static readonly List<DeviceProfile> _loadedProfiles = new List<DeviceProfile>();
        private static bool _initialized = false;

        public static List<DeviceProfile> GetLoadedProfiles()
        {
            EnsureLoaded();
            lock (_sync)
            {
                return new List<DeviceProfile>(_loadedProfiles);
            }
        }

        public static void EnsureLoaded()
        {
            if (_initialized) return;
            lock (_sync)
            {
                if (_initialized) return;
                _initialized = true;
                ReloadNoLock();
            }
        }

        public static void Reload()
        {
            lock (_sync)
            {
                _initialized = true;
                ReloadNoLock();
            }
        }

        internal static void ResetForTest(IEnumerable<DeviceProfile> profiles = null)
        {
            lock (_sync)
            {
                _loadedProfiles.Clear();
                if (profiles != null)
                {
                    foreach (DeviceProfile p in profiles)
                    {
                        if (p != null) _loadedProfiles.Add(p);
                    }
                }
                _initialized = (profiles != null);
            }
        }

        private static void ReloadNoLock()
        {
            _loadedProfiles.Clear();
            HashSet<string> seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<string> dirs = new List<string>(2);
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
            {
                dirs.Add(Path.Combine(baseDir, PROFILES_DIR_NAME));
            }
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localApp))
            {
                string userDir = Path.Combine(Path.Combine(localApp, "MiniFanControl"), PROFILES_DIR_NAME);
                if (dirs.Count == 0 || !dirs[0].Equals(userDir, StringComparison.OrdinalIgnoreCase))
                {
                    dirs.Add(userDir);
                }
            }

            foreach (string dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                List<string> errs;
                List<DeviceProfile> parsed = LoadFromDirectory(dir, out errs);
                foreach (DeviceProfile p in parsed)
                {
                    if (!seenIds.Contains(p.Id))
                    {
                        seenIds.Add(p.Id);
                        _loadedProfiles.Add(p);
                    }
                }
            }
        }

        public static bool IsTemplateFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return true;
            string n = Path.GetFileName(fileName);
            return n.StartsWith("template", StringComparison.OrdinalIgnoreCase) ||
                   n.EndsWith(".template.json", StringComparison.OrdinalIgnoreCase);
        }

        public static List<DeviceProfile> LoadFromDirectory(string directoryPath, out List<string> errors)
        {
            errors = new List<string>();
            List<DeviceProfile> results = new List<DeviceProfile>();
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
                return results;

            string[] files = Directory.GetFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);
                if (IsTemplateFile(fileName)) continue;

                try
                {
                    string json = File.ReadAllText(file);
                    DeviceProfile p = DeviceProfile.FromJson(json);
                    if (p == null)
                    {
                        errors.Add(fileName + ": failed to parse JSON hardware profile.");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(p.Id) ||
                        p.Id.Equals("unknown-safe", StringComparison.OrdinalIgnoreCase) ||
                        p.Id.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                        p.Id.Equals("custom", StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(fileName + ": invalid or reserved profile 'id' ('" + (p.Id ?? "") + "').");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(p.DisplayName))
                    {
                        errors.Add(fileName + ": missing 'displayName'.");
                        continue;
                    }
                    if (!p.HasValidRegisterMap())
                    {
                        errors.Add(fileName + ": invalid EC base address or missing required registers (regCpuTemp / regFan1Ctrl).");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(p.BoundProductName) && string.IsNullOrWhiteSpace(p.BoundBaseBoard))
                    {
                        errors.Add(fileName + ": external profile must specify 'boundProductName' or 'boundBaseBoard' for safe DMI matching.");
                        continue;
                    }
                    if (ids.Contains(p.Id))
                    {
                        errors.Add(fileName + ": duplicate profile id '" + p.Id + "'.");
                        continue;
                    }
                    ids.Add(p.Id);
                    results.Add(p);
                }
                catch (Exception ex)
                {
                    errors.Add(fileName + ": " + ex.Message);
                }
            }

            return results;
        }

        public static bool ValidateDirectory(string directoryPath, out List<string> errors, out int validCount)
        {
            List<DeviceProfile> loaded = LoadFromDirectory(directoryPath, out errors);
            validCount = loaded.Count;
            return errors.Count == 0;
        }

        public static DeviceProfile FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureLoaded();
            lock (_sync)
            {
                for (int i = 0; i < _loadedProfiles.Count; i++)
                {
                    if (_loadedProfiles[i].Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                        return _loadedProfiles[i];
                }
            }
            return null;
        }

        public static DeviceProfile MatchExternalProfile(string productName, string baseBoard, string manufacturer)
        {
            EnsureLoaded();
            lock (_sync)
            {
                for (int i = 0; i < _loadedProfiles.Count; i++)
                {
                    DeviceProfile p = _loadedProfiles[i];
                    if (p != null && p.HasValidRegisterMap() && p.MatchesDmi(productName, baseBoard, manufacturer))
                    {
                        return p;
                    }
                }
            }
            return null;
        }
    }
}
