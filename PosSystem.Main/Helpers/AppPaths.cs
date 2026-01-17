using System;
using System.IO;

namespace PosSystem.Main.Helpers
{
    public static class AppPaths
    {
        private const string AppFolderName = "PosSystem";
        private const string DataFolderName = "data";
        // Canonical folder name (matches the desired deploy layout: /data/image/...)
        private const string ImagesFolderName = "image";
        // Backward-compatible folder name used by earlier builds
        private const string ImagesFolderNameLegacyPlural = "images";
        private const string DataRootConfigFileName = "data-root.txt";

        public static string ProgramDataAppFolder
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppFolderName);

        // Config stored in ProgramData so installer can pre-configure it.
        public static string DataRootConfigPath
            => Path.Combine(ProgramDataAppFolder, DataRootConfigFileName);

        public static string DefaultDataRoot
            => Path.Combine(ProgramDataAppFolder, DataFolderName);

        public static string DataRoot
        {
            get
            {
                try
                {
                    if (File.Exists(DataRootConfigPath))
                    {
                        var configured = File.ReadAllText(DataRootConfigPath).Trim().Trim('"');
                        if (!string.IsNullOrWhiteSpace(configured))
                            return configured;
                    }
                }
                catch
                {
                    // ignore and fall back
                }

                return DefaultDataRoot;
            }
        }

        public static string DbPath => Path.Combine(DataRoot, "pos_data.db");

        public static string ImagesDir => Path.Combine(DataRoot, ImagesFolderName);

        public static string ImagesDirLegacyPlural => Path.Combine(DataRoot, ImagesFolderNameLegacyPlural);

        // Legacy locations (older versions)
        public static string LegacyDbPath => Path.Combine(AppContext.BaseDirectory, "pos_data.db");
        public static string LegacyImagesDir => Path.Combine(AppContext.BaseDirectory, "Images");

        public static void EnsureInitialized()
        {
            // Ensure ProgramData app folder exists (for config)
            try
            {
                Directory.CreateDirectory(ProgramDataAppFolder);
            }
            catch
            {
                // If ProgramData is not writable, we still try to run with configured/default
            }

            // Ensure data folders exist
            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(ImagesDir);
            Directory.CreateDirectory(ImagesDirLegacyPlural);

            // One-time migrate plural folder -> canonical folder (if needed)
            try
            {
                if (Directory.Exists(ImagesDirLegacyPlural) && Directory.Exists(ImagesDir))
                {
                    foreach (var src in Directory.EnumerateFiles(ImagesDirLegacyPlural))
                    {
                        var fileName = Path.GetFileName(src);
                        var dest = Path.Combine(ImagesDir, fileName);
                        if (!File.Exists(dest))
                        {
                            try { File.Copy(src, dest, overwrite: false); } catch { }
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            // One-time auto-migrate legacy DB
            try
            {
                if (!File.Exists(DbPath) && File.Exists(LegacyDbPath))
                {
                    File.Copy(LegacyDbPath, DbPath, overwrite: false);
                }
            }
            catch
            {
                // ignore
            }

            // One-time auto-migrate legacy images
            try
            {
                if (Directory.Exists(LegacyImagesDir))
                {
                    foreach (var src in Directory.EnumerateFiles(LegacyImagesDir))
                    {
                        var fileName = Path.GetFileName(src);
                        var dest = Path.Combine(ImagesDir, fileName);
                        if (!File.Exists(dest))
                        {
                            try { File.Copy(src, dest, overwrite: false); } catch { }
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
