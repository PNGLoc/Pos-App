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

        // [NEW] Audio Directory: <appRoot>/data/notifiaudio
        public static string AudioDir => Path.Combine(DataRoot, "notifiaudio");

        public static void EnsureInitialized()
        {
            // Ensure data folders exist
            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(ImagesDir);
            Directory.CreateDirectory(ImagesDirLegacyPlural);
            Directory.CreateDirectory(AudioDir); // [NEW]
        }
    }
}
