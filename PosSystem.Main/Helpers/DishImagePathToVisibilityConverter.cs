using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using PosSystem.Main.Helpers;

namespace PosSystem.Main.Helpers
{
    public sealed class DishImagePathToVisibilityConverter : IValueConverter
    {
        private static readonly ConcurrentDictionary<string, bool> _existsCache = new(StringComparer.OrdinalIgnoreCase);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string raw)
                return Visibility.Collapsed;

            var path = raw.Trim();
            if (string.IsNullOrWhiteSpace(path))
                return Visibility.Collapsed;

            if (string.Equals(path, "default.png", StringComparison.OrdinalIgnoreCase))
                return Visibility.Collapsed;

            try
            {
                // absolute uri (http/https/file) => show
                if (Uri.TryCreate(path, UriKind.Absolute, out _))
                    return Visibility.Visible;

                path = path.TrimStart('\\', '/');

                // If it's just a filename, try DataRoot/images first
                if (!path.Contains("/", StringComparison.Ordinal) && !path.Contains("\\", StringComparison.Ordinal))
                {
                    try
                    {
                        AppPaths.EnsureInitialized();
                        var dataImagesPath = Path.Combine(AppPaths.ImagesDir, path);
                        if (ExistsCached(dataImagesPath))
                            return Visibility.Visible;

                        var dataImagesLegacyPluralPath = Path.Combine(AppPaths.ImagesDirLegacyPlural, path);
                        if (ExistsCached(dataImagesLegacyPluralPath))
                            return Visibility.Visible;
                    }
                    catch { }
                }

                // If it's just a filename, first try runtime upload folder: ./Images/<file>
                if (!path.Contains("/", StringComparison.Ordinal) && !path.Contains("\\", StringComparison.Ordinal))
                {
                    var runtimeUploadPath = Path.Combine(AppContext.BaseDirectory, "Images", path);
                    if (ExistsCached(runtimeUploadPath))
                        return Visibility.Visible;
                }

                if (path.StartsWith("images/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("images\\", StringComparison.OrdinalIgnoreCase))
                    path = Path.Combine("wwwroot", path);

                if (!path.Contains("/", StringComparison.Ordinal) && !path.Contains("\\", StringComparison.Ordinal))
                    path = Path.Combine("wwwroot", "images", path);

                var fullPath = Path.Combine(AppContext.BaseDirectory, path);
                return ExistsCached(fullPath) ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                return Visibility.Collapsed;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;

        private static bool ExistsCached(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return false;

            return _existsCache.GetOrAdd(fullPath, static p => File.Exists(p));
        }
    }
}
