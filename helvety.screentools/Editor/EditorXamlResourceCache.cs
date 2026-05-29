using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace helvety.screentools.Editor
{
    /// <summary>
    /// Caches parsed colors and font families. Brushes and dash collections are created per element because WinUI cannot share those instances across multiple live targets.
    /// </summary>
    internal static class EditorXamlResourceCache
    {
        private static readonly Dictionary<string, Color> ColorByHex = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, FontFamily> FontByName = new(StringComparer.OrdinalIgnoreCase);
        private static readonly double[] SelectionDashPattern = { 4, 2 };
        private static readonly double[] CropDashPattern = { 4, 3 };

        internal static SolidColorBrush GetBrush(string colorHex)
        {
            return new SolidColorBrush(GetColor(colorHex));
        }

        internal static SolidColorBrush GetBrush(Color color)
        {
            return new SolidColorBrush(color);
        }

        internal static FontFamily GetFontFamily(string? fontFamily)
        {
            var name = string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily.Trim();
            if (FontByName.TryGetValue(name, out var cached))
            {
                return cached;
            }

            var family = new FontFamily(name);
            FontByName[name] = family;
            return family;
        }

        internal static DoubleCollection CreateSelectionStrokeDash()
        {
            return new DoubleCollection { SelectionDashPattern[0], SelectionDashPattern[1] };
        }

        internal static DoubleCollection CreateCropStrokeDash()
        {
            return new DoubleCollection { CropDashPattern[0], CropDashPattern[1] };
        }

        internal static SolidColorBrush ToolSelectedBackground => GetBrush(ColorHelper.FromArgb(70, 216, 27, 96));

        internal static SolidColorBrush SelectionFill => GetBrush(ColorHelper.FromArgb(35, 216, 27, 96));

        internal static SolidColorBrush CropFill => GetBrush(ColorHelper.FromArgb(48, 216, 27, 96));

        internal static SolidColorBrush CropStrokeMuted => GetBrush(ColorHelper.FromArgb(220, 216, 27, 96));

        internal static Color ParseColor(string colorHex)
        {
            return GetColor(colorHex);
        }

        private static Color GetColor(string colorHex)
        {
            var key = string.IsNullOrWhiteSpace(colorHex) ? "#FFFFFFFF" : colorHex.Trim();
            if (ColorByHex.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var color = ParseColorCore(key);
            ColorByHex[key] = color;
            return color;
        }

        private static Color ParseColorCore(string colorHex)
        {
            if (string.IsNullOrWhiteSpace(colorHex))
            {
                return Colors.White;
            }

            var value = colorHex.Trim().TrimStart('#');
            if (value.Length == 6)
            {
                var rgb = Convert.ToUInt32(value, 16);
                var r = (byte)((rgb & 0xFF0000) >> 16);
                var g = (byte)((rgb & 0x00FF00) >> 8);
                var b = (byte)(rgb & 0x0000FF);
                return ColorHelper.FromArgb(255, r, g, b);
            }

            if (value.Length == 8)
            {
                var argb = Convert.ToUInt32(value, 16);
                var a = (byte)((argb & 0xFF000000) >> 24);
                var r = (byte)((argb & 0x00FF0000) >> 16);
                var g = (byte)((argb & 0x0000FF00) >> 8);
                var b = (byte)(argb & 0x000000FF);
                return ColorHelper.FromArgb(a, r, g, b);
            }

            return Colors.White;
        }
    }
}
