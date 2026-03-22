// Glyph codes and Windows logo path align with Microsoft PowerToys KeyVisual / KeyCharPresenter (MIT).
// See: https://github.com/microsoft/PowerToys/tree/main/src/common/Common.UI.Controls/Controls/KeyVisual
//
// Persisted Display strings use '+' between segments (e.g. Shift+S+S+S). The Settings UI renders the same
// chord as spaced key pills without '+' characters between keys (PowerToys-style).

using System;
using System.Collections.Generic;
using System.Globalization;

namespace helvety.screentools
{
    /// <summary>
    /// Maps modifier flags and virtual-key codes to persisted display strings (plus-separated) and to per-key segments for chord UI.
    /// </summary>
    internal static class HotkeyVisualMapper
    {
        public const uint ModAlt = 0x0001;
        public const uint ModControl = 0x0002;
        public const uint ModShift = 0x0004;
        public const uint ModWin = 0x0008;

        private const int VkShift = 0x10;
        private const int VkLwin = 0x5B;
        private const int VkRwin = 0x5C;
        private const int VkPrintScreen = 0x2C;

        /// <summary>Path data from PowerToys WindowsKeyCharPresenterStyle.</summary>
        public const string WindowsLogoPathData = "M9 20H0V11H9V20ZM20 20H11V11H20V20ZM9 9H0V0H9V9ZM20 9H11V0H20V9Z";

        internal enum HotkeyPillKind
        {
            Text,
            Glyph,
            WindowsLogo,
        }

        internal readonly struct HotkeyPillSegment
        {
            public HotkeyPillSegment(HotkeyPillKind kind, string? text, string? glyph)
            {
                Kind = kind;
                Text = text;
                Glyph = glyph;
            }

            public HotkeyPillKind Kind { get; }
            public string? Text { get; }
            public string? Glyph { get; }
        }

        public static string BuildModifierPreview(uint modifiers)
        {
            var parts = new List<string>();

            if ((modifiers & ModControl) != 0)
            {
                parts.Add("Ctrl");
            }

            if ((modifiers & ModAlt) != 0)
            {
                parts.Add("Alt");
            }

            if ((modifiers & ModShift) != 0)
            {
                parts.Add("Shift");
            }

            if ((modifiers & ModWin) != 0)
            {
                parts.Add("Win");
            }

            return string.Join('+', parts);
        }

        public static string BuildBindingDisplay(uint modifiers, IReadOnlyList<string> keyNames)
        {
            var modifiersPart = BuildModifierPreview(modifiers);
            var sequencePart = string.Join('+', keyNames);
            return string.IsNullOrEmpty(modifiersPart) ? sequencePart : $"{modifiersPart}+{sequencePart}";
        }

        public static string GetAutomationChordName(uint modifiers, IReadOnlyList<uint> sequence)
        {
            if (sequence.Count == 0)
            {
                return "(none)";
            }

            var names = new string[sequence.Count];
            for (var i = 0; i < sequence.Count; i++)
            {
                names[i] = GetKeyDisplayName(sequence[i]);
            }

            return BuildBindingDisplay(modifiers, names);
        }

        public static IReadOnlyList<HotkeyPillSegment> GetChordSegments(uint modifiers, IReadOnlyList<uint> sequence)
        {
            var list = new List<HotkeyPillSegment>();

            if ((modifiers & ModControl) != 0)
            {
                list.Add(new HotkeyPillSegment(HotkeyPillKind.Text, "Ctrl", null));
            }

            if ((modifiers & ModAlt) != 0)
            {
                list.Add(new HotkeyPillSegment(HotkeyPillKind.Text, "Alt", null));
            }

            if ((modifiers & ModShift) != 0)
            {
                list.Add(new HotkeyPillSegment(HotkeyPillKind.Text, "Shift", null));
            }

            if ((modifiers & ModWin) != 0)
            {
                list.Add(new HotkeyPillSegment(HotkeyPillKind.WindowsLogo, null, null));
            }

            foreach (var vk in sequence)
            {
                list.Add(SegmentForVirtualKey(vk));
            }

            return list;
        }

        private static HotkeyPillSegment SegmentForVirtualKey(uint virtualKey)
        {
            switch (virtualKey)
            {
                case VkLwin:
                case VkRwin:
                    return new HotkeyPillSegment(HotkeyPillKind.WindowsLogo, null, null);
                case 0x25:
                    return new HotkeyPillSegment(HotkeyPillKind.Glyph, null, "\uE0E2");
                case 0x26:
                    return new HotkeyPillSegment(HotkeyPillKind.Glyph, null, "\uE0E4");
                case 0x27:
                    return new HotkeyPillSegment(HotkeyPillKind.Glyph, null, "\uE0E3");
                case 0x28:
                    return new HotkeyPillSegment(HotkeyPillKind.Glyph, null, "\uE0E5");
                case 0x0D:
                    return new HotkeyPillSegment(HotkeyPillKind.Glyph, null, "\uE751");
                case 0x08:
                    return new HotkeyPillSegment(HotkeyPillKind.Glyph, null, "\uE750");
                case VkShift:
                case 160:
                case 161:
                    return new HotkeyPillSegment(HotkeyPillKind.Glyph, null, "\uE752");
                default:
                    return new HotkeyPillSegment(HotkeyPillKind.Text, GetKeyDisplayName(virtualKey), null);
            }
        }

        public static string GetKeyDisplayName(uint virtualKey)
        {
            if (virtualKey >= 0x41 && virtualKey <= 0x5A)
            {
                return ((char)virtualKey).ToString();
            }

            if (virtualKey >= 0x30 && virtualKey <= 0x39)
            {
                return ((char)virtualKey).ToString();
            }

            if (virtualKey >= 0x70 && virtualKey <= 0x87)
            {
                return string.Format(CultureInfo.InvariantCulture, "F{0}", virtualKey - 0x6F);
            }

            return virtualKey switch
            {
                0x20 => "Space",
                0x25 => "LeftArrow",
                0x26 => "UpArrow",
                0x27 => "RightArrow",
                0x28 => "DownArrow",
                VkPrintScreen => "PrintScreen",
                0x2D => "Insert",
                0x2E => "Delete",
                0x24 => "Home",
                0x23 => "End",
                0x21 => "PageUp",
                0x22 => "PageDown",
                0x08 => "Backspace",
                0x09 => "Tab",
                0x0D => "Enter",
                _ => string.Format(CultureInfo.InvariantCulture, "VK_{0:X2}", virtualKey),
            };
        }
    }
}
