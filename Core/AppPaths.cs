using System;
using System.IO;

namespace MiniFanControl
{
    /// <summary>
    /// Shared path resolution helper ensuring Program Files installations redirect all writable
    /// state (config, logs, diagnostics) to %LocalAppData%\MiniFanControl while portable runs
    /// stay self-contained next to the executable.
    /// </summary>
    public static class AppPaths
    {
        public static bool IsInstalledInProgramFiles()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(baseDir)) return false;

            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            return (!string.IsNullOrEmpty(progFiles) && baseDir.StartsWith(progFiles, StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrEmpty(progFilesX86) && baseDir.StartsWith(progFilesX86, StringComparison.OrdinalIgnoreCase));
        }

        public static string GetAppDataDir(bool ensureCreated = false)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData)) return AppDomain.CurrentDomain.BaseDirectory;
            string dir = Path.Combine(localAppData, "MiniFanControl");
            if (ensureCreated)
            {
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
            }
            return dir;
        }

        public static string GetWritablePath(string fileName)
        {
            if (IsInstalledInProgramFiles())
            {
                return Path.Combine(GetAppDataDir(true), fileName);
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        }
    }
}
