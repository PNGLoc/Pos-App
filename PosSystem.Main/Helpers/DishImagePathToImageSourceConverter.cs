using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PosSystem.Main.Helpers;

namespace PosSystem.Main.Helpers
{
    public sealed class DishImagePathToImageSourceConverter : IValueConverter
    {
        private static readonly ConcurrentDictionary<string, WeakReference<ImageSource>> _cache = new(StringComparer.OrdinalIgnoreCase);

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string raw)
                return null;

            var path = raw.Trim();
            if (string.IsNullOrWhiteSpace(path))
                return null;

            // Treat default placeholder as "no image" for UI background
            if (string.Equals(path, "default.png", StringComparison.OrdinalIgnoreCase))
                return null;

            var decodePixelWidth = TryParseDecodePixelWidth(parameter);

            try
            {
                // Absolute URIs (http/https/file)
                if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri))
                    return LoadBitmapCached(absoluteUri, decodePixelWidth);

                // Normalize relative path
                path = path.TrimStart('\\', '/');

                // If it's just a filename, try DataRoot/images first
                if (!path.Contains("/", StringComparison.Ordinal) && !path.Contains("\\", StringComparison.Ordinal))
                {
                    try
                    {
                        AppPaths.EnsureInitialized();
                        var dataImagesPath = Path.Combine(AppPaths.ImagesDir, path);
                        if (File.Exists(dataImagesPath))
                            return LoadBitmapCached(new Uri(dataImagesPath, UriKind.Absolute), decodePixelWidth);

                        var dataImagesLegacyPluralPath = Path.Combine(AppPaths.ImagesDirLegacyPlural, path);
                        if (File.Exists(dataImagesLegacyPluralPath))
                            return LoadBitmapCached(new Uri(dataImagesLegacyPluralPath, UriKind.Absolute), decodePixelWidth);
                    }
                    catch { }
                }

                // If it's just a filename, first try the runtime upload folder: ./Images/<file>
                if (!path.Contains("/", StringComparison.Ordinal) && !path.Contains("\\", StringComparison.Ordinal))
                {
                    var runtimeUploadPath = Path.Combine(AppContext.BaseDirectory, "Images", path);
                    if (File.Exists(runtimeUploadPath))
                        return LoadBitmapCached(new Uri(runtimeUploadPath, UriKind.Absolute), decodePixelWidth);
                }

                // Common form stored by web-style paths: images/xxx.png
                if (path.StartsWith("images/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("images\\", StringComparison.OrdinalIgnoreCase))
                    path = Path.Combine("wwwroot", path);

                // If it's just a filename, assume it's under wwwroot/images
                if (!path.Contains("/", StringComparison.Ordinal) && !path.Contains("\\", StringComparison.Ordinal))
                    path = Path.Combine("wwwroot", "images", path);

                // Allow "wwwroot/images/..." or any relative file under output
                var fullPath = Path.Combine(AppContext.BaseDirectory, path);

                if (!File.Exists(fullPath))
                    return null;

                return LoadBitmapCached(new Uri(fullPath, UriKind.Absolute), decodePixelWidth);
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;

        private static int? TryParseDecodePixelWidth(object parameter)
        {
            if (parameter is null)
                return null;

            if (parameter is int i && i > 0)
                return i;

            if (parameter is double d && d > 0)
                return (int)Math.Round(d);

            if (parameter is string s && int.TryParse(s, out var parsed) && parsed > 0)
                return parsed;

            return null;
        }

        private static ImageSource? LoadBitmapCached(Uri uri, int? decodePixelWidth)
        {
            var key = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.ToString();

            if (_cache.TryGetValue(key, out var weak) && weak.TryGetTarget(out var existing))
                return existing;

            var loaded = LoadBitmap(uri, decodePixelWidth);
            if (loaded != null)
                _cache[key] = new WeakReference<ImageSource>(loaded);

            return loaded;
        }

        private static ImageSource? LoadBitmap(Uri uri, int? decodePixelWidth)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.None;
            if (decodePixelWidth is int w && w > 0)
                bitmap.DecodePixelWidth = w;
            bitmap.UriSource = uri;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
    }
}
