using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;

namespace helvety.screentools.Views.Controls
{
    /// <summary>
    /// Visual weight for key pills in <see cref="HotkeyChordStrip"/> (accent vs subtle theme brushes).
    /// </summary>
    public enum HotkeyChordAppearance
    {
        Default,
        Accent,
        Disabled,
    }

    /// <summary>
    /// WinUI control that displays modifier + key sequence using <see cref="HotkeyVisualMapper"/> segments (glyphs, Windows logo path, or text).
    /// Sets the automation name on the control for screen readers (full shortcut phrase when provided).
    /// </summary>
    public sealed partial class HotkeyChordStrip : UserControl
    {
        private static readonly FontFamily Mdl2Font = new("Segoe MDL2 Assets");

        public HotkeyChordStrip()
        {
            InitializeComponent();
        }

        public void SetChord(uint modifiers, IReadOnlyList<uint> sequence, HotkeyChordAppearance appearance, string? automationName)
        {
            RootPanel.Children.Clear();
            if (sequence.Count == 0)
            {
                SetEmpty("(none)", automationName ?? "(none)");
                return;
            }

            var segments = HotkeyVisualMapper.GetChordSegments(modifiers, sequence);
            foreach (var segment in segments)
            {
                RootPanel.Children.Add(CreatePill(segment, appearance));
            }

            AutomationProperties.SetName(
                this,
                !string.IsNullOrEmpty(automationName)
                    ? automationName!
                    : HotkeyVisualMapper.GetAutomationChordName(modifiers, sequence));
        }

        public void SetEmpty(string placeholderText, string automationName)
        {
            RootPanel.Children.Clear();
            var border = CreatePillBorder(HotkeyChordAppearance.Default);
            border.Child = new TextBlock
            {
                Text = placeholderText,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            };
            AutomationProperties.SetAccessibilityView(border, AccessibilityView.Raw);
            RootPanel.Children.Add(border);
            AutomationProperties.SetName(this, automationName);
        }

        public void SetSingleKey(uint? virtualKey, HotkeyChordAppearance appearance, string? automationName)
        {
            if (!virtualKey.HasValue)
            {
                SetEmpty("(not set)", automationName ?? "(not set)");
                return;
            }

            var vk = virtualKey.Value;
            SetChord(0, new List<uint> { vk }, appearance, automationName ?? HotkeyVisualMapper.GetKeyDisplayName(vk));
        }

        private FrameworkElement CreatePill(HotkeyVisualMapper.HotkeyPillSegment segment, HotkeyChordAppearance appearance)
        {
            var border = CreatePillBorder(appearance);

            switch (segment.Kind)
            {
                case HotkeyVisualMapper.HotkeyPillKind.Text:
                    border.Child = new TextBlock
                    {
                        Text = segment.Text,
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        Foreground = GetForegroundBrush(appearance),
                    };
                    break;
                case HotkeyVisualMapper.HotkeyPillKind.Glyph:
                    var icon = new FontIcon
                    {
                        Glyph = segment.Glyph!,
                        FontFamily = Mdl2Font,
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = GetForegroundBrush(appearance),
                    };
                    border.Child = new Viewbox
                    {
                        MaxHeight = 18,
                        MaxWidth = 18,
                        Child = icon,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    };
                    break;
                case HotkeyVisualMapper.HotkeyPillKind.WindowsLogo:
                    var pathIcon = CreateWindowsPathIcon(GetForegroundBrush(appearance));
                    border.Child = new Viewbox
                    {
                        MaxHeight = 14,
                        MaxWidth = 14,
                        Child = pathIcon,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    };
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            AutomationProperties.SetAccessibilityView(border, AccessibilityView.Raw);
            return border;
        }

        private static PathIcon CreateWindowsPathIcon(Brush foreground)
        {
            var xaml =
                "<PathIcon xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='"
                + HotkeyVisualMapper.WindowsLogoPathData
                + "' />";
            var icon = (PathIcon)XamlReader.Load(xaml);
            icon.Foreground = foreground;
            return icon;
        }

        private static Brush GetForegroundBrush(HotkeyChordAppearance appearance)
        {
            if (appearance == HotkeyChordAppearance.Accent)
            {
                return (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];
            }

            if (appearance == HotkeyChordAppearance.Disabled)
            {
                return (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            }

            return (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        }

        private static Border CreatePillBorder(HotkeyChordAppearance appearance)
        {
            if (appearance == HotkeyChordAppearance.Accent)
            {
                return new Border
                {
                    MinHeight = 32,
                    Padding = new Thickness(10, 8, 10, 8),
                    CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
                    Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["AccentControlElevationBorderBrush"],
                    BorderThickness = new Thickness(1),
                    BackgroundSizing = BackgroundSizing.OuterBorderEdge,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }

            if (appearance == HotkeyChordAppearance.Disabled)
            {
                return new Border
                {
                    MinHeight = 28,
                    Padding = new Thickness(10, 6, 10, 6),
                    CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
                    Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    BorderThickness = (Thickness)Application.Current.Resources["ButtonBorderThemeThickness"],
                    BackgroundSizing = BackgroundSizing.InnerBorderEdge,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }

            return new Border
            {
                MinHeight = 28,
                Padding = new Thickness(10, 6, 10, 6),
                CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = (Thickness)Application.Current.Resources["ButtonBorderThemeThickness"],
                BackgroundSizing = BackgroundSizing.InnerBorderEdge,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
    }
}
