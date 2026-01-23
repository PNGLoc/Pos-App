using System;
using System.IO;

namespace PosSystem.Main.Helpers
{
    public static class AppPaths
    {
        private const string DataFolderName = "data";
        // Canonical folder name (matches the desired deploy layout: /data/image/...)
        private const string ImagesFolderName = "image";
        // Backward-compatible folder name used by earlier builds
        private const string ImagesFolderNameLegacyPlural = "images";

        // AppRoot is the installation folder (where the exe is located)
        public static string AppRoot => AppContext.BaseDirectory;

        // REQUIRED DEPLOY LAYOUT:
        // <appRoot>/data/pos_data.db
        // <appRoot>/data/image/<files>
        public static string DataRoot => Path.Combine(AppRoot, DataFolderName);

        public static string DbPath => Path.Combine(DataRoot, "pos_data.db");

        public static string ImagesDir => Path.Combine(DataRoot, ImagesFolderName);

        public static string ImagesDirLegacyPlural => Path.Combine(DataRoot, ImagesFolderNameLegacyPlural);

        // Legacy locations (older versions)
        public static string LegacyDbPath => Path.Combine(AppRoot, "pos_data.db");
        public static string LegacyImagesDir => Path.Combine(AppRoot, "Images");

        // Previous location used by some builds (ProgramData)
        private static string LegacyProgramDataRoot
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PosSystem", DataFolderName);

        private static string LegacyProgramDataDbPath => Path.Combine(LegacyProgramDataRoot, "pos_data.db");

        private static string LegacyProgramDataImagesDir => Path.Combine(LegacyProgramDataRoot, ImagesFolderName);

        public static void EnsureInitialized()
        {
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

            // One-time auto-migrate ProgramData DB (older installs)
            try
            {
                if (!File.Exists(DbPath) && File.Exists(LegacyProgramDataDbPath))
                {
                    File.Copy(LegacyProgramDataDbPath, DbPath, overwrite: false);
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

            // One-time auto-migrate ProgramData images (older installs)
            try
            {
                if (Directory.Exists(LegacyProgramDataImagesDir))
                {
                    foreach (var src in Directory.EnumerateFiles(LegacyProgramDataImagesDir))
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
