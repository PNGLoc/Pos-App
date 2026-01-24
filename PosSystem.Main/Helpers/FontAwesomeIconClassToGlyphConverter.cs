using System;
using System.Globalization;
using System.Windows.Data;

namespace PosSystem.Main.Helpers
{
    public class FontAwesomeIconClassToGlyphConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var cls = (value as string ?? string.Empty).Trim().ToLowerInvariant();
            int codepoint = cls switch
            {
                "fas fa-chair" or "fa-solid fa-chair" => 0xf6c0,
                "fas fa-shopping-bag" or "fa-solid fa-bag-shopping" or "fa-solid fa-shopping-bag" => 0xf290,
                "fas fa-walking" or "fa-solid fa-person-walking" or "fa-solid fa-walking" => 0xf554,
                "fas fa-motorcycle" or "fa-solid fa-motorcycle" => 0xf21c,
                "fas fa-crown" or "fa-solid fa-crown" => 0xf521,
                "fas fa-users" or "fa-solid fa-users" => 0xf0c0,
                "fas fa-clock" or "fa-solid fa-clock" => 0xf017,
                _ => 0xf6c0
            };

            return char.ConvertFromUtf32(codepoint);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
