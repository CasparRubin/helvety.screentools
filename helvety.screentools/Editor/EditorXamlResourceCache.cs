using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace helvety.screentools.Editor
{
    /// <summary>Caches immutable XAML brushes and collections to reduce allocation during editor redraws.</summary>
    internal static class EditorXamlResourceCache
    {
        private static readonly Dictionary<string, SolidColorBrush> BrushByHex = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, FontFamily> FontByName = new(StringComparer.OrdinalIgnoreCase);
        private static readonly DoubleCollection SelectionDash = new() { 4, 2 };
        private static readonly DoubleCollection CropDash = new() { 4, 3 };
        private static SolidColorBrush? _toolSelectedBrush;
        private static SolidColorBrush? _selectionFillBrush;
        private static SolidColorBrush? _cropFillBrush;
        private static SolidColorBrush? _cropStrokeMutedBrush;

        internal static SolidColorBrush GetBrush(string colorHex)
        {
            var key = string.IsNullOrWhiteSpace(colorHex) ? "#FFFFFFFF" : colorHex.Trim();
            if (BrushByHex.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var brush = new SolidColorBrush(ParseColor(key));
            BrushByHex[key] = brush;
            return brush;
        }

        internal static SolidColorBrush GetBrush(Color color)
        {
            var key = $"argb:{color.A}:{color.R}:{color.G}:{color.B}";
            if (BrushByHex.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var brush = new SolidColorBrush(color);
            BrushByHex[key] = brush;
            return brush;
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

        internal static DoubleCollection SelectionStrokeDash => SelectionDash;

        internal static DoubleCollection CropStrokeDash => CropDash;

        internal static SolidColorBrush ToolSelectedBackground
        {
            get
            {
                _toolSelectedBrush ??= GetBrush(ColorHelper.FromArgb(70, 216, 27, 96));
                return _toolSelectedBrush;
            }
        }

        internal static SolidColorBrush SelectionFill => _selectionFillBrush ??= GetBrush(ColorHelper.FromArgb(35, 216, 27, 96));

        internal static SolidColorBrush CropFill => _cropFillBrush ??= GetBrush(ColorHelper.FromArgb(48, 216, 27, 96));

        internal static SolidColorBrush CropStrokeMuted => _cropStrokeMutedBrush ??= GetBrush(ColorHelper.FromArgb(220, 216, 27, 96));

        internal static Color ParseColor(string colorHex)
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
