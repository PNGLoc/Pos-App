using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PosSystem.Main.Helpers
{
    public sealed class DishImagePathToImageSourceConverter : IValueConverter
    {
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

            try
            {
                // Absolute URIs (http/https/file)
                if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri))
                    return LoadBitmap(absoluteUri);

                // Normalize relative path
                path = path.TrimStart('\\', '/');

                // If it's just a filename, first try the runtime upload folder: ./Images/<file>
                if (!path.Contains("/", StringComparison.Ordinal) && !path.Contains("\\", StringComparison.Ordinal))
                {
                    var runtimeUploadPath = Path.Combine(AppContext.BaseDirectory, "Images", path);
                    if (File.Exists(runtimeUploadPath))
                        return LoadBitmap(new Uri(runtimeUploadPath, UriKind.Absolute));
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

                return LoadBitmap(new Uri(fullPath, UriKind.Absolute));
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;

        private static ImageSource? LoadBitmap(Uri uri)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = uri;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
    }
}
