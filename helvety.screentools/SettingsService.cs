using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation.Collections;
using Windows.Storage;

namespace helvety.screentools
{
    internal static class SettingsService
    {
        internal const int MaxHotkeySequenceLength = 5;
        private const uint DefaultHotkeyModifiers = 0x0004; // Shift
        /// <summary>Default persisted capture shortcut label (plus-separated); Settings shows the same chord as visual key pills.</summary>
        private const string DefaultHotkeyDisplay = "Shift+S+S+S";
        private const string DefaultHotkeySequence = "83,83,83";
        private const uint DefaultLiveDrawHotkeyModifiers = 0x0004; // Shift
        /// <summary>Default persisted Live Draw shortcut label (plus-separated); Settings shows the same chord as visual key pills.</summary>
        private const string DefaultLiveDrawHotkeyDisplay = "Shift+D+D+D";
        private const string DefaultLiveDrawHotkeySequence = "68,68,68";
        private const ScreenshotBorderIntensity DefaultScreenshotBorderIntensity = ScreenshotBorderIntensity.Bold;
        private const ScreenshotQualityMode DefaultScreenshotQualityMode = ScreenshotQualityMode.Fast;
        private const bool DefaultShowScreenshotOverlayInstructions = true;
        private const bool DefaultMinimizeToTrayOnClose = true;
        private const string DefaultEditorPrimaryColor = "#FFD81B60";
        private const int DefaultEditorPrimaryThickness = 4;
        private const int DefaultLiveDrawMainStrokeThickness = DefaultEditorPrimaryThickness;
        private const bool DefaultLiveDrawFreeDrawEnabled = true;
        private const bool DefaultLiveDrawSparkleEnabled = true;
        private const string DefaultEditorTextFont = "Segoe UI";
        private const int DefaultEditorTextSize = 38;
        private const bool DefaultEditorTextBoldEnabled = true;
        private const bool DefaultEditorTextItalicEnabled = false;
        private const bool DefaultEditorTextBorderEnabled = false;
        private const string DefaultEditorTextBorderColor = "#FFFFFFFF";
        private const int DefaultEditorTextBorderThickness = 1;
        private const bool DefaultEditorTextShadowEnabled = true;
        private const bool DefaultEditorBorderShadowEnabled = true;
        private const bool DefaultEditorArrowBorderEnabled = false;
        private const string DefaultEditorArrowBorderColor = "#FF000000";
        private const int DefaultEditorArrowBorderThickness = 1;
        private const bool DefaultEditorArrowShadowEnabled = true;
        private const string DefaultEditorArrowFormStyle = "Tapered";
        private const int DefaultEditorBlurRadius = 6;
        private const int DefaultEditorBlurFeather = 0;
        private const bool DefaultEditorBlurInvertMode = false;
        private const int DefaultEditorHighlightDimPercent = 35;
        private const bool DefaultEditorHighlightInvertMode = false;
        private const int DefaultEditorRegionCornerRadius = 8;
        private const bool DefaultEditorPerformanceModeEnabled = false;
        private const bool DefaultEditorGpuEffectsEnabled = true;
        private const bool DefaultEditorEmbedEditableMetadata = true;
        private const bool DefaultCaptureHotkeyEnabled = true;
        private const bool DefaultLiveDrawFeatureEnabled = true;

        internal static event Action? SaveFolderPathChanged;
        internal static event Action? SettingsChanged;

        private const int CurrentSettingsVersion = 5;
        private const string DefaultCaptureFolderName = "Helvety Screen Tools captures";
        private const string SettingsVersionKey = "SettingsVersion";
        private const string SaveFolderPathKey = "SaveFolderPath";
        private const string HotkeyModifiersKey = "HotkeyModifiers";
        private const string HotkeyVirtualKeyKey = "HotkeyVirtualKey";
        private const string HotkeySequenceKey = "HotkeySequence";
        private const string HotkeyDisplayKey = "HotkeyDisplay";
        private const string HotkeyClearedKey = "HotkeyCleared";
        private const string LiveDrawHotkeyModifiersKey = "LiveDrawHotkeyModifiers";
        private const string LiveDrawHotkeyVirtualKeyKey = "LiveDrawHotkeyVirtualKey";
        private const string LiveDrawHotkeySequenceKey = "LiveDrawHotkeySequence";
        private const string LiveDrawHotkeyDisplayKey = "LiveDrawHotkeyDisplay";
        private const string LiveDrawHotkeyClearedKey = "LiveDrawHotkeyCleared";
        private const string LiveDrawRectangleModifierKey = "LiveDrawRectangleModifier";
        private const string LiveDrawShapeModRectangleKey = "LiveDrawShapeModRectangle";
        private const string LiveDrawShapeModArrowKey = "LiveDrawShapeModArrow";
        private const string LiveDrawShapeModStraightLineKey = "LiveDrawShapeModStraightLine";
        private const string LiveDrawShapeModCircleRightKey = "LiveDrawShapeModCircleRight";
        private const string LiveDrawShapeModEllipseRightKey = "LiveDrawShapeModEllipseRight";
        private const string LiveDrawMainStrokeThicknessKey = "LiveDrawMainStrokeThickness";
        private const string LiveDrawFreeDrawEnabledKey = "LiveDrawFreeDrawEnabled";
        private const string LiveDrawSparkleEnabledKey = "LiveDrawSparkleEnabled";
        private const string CaptureHotkeyEnabledKey = "CaptureHotkeyEnabled";
        private const string LiveDrawEnabledKey = "LiveDrawEnabled";
        private const string SaveFolderClearedKey = "SaveFolderCleared";
        private const string ScreenshotBorderIntensityKey = "ScreenshotBorderIntensity";
        private const string ScreenshotQualityModeKey = "ScreenshotQualityMode";
        private const string ShowScreenshotOverlayInstructionsKey = "ShowScreenshotOverlayInstructions";
        private const string MinimizeToTrayOnCloseKey = "MinimizeToTrayOnClose";
        private const string EditorPrimaryColorKey = "EditorPrimaryColor";
        private const string EditorPrimaryThicknessKey = "EditorPrimaryThickness";
        private const string EditorTextFontKey = "EditorTextFont";
        private const string EditorTextSizeKey = "EditorTextSize";
        private const string EditorTextBoldEnabledKey = "EditorTextBoldEnabled";
        private const string EditorTextItalicEnabledKey = "EditorTextItalicEnabled";
        private const string EditorTextBorderEnabledKey = "EditorTextBorderEnabled";
        private const string EditorTextBorderColorKey = "EditorTextBorderColor";
        private const string EditorTextBorderThicknessKey = "EditorTextBorderThickness";
        private const string EditorTextShadowEnabledKey = "EditorTextShadowEnabled";
        private const string EditorTextShadowColorKey = "EditorTextShadowColor";
        private const string EditorTextShadowOffsetKey = "EditorTextShadowOffset";
        private const string EditorBorderShadowEnabledKey = "EditorBorderShadowEnabled";
        private const string EditorBorderShadowColorKey = "EditorBorderShadowColor";
        private const string EditorBorderShadowOffsetKey = "EditorBorderShadowOffset";
        private const string EditorArrowBorderEnabledKey = "EditorArrowBorderEnabled";
        private const string EditorArrowBorderColorKey = "EditorArrowBorderColor";
        private const string EditorArrowBorderThicknessKey = "EditorArrowBorderThickness";
        private const string EditorArrowShadowEnabledKey = "EditorArrowShadowEnabled";
        private const string EditorArrowShadowColorKey = "EditorArrowShadowColor";
        private const string EditorArrowShadowOffsetKey = "EditorArrowShadowOffset";
        private const string EditorArrowFormStyleKey = "EditorArrowFormStyle";
        private const string EditorBlurRadiusKey = "EditorBlurRadius";
        private const string EditorBlurFeatherKey = "EditorBlurFeather";
        private const string EditorBlurInvertModeKey = "EditorBlurInvertMode";
        private const string EditorHighlightDimPercentKey = "EditorHighlightDimPercent";
        private const string EditorHighlightInvertModeKey = "EditorHighlightInvertMode";
        private const string EditorRegionCornerRadiusKey = "EditorRegionCornerRadius";
        private const string EditorPerformanceModeEnabledKey = "EditorPerformanceModeEnabled";
        private const string EditorGpuEffectsEnabledKey = "EditorGpuEffectsEnabled";
        private const string EditorEmbedEditableMetadataKey = "EditorEmbedEditableMetadata";
        private static readonly string[] ManagedSettingKeys =
        {
            SettingsVersionKey,
            SaveFolderPathKey,
            SaveFolderClearedKey,
            HotkeyModifiersKey,
            HotkeyVirtualKeyKey,
            HotkeySequenceKey,
            HotkeyDisplayKey,
            HotkeyClearedKey,
            LiveDrawHotkeyModifiersKey,
            LiveDrawHotkeyVirtualKeyKey,
            LiveDrawHotkeySequenceKey,
            LiveDrawHotkeyDisplayKey,
            LiveDrawHotkeyClearedKey,
            LiveDrawRectangleModifierKey,
            LiveDrawShapeModRectangleKey,
            LiveDrawShapeModArrowKey,
            LiveDrawShapeModStraightLineKey,
            LiveDrawShapeModCircleRightKey,
            LiveDrawShapeModEllipseRightKey,
            CaptureHotkeyEnabledKey,
            LiveDrawEnabledKey,
            ScreenshotBorderIntensityKey,
            ScreenshotQualityModeKey,
            ShowScreenshotOverlayInstructionsKey,
            MinimizeToTrayOnCloseKey,
            LiveDrawMainStrokeThicknessKey,
            LiveDrawFreeDrawEnabledKey,
            LiveDrawSparkleEnabledKey,
            EditorPrimaryColorKey,
            EditorPrimaryThicknessKey,
            EditorTextFontKey,
            EditorTextSizeKey,
            EditorTextBoldEnabledKey,
            EditorTextItalicEnabledKey,
            EditorTextBorderEnabledKey,
            EditorTextBorderColorKey,
            EditorTextBorderThicknessKey,
            EditorTextShadowEnabledKey,
            EditorTextShadowColorKey,
            EditorTextShadowOffsetKey,
            EditorBorderShadowEnabledKey,
            EditorBorderShadowColorKey,
            EditorBorderShadowOffsetKey,
            EditorArrowBorderEnabledKey,
            EditorArrowBorderColorKey,
            EditorArrowBorderThicknessKey,
            EditorArrowShadowEnabledKey,
            EditorArrowShadowColorKey,
            EditorArrowShadowOffsetKey,
            EditorArrowFormStyleKey,
            EditorBlurRadiusKey,
            EditorBlurFeatherKey,
            EditorBlurInvertModeKey,
            EditorHighlightDimPercentKey,
            EditorHighlightInvertModeKey,
            EditorRegionCornerRadiusKey,
            EditorPerformanceModeEnabledKey,
            EditorGpuEffectsEnabledKey,
            EditorEmbedEditableMetadataKey
        };

        internal static AppSettings Load()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);

            var saveFolderPath = values.TryGetValue(SaveFolderPathKey, out var folderValue)
                ? folderValue as string
                : null;
            var isHotkeyCleared = values.TryGetValue(HotkeyClearedKey, out var hotkeyClearedValue) &&
                                  hotkeyClearedValue is bool hotkeyCleared &&
                                  hotkeyCleared;
            var isSaveFolderCleared = values.TryGetValue(SaveFolderClearedKey, out var saveFolderClearedValue) &&
                                      saveFolderClearedValue is bool saveFolderCleared &&
                                      saveFolderCleared;
            var screenshotBorderIntensity = values.TryGetValue(ScreenshotBorderIntensityKey, out var borderIntensityValue) &&
                                            borderIntensityValue is int borderIntensityInt &&
                                            Enum.IsDefined(typeof(ScreenshotBorderIntensity), borderIntensityInt)
                ? (ScreenshotBorderIntensity)borderIntensityInt
                : DefaultScreenshotBorderIntensity;
            var screenshotQualityMode = values.TryGetValue(ScreenshotQualityModeKey, out var qualityModeValue) &&
                                        qualityModeValue is int qualityModeInt &&
                                        Enum.IsDefined(typeof(ScreenshotQualityMode), qualityModeInt)
                ? (ScreenshotQualityMode)qualityModeInt
                : DefaultScreenshotQualityMode;
            var showScreenshotOverlayInstructions = values.TryGetValue(ShowScreenshotOverlayInstructionsKey, out var showOverlayValue) &&
                                                    showOverlayValue is bool showOverlayInstructions
                ? showOverlayInstructions
                : DefaultShowScreenshotOverlayInstructions;
            var minimizeToTrayOnClose = values.TryGetValue(MinimizeToTrayOnCloseKey, out var minimizeToTrayValue) &&
                                        minimizeToTrayValue is bool minimizeToTray
                ? minimizeToTray
                : DefaultMinimizeToTrayOnClose;
            var captureHotkeyEnabled = ReadBool(values, CaptureHotkeyEnabledKey, DefaultCaptureHotkeyEnabled);
            var liveDrawEnabled = ReadBool(values, LiveDrawEnabledKey, DefaultLiveDrawFeatureEnabled);

            HotkeySettings? hotkey = null;
            if (!isHotkeyCleared &&
                values.TryGetValue(HotkeyModifiersKey, out var modifiersValue) &&
                values.TryGetValue(HotkeyDisplayKey, out var displayValue) &&
                modifiersValue is int modifiersInt &&
                displayValue is string display &&
                !string.IsNullOrWhiteSpace(display) &&
                modifiersInt >= 0)
            {
                var parsedSequence = ReadSequence(values);
                if (parsedSequence.Count == 0 &&
                    values.TryGetValue(HotkeyVirtualKeyKey, out var virtualKeyValue) &&
                    virtualKeyValue is int virtualKeyInt &&
                    virtualKeyInt > 0)
                {
                    parsedSequence = new[] { (uint)virtualKeyInt };
                }

                if (parsedSequence.Count > 0)
                {
                    hotkey = new HotkeySettings((uint)modifiersInt, parsedSequence, display);
                }
            }

            return new AppSettings(
                saveFolderPath,
                hotkey,
                isHotkeyCleared,
                isSaveFolderCleared,
                screenshotBorderIntensity,
                screenshotQualityMode,
                showScreenshotOverlayInstructions,
                minimizeToTrayOnClose,
                captureHotkeyEnabled,
                liveDrawEnabled);
        }

        internal static void SaveHotkey(uint modifiers, IReadOnlyList<uint> sequence, string display)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);
            var normalizedSequence = sequence.Where(key => key > 0).Take(MaxHotkeySequenceLength).ToArray();

            values[HotkeyModifiersKey] = (int)modifiers;
            values[HotkeySequenceKey] = SerializeSequence(normalizedSequence);
            if (normalizedSequence.Length > 0)
            {
                values[HotkeyVirtualKeyKey] = (int)normalizedSequence[0];
            }
            values[HotkeyDisplayKey] = display;
            values[HotkeyClearedKey] = false;
            SettingsChanged?.Invoke();
        }

        internal static void SaveFolderPath(string folderPath)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);

            if (values.TryGetValue(SaveFolderPathKey, out var existingValue) &&
                existingValue is string existingPath &&
                string.Equals(existingPath, folderPath, StringComparison.Ordinal) &&
                !(values.TryGetValue(SaveFolderClearedKey, out var clearedValue) &&
                  clearedValue is bool isCleared &&
                  isCleared))
            {
                return;
            }

            values[SaveFolderPathKey] = folderPath;
            values[SaveFolderClearedKey] = false;
            SaveFolderPathChanged?.Invoke();
            SettingsChanged?.Invoke();
        }

        internal static void ClearHotkey()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);

            values.Remove(HotkeyModifiersKey);
            values.Remove(HotkeyVirtualKeyKey);
            values.Remove(HotkeySequenceKey);
            values.Remove(HotkeyDisplayKey);
            values[HotkeyClearedKey] = true;
            SettingsChanged?.Invoke();
        }

        internal static void ClearSaveFolderPath()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);

            values.Remove(SaveFolderPathKey);
            values[SaveFolderClearedKey] = true;
            SaveFolderPathChanged?.Invoke();
            SettingsChanged?.Invoke();
        }

        /// <summary>
        /// Persists global snap-border animation intensity (frozen-screen capture and Live Draw via <see cref="Capture.SnapBorderChromeController"/>).
        /// </summary>
        internal static void SaveScreenshotBorderIntensity(ScreenshotBorderIntensity intensity)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);
            values[ScreenshotBorderIntensityKey] = (int)intensity;
            SettingsChanged?.Invoke();
        }

        internal static void SaveScreenshotQualityMode(ScreenshotQualityMode qualityMode)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);
            values[ScreenshotQualityModeKey] = (int)qualityMode;
            SettingsChanged?.Invoke();
        }

        internal static void SaveShowScreenshotOverlayInstructions(bool showInstructions)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);
            values[ShowScreenshotOverlayInstructionsKey] = showInstructions;
            SettingsChanged?.Invoke();
        }

        internal static void SaveMinimizeToTrayOnClose(bool minimizeToTrayOnClose)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);
            values[MinimizeToTrayOnCloseKey] = minimizeToTrayOnClose;
            SettingsChanged?.Invoke();
        }

        internal static void SaveEditorPerformanceModeEnabled(bool enabled)
        {
            var settings = LoadEditorUiSettings();
            SaveEditorUiSettings(settings with { PerformanceModeEnabled = enabled });
        }

        internal static bool LoadEditorEmbedEditableMetadataEnabled()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);
            return ReadBool(values, EditorEmbedEditableMetadataKey, DefaultEditorEmbedEditableMetadata);
        }

        internal static void SaveEditorEmbedEditableMetadataEnabled(bool enabled)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);
            values[EditorEmbedEditableMetadataKey] = enabled;
            SettingsChanged?.Invoke();
        }

        internal static EditorUiSettings LoadEditorUiSettings()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);

            return new EditorUiSettings(
                ReadString(values, EditorPrimaryColorKey, DefaultEditorPrimaryColor),
                ReadInt(values, EditorPrimaryThicknessKey, DefaultEditorPrimaryThickness, 1, 24),
                ReadString(values, EditorTextFontKey, DefaultEditorTextFont),
                ReadInt(values, EditorTextSizeKey, DefaultEditorTextSize, 8, 180),
                ReadBool(values, EditorTextBoldEnabledKey, DefaultEditorTextBoldEnabled),
                ReadBool(values, EditorTextItalicEnabledKey, DefaultEditorTextItalicEnabled),
                ReadBool(values, EditorTextBorderEnabledKey, DefaultEditorTextBorderEnabled),
                ReadString(values, EditorTextBorderColorKey, DefaultEditorTextBorderColor),
                ReadInt(values, EditorTextBorderThicknessKey, DefaultEditorTextBorderThickness, 1, 24),
                ReadBool(values, EditorTextShadowEnabledKey, DefaultEditorTextShadowEnabled),
                ReadBool(values, EditorBorderShadowEnabledKey, DefaultEditorBorderShadowEnabled),
                ReadBool(values, EditorArrowBorderEnabledKey, DefaultEditorArrowBorderEnabled),
                ReadString(values, EditorArrowBorderColorKey, DefaultEditorArrowBorderColor),
                ReadInt(values, EditorArrowBorderThicknessKey, DefaultEditorArrowBorderThickness, 1, 24),
                ReadBool(values, EditorArrowShadowEnabledKey, DefaultEditorArrowShadowEnabled),
                ReadString(values, EditorArrowFormStyleKey, DefaultEditorArrowFormStyle),
                ReadInt(values, EditorBlurRadiusKey, DefaultEditorBlurRadius, 1, 25),
                ReadInt(values, EditorBlurFeatherKey, DefaultEditorBlurFeather, 0, 40),
                ReadBool(values, EditorBlurInvertModeKey, DefaultEditorBlurInvertMode),
                ReadInt(values, EditorHighlightDimPercentKey, DefaultEditorHighlightDimPercent, 0, 80),
                ReadBool(values, EditorHighlightInvertModeKey, DefaultEditorHighlightInvertMode),
                ReadInt(values, EditorRegionCornerRadiusKey, DefaultEditorRegionCornerRadius, 0, 24),
                ReadBool(values, EditorPerformanceModeEnabledKey, DefaultEditorPerformanceModeEnabled),
                ReadBool(values, EditorGpuEffectsEnabledKey, DefaultEditorGpuEffectsEnabled));
        }

        internal static void SaveCaptureHotkeyEnabled(bool enabled)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);
            values[CaptureHotkeyEnabledKey] = enabled;
            SettingsChanged?.Invoke();
        }

        internal static void SaveLiveDrawEnabled(bool enabled)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);
            values[LiveDrawEnabledKey] = enabled;
            SettingsChanged?.Invoke();
        }

        internal static LiveDrawDrawingSettings LoadLiveDrawDrawingSettings()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);
            return new LiveDrawDrawingSettings(
                ReadInt(values, LiveDrawMainStrokeThicknessKey, DefaultLiveDrawMainStrokeThickness, 1, 24),
                ReadBool(values, LiveDrawFreeDrawEnabledKey, DefaultLiveDrawFreeDrawEnabled),
                ReadBool(values, LiveDrawSparkleEnabledKey, DefaultLiveDrawSparkleEnabled));
        }

        internal static void SaveLiveDrawMainStrokeThickness(int thickness)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);
            values[LiveDrawMainStrokeThicknessKey] = Clamp(thickness, 1, 24);
            SettingsChanged?.Invoke();
        }

        internal static void SaveLiveDrawFreeDrawEnabled(bool enabled)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);
            values[LiveDrawFreeDrawEnabledKey] = enabled;
            SettingsChanged?.Invoke();
        }

        internal static void SaveLiveDrawSparkleEnabled(bool enabled)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);
            values[LiveDrawSparkleEnabledKey] = enabled;
            SettingsChanged?.Invoke();
        }

        internal static void SaveEditorUiSettings(EditorUiSettings settings)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);

            values[EditorPrimaryColorKey] = settings.PrimaryColorHex;
            values[EditorPrimaryThicknessKey] = Clamp(settings.PrimaryThickness, 1, 24);
            values[EditorTextFontKey] = settings.TextFont;
            values[EditorTextSizeKey] = Clamp(settings.TextSize, 8, 180);
            values[EditorTextBoldEnabledKey] = settings.TextBoldEnabled;
            values[EditorTextItalicEnabledKey] = settings.TextItalicEnabled;
            values[EditorTextBorderEnabledKey] = settings.TextBorderEnabled;
            values[EditorTextBorderColorKey] = settings.TextBorderColorHex;
            values[EditorTextBorderThicknessKey] = Clamp(settings.TextBorderThickness, 1, 24);
            values[EditorTextShadowEnabledKey] = settings.TextShadowEnabled;
            values[EditorBorderShadowEnabledKey] = settings.BorderShadowEnabled;
            values[EditorArrowBorderEnabledKey] = settings.ArrowBorderEnabled;
            values[EditorArrowBorderColorKey] = settings.ArrowBorderColorHex;
            values[EditorArrowBorderThicknessKey] = Clamp(settings.ArrowBorderThickness, 1, 24);
            values[EditorArrowShadowEnabledKey] = settings.ArrowShadowEnabled;
            values[EditorArrowFormStyleKey] = settings.ArrowFormStyle;
            values[EditorBlurRadiusKey] = Clamp(settings.BlurRadius, 1, 25);
            values[EditorBlurFeatherKey] = Clamp(settings.BlurFeather, 0, 40);
            values[EditorBlurInvertModeKey] = settings.BlurInvertMode;
            values[EditorHighlightDimPercentKey] = Clamp(settings.HighlightDimPercent, 0, 80);
            values[EditorHighlightInvertModeKey] = settings.HighlightInvertMode;
            values[EditorRegionCornerRadiusKey] = Clamp(settings.RegionCornerRadius, 0, 24);
            values[EditorPerformanceModeEnabledKey] = settings.PerformanceModeEnabled;
            values[EditorGpuEffectsEnabledKey] = settings.GpuEffectsEnabled;
            SettingsChanged?.Invoke();
        }

        /// <summary>
        /// Clears all managed settings keys and reapplies defaults (including snap-border intensity, capture, Live Draw, reset-related settings, and editor state).
        /// </summary>
        internal static void ResetAllSettingsToDefaults()
        {
            var values = ApplicationData.Current.LocalSettings.Values;

            foreach (var key in ManagedSettingKeys)
            {
                values.Remove(key);
            }

            ApplyDefaultSettings(values);
            SaveFolderPathChanged?.Invoke();
            SettingsChanged?.Invoke();
        }

        internal static HotkeySettings GetDefaultHotkey()
        {
            return new HotkeySettings(DefaultHotkeyModifiers, ParseSequence(DefaultHotkeySequence), DefaultHotkeyDisplay);
        }

        internal static HotkeySettings GetDefaultLiveDrawHotkey()
        {
            return new HotkeySettings(DefaultLiveDrawHotkeyModifiers, ParseSequence(DefaultLiveDrawHotkeySequence), DefaultLiveDrawHotkeyDisplay);
        }

        internal static bool HotkeysEqual(HotkeySettings? a, HotkeySettings? b)
        {
            if (a is null || b is null)
            {
                return false;
            }

            if (a.Modifiers != b.Modifiers || !string.Equals(a.Display, b.Display, StringComparison.Ordinal))
            {
                return false;
            }

            if (a.Sequence.Count != b.Sequence.Count)
            {
                return false;
            }

            for (var i = 0; i < a.Sequence.Count; i++)
            {
                if (a.Sequence[i] != b.Sequence[i])
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool HotkeyModifiersAndSequenceEqual(HotkeySettings? a, HotkeySettings? b)
        {
            if (a is null || b is null)
            {
                return false;
            }

            if (a.Modifiers != b.Modifiers || a.Sequence.Count != b.Sequence.Count)
            {
                return false;
            }

            for (var i = 0; i < a.Sequence.Count; i++)
            {
                if (a.Sequence[i] != b.Sequence[i])
                {
                    return false;
                }
            }

            return true;
        }

        internal static void SaveLiveDrawHotkey(uint modifiers, IReadOnlyList<uint> sequence, string display)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);
            var normalizedSequence = sequence.Where(key => key > 0).Take(MaxHotkeySequenceLength).ToArray();

            values[LiveDrawHotkeyModifiersKey] = (int)modifiers;
            values[LiveDrawHotkeySequenceKey] = SerializeSequence(normalizedSequence);
            if (normalizedSequence.Length > 0)
            {
                values[LiveDrawHotkeyVirtualKeyKey] = (int)normalizedSequence[0];
            }

            values[LiveDrawHotkeyDisplayKey] = display;
            values[LiveDrawHotkeyClearedKey] = false;
            SettingsChanged?.Invoke();
        }

        internal static void ClearLiveDrawHotkey()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);

            values.Remove(LiveDrawHotkeyModifiersKey);
            values.Remove(LiveDrawHotkeyVirtualKeyKey);
            values.Remove(LiveDrawHotkeySequenceKey);
            values.Remove(LiveDrawHotkeyDisplayKey);
            values[LiveDrawHotkeyClearedKey] = true;
            SettingsChanged?.Invoke();
        }

        internal static LiveDrawShapeModifiers LoadLiveDrawShapeModifiers()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);
            return ReadLiveDrawShapeModifiers(values);
        }

        internal static bool TrySaveLiveDrawShapeModifiers(LiveDrawShapeModifiers modifiers, out string errorMessage)
        {
            if (!ValidateLiveDrawShapeModifiers(modifiers, out errorMessage))
            {
                return false;
            }

            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);
            WriteLiveDrawShapeModifiers(values, modifiers);
            SettingsChanged?.Invoke();
            return true;
        }

        internal static bool ValidateLiveDrawShapeModifiers(LiveDrawShapeModifiers modifiers, out string errorMessage)
        {
            var left = new[] { modifiers.Rectangle, modifiers.Arrow, modifiers.StraightLine };
            var leftActive = left.Where(m => m != LiveDrawRectangleModifier.None).ToArray();
            if (leftActive.Length != leftActive.Distinct().Count())
            {
                errorMessage = "Left-button shapes: each non-None modifier must be unique.";
                return false;
            }

            if (modifiers.CircleRight != LiveDrawRectangleModifier.None &&
                modifiers.EllipseRight != LiveDrawRectangleModifier.None &&
                modifiers.CircleRight == modifiers.EllipseRight)
            {
                errorMessage = "Circle and Ellipse must use different modifiers on the right mouse button (or set one to None).";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private static LiveDrawShapeModifiers ReadLiveDrawShapeModifiers(IPropertySet values)
        {
            if (!values.ContainsKey(LiveDrawShapeModRectangleKey))
            {
                EnsureLiveDrawShapeModifiers(values);
            }

            static LiveDrawRectangleModifier Read(IPropertySet v, string key, LiveDrawRectangleModifier fallback)
            {
                if (v.TryGetValue(key, out var raw) && raw is int i && Enum.IsDefined(typeof(LiveDrawRectangleModifier), i))
                {
                    return (LiveDrawRectangleModifier)i;
                }

                return fallback;
            }

            return new LiveDrawShapeModifiers(
                Read(values, LiveDrawShapeModRectangleKey, LiveDrawRectangleModifier.Shift),
                Read(values, LiveDrawShapeModArrowKey, LiveDrawRectangleModifier.Alt),
                Read(values, LiveDrawShapeModStraightLineKey, LiveDrawRectangleModifier.Control),
                Read(values, LiveDrawShapeModCircleRightKey, LiveDrawRectangleModifier.Shift),
                Read(values, LiveDrawShapeModEllipseRightKey, LiveDrawRectangleModifier.Alt));
        }

        private static void WriteLiveDrawShapeModifiers(IPropertySet values, LiveDrawShapeModifiers modifiers)
        {
            values[LiveDrawShapeModRectangleKey] = (int)modifiers.Rectangle;
            values[LiveDrawShapeModArrowKey] = (int)modifiers.Arrow;
            values[LiveDrawShapeModStraightLineKey] = (int)modifiers.StraightLine;
            values[LiveDrawShapeModCircleRightKey] = (int)modifiers.CircleRight;
            values[LiveDrawShapeModEllipseRightKey] = (int)modifiers.EllipseRight;
        }

        private static void WriteDefaultLiveDrawShapeModifiers(IPropertySet values)
        {
            if (!TryBuildDefaultLiveDrawShapeModifiers(out var defaults))
            {
                return;
            }

            WriteLiveDrawShapeModifiers(values, defaults);
        }

        private static bool TryBuildDefaultLiveDrawShapeModifiers(out LiveDrawShapeModifiers modifiers)
        {
            modifiers = BuildDistinctLeftTripleFromRectangle(LiveDrawRectangleModifier.Shift);
            return ValidateLiveDrawShapeModifiers(modifiers, out _);
        }

        private static void EnsureLiveDrawShapeModifiers(IPropertySet values)
        {
            if (values.ContainsKey(LiveDrawShapeModRectangleKey) &&
                values.ContainsKey(LiveDrawShapeModArrowKey) &&
                values.ContainsKey(LiveDrawShapeModStraightLineKey) &&
                values.ContainsKey(LiveDrawShapeModCircleRightKey) &&
                values.ContainsKey(LiveDrawShapeModEllipseRightKey))
            {
                return;
            }

            LiveDrawRectangleModifier legacy;
            if (values.TryGetValue(LiveDrawRectangleModifierKey, out var legacyRaw) && legacyRaw is int li &&
                Enum.IsDefined(typeof(LiveDrawRectangleModifier), li))
            {
                legacy = (LiveDrawRectangleModifier)li;
            }
            else
            {
                legacy = LiveDrawRectangleModifier.Shift;
            }

            var left = BuildDistinctLeftTripleFromRectangle(legacy);
            var circle = LiveDrawRectangleModifier.Shift;
            var ellipse = LiveDrawRectangleModifier.Alt;
            if (circle == ellipse)
            {
                ellipse = LiveDrawRectangleModifier.Control;
            }

            var combined = new LiveDrawShapeModifiers(
                left.Rectangle,
                left.Arrow,
                left.StraightLine,
                circle,
                ellipse);

            if (!ValidateLiveDrawShapeModifiers(combined, out _))
            {
                TryBuildDefaultLiveDrawShapeModifiers(out combined);
            }

            WriteLiveDrawShapeModifiers(values, combined);
        }

        private static LiveDrawShapeModifiers BuildDistinctLeftTripleFromRectangle(LiveDrawRectangleModifier rectangle)
        {
            var order = new[]
            {
                LiveDrawRectangleModifier.Shift,
                LiveDrawRectangleModifier.Alt,
                LiveDrawRectangleModifier.Control,
                LiveDrawRectangleModifier.Win
            };

            var arrow = order.First(m => m != rectangle);
            var line = order.First(m => m != rectangle && m != arrow);
            return new LiveDrawShapeModifiers(rectangle, arrow, line, LiveDrawRectangleModifier.Shift, LiveDrawRectangleModifier.Alt);
        }

        internal static bool IsLiveDrawHotkeyExplicitlyCleared()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);
            return values.TryGetValue(LiveDrawHotkeyClearedKey, out var clearedValue) &&
                   clearedValue is bool c &&
                   c;
        }

        internal static bool TryGetEffectiveLiveDrawHotkey(out HotkeySettings hotkey)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);

            var isCleared = values.TryGetValue(LiveDrawHotkeyClearedKey, out var clearedValue) &&
                            clearedValue is bool c && c;

            HotkeySettings? parsed = null;
            if (!isCleared &&
                values.TryGetValue(LiveDrawHotkeyModifiersKey, out var modifiersValue) &&
                values.TryGetValue(LiveDrawHotkeyDisplayKey, out var displayValue) &&
                modifiersValue is int modifiersInt &&
                displayValue is string display &&
                !string.IsNullOrWhiteSpace(display) &&
                modifiersInt >= 0)
            {
                var seq = ReadLiveDrawSequence(values);
                if (seq.Count == 0 &&
                    values.TryGetValue(LiveDrawHotkeyVirtualKeyKey, out var liveVk) &&
                    liveVk is int liveVkInt && liveVkInt > 0)
                {
                    seq = new[] { (uint)liveVkInt };
                }

                if (seq.Count > 0)
                {
                    parsed = new HotkeySettings((uint)modifiersInt, seq, display);
                }
            }

            if (parsed is not null && IsValidHotkey(parsed))
            {
                hotkey = parsed;
                return true;
            }

            if (!isCleared)
            {
                var fallback = GetDefaultLiveDrawHotkey();
                if (IsValidHotkey(fallback))
                {
                    hotkey = fallback;
                    return true;
                }
            }

            hotkey = new HotkeySettings(0, Array.Empty<uint>(), string.Empty);
            return false;
        }

        private static IReadOnlyList<uint> ReadLiveDrawSequence(IPropertySet values)
        {
            if (!values.TryGetValue(LiveDrawHotkeySequenceKey, out var sequenceValue) || sequenceValue is not string serialized)
            {
                return Array.Empty<uint>();
            }

            return ParseSequence(serialized);
        }

        internal static bool TryGetEffectiveHotkey(out HotkeySettings hotkey)
        {
            var settings = Load();
            if (settings.Hotkey is not null && IsValidHotkey(settings.Hotkey))
            {
                hotkey = settings.Hotkey;
                return true;
            }

            if (!settings.IsHotkeyCleared)
            {
                var fallback = GetDefaultHotkey();
                if (IsValidHotkey(fallback))
                {
                    hotkey = fallback;
                    return true;
                }
            }

            hotkey = new HotkeySettings(0, Array.Empty<uint>(), string.Empty);
            return false;
        }

        internal static bool TryGetEffectiveSaveFolderPath(out string folderPath)
        {
            var settings = Load();
            if (settings.IsSaveFolderCleared)
            {
                folderPath = string.Empty;
                return false;
            }

            var candidatePath = !string.IsNullOrWhiteSpace(settings.SaveFolderPath)
                ? settings.SaveFolderPath
                : GetDefaultDesktopFolderPath();

            if (TryValidateWritableFolder(candidatePath, out _))
            {
                folderPath = candidatePath;
                return true;
            }

            folderPath = string.Empty;
            return false;
        }

        internal static void InitializeSaveFolderOnStartup()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            EnsureSettingsVersion(values);

            var isSaveFolderCleared = values.TryGetValue(SaveFolderClearedKey, out var saveFolderClearedValue) &&
                                      saveFolderClearedValue is bool saveFolderCleared &&
                                      saveFolderCleared;
            if (isSaveFolderCleared)
            {
                return;
            }

            var defaultPath = GetDefaultDesktopFolderPath();
            var persistedPath = values.TryGetValue(SaveFolderPathKey, out var folderValue)
                ? folderValue as string
                : null;
            var hasPersistedPath = !string.IsNullOrWhiteSpace(persistedPath);

            if (hasPersistedPath)
            {
                if (!PathsEqual(persistedPath!, defaultPath))
                {
                    return;
                }

                if (TryEnsureFolderExists(defaultPath, shouldNotifyOnCreate: true))
                {
                    values[SaveFolderPathKey] = defaultPath;
                    values[SaveFolderClearedKey] = false;
                }

                return;
            }

            if (!TryEnsureFolderExists(defaultPath, shouldNotifyOnCreate: true))
            {
                return;
            }

            values[SaveFolderPathKey] = defaultPath;
            values[SaveFolderClearedKey] = false;
        }

        internal static IReadOnlyList<GlobalSetupIssue> GetGlobalSetupIssues()
        {
            var issues = new List<GlobalSetupIssue>();
            if (!TryGetEffectiveSaveFolderPath(out _))
            {
                var defaultSaveFolderPath = GetDefaultDesktopFolderPath();
                issues.Add(new GlobalSetupIssue(
                    InfoBarSeverity.Error,
                    "Save location required",
                    "Choose a writable folder to save and open captures in Screen Tools.",
                    $"Use Default ({defaultSaveFolderPath})",
                    "use-default-save-folder",
                    "Open Settings",
                    "settings"));
            }

            var appSettings = Load();
            if (appSettings.CaptureHotkeyEnabled && !TryGetEffectiveHotkey(out _))
            {
                var defaultHotkey = GetDefaultHotkey();
                issues.Add(new GlobalSetupIssue(
                    InfoBarSeverity.Error,
                    "Capture hotkey required",
                    "Set a capture hotkey before capturing and saving to your folder.",
                    $"Use Default ({defaultHotkey.Display})",
                    "use-default-hotkey",
                    "Open Settings",
                    "settings"));
            }

            if (appSettings.LiveDrawEnabled && IsLiveDrawHotkeyExplicitlyCleared())
            {
                var defaultLive = GetDefaultLiveDrawHotkey();
                issues.Add(new GlobalSetupIssue(
                    InfoBarSeverity.Informational,
                    "Live Draw hotkey removed",
                    "Restore the default Live Draw shortcut or set a new one in Settings.",
                    $"Use Default ({defaultLive.Display})",
                    "use-default-live-draw-hotkey",
                    "Open Settings",
                    "settings"));
            }

            return issues;
        }

        internal static string GetDefaultDesktopFolderPath()
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktopPath))
            {
                desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            return Path.Combine(desktopPath, DefaultCaptureFolderName);
        }

        internal static bool TryEnsureDefaultDesktopFolder(out string folderPath)
        {
            folderPath = GetDefaultDesktopFolderPath();
            return TryEnsureFolderExists(folderPath, shouldNotifyOnCreate: true);
        }

        internal static bool TryValidateWritableFolder(string? folderPath, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                errorMessage = "No folder selected.";
                return false;
            }

            if (!Directory.Exists(folderPath))
            {
                errorMessage = "Folder does not exist.";
                return false;
            }

            var probePath = Path.Combine(folderPath, $".helvety-write-check-{Guid.NewGuid():N}.tmp");
            try
            {
                using var stream = new FileStream(
                    probePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1,
                    FileOptions.DeleteOnClose);
                stream.WriteByte(0);
            }
            catch (Exception ex)
            {
                errorMessage = $"Folder is not writable ({ex.Message})";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private static void EnsureSettingsVersion(IPropertySet values)
        {
            var storedVersion = values.TryGetValue(SettingsVersionKey, out var versionValue) && versionValue is int v
                ? v
                : 0;

            if (storedVersion >= CurrentSettingsVersion)
            {
                return;
            }

            if (storedVersion < 2)
            {
                ApplyLiveDrawDefaultsIfMissing(values);
            }

            if (storedVersion < 3)
            {
                if (!values.ContainsKey(CaptureHotkeyEnabledKey))
                {
                    values[CaptureHotkeyEnabledKey] = DefaultCaptureHotkeyEnabled;
                }

                if (!values.ContainsKey(LiveDrawEnabledKey))
                {
                    values[LiveDrawEnabledKey] = DefaultLiveDrawFeatureEnabled;
                }
            }

            if (storedVersion < 4)
            {
                EnsureLiveDrawShapeModifiers(values);
            }

            if (storedVersion < 5)
            {
                if (!values.ContainsKey(LiveDrawMainStrokeThicknessKey))
                {
                    values[LiveDrawMainStrokeThicknessKey] = DefaultLiveDrawMainStrokeThickness;
                }
            }

            values[SettingsVersionKey] = CurrentSettingsVersion;
        }

        private static void ApplyLiveDrawDefaultsIfMissing(IPropertySet values)
        {
            if (!values.ContainsKey(LiveDrawHotkeyClearedKey) &&
                !values.ContainsKey(LiveDrawHotkeyModifiersKey))
            {
                var defaultLive = GetDefaultLiveDrawHotkey();
                values[LiveDrawHotkeyModifiersKey] = (int)defaultLive.Modifiers;
                values[LiveDrawHotkeySequenceKey] = SerializeSequence(defaultLive.Sequence);
                if (defaultLive.Sequence.Count > 0)
                {
                    values[LiveDrawHotkeyVirtualKeyKey] = (int)defaultLive.Sequence[0];
                }

                values[LiveDrawHotkeyDisplayKey] = defaultLive.Display;
                values[LiveDrawHotkeyClearedKey] = false;
            }

            if (!values.ContainsKey(LiveDrawRectangleModifierKey))
            {
                values[LiveDrawRectangleModifierKey] = (int)LiveDrawRectangleModifier.Shift;
            }

            EnsureLiveDrawShapeModifiers(values);
        }

        private static void ApplyDefaultSettings(IPropertySet values)
        {
            values[SettingsVersionKey] = CurrentSettingsVersion;

            var defaultSaveFolderPath = GetDefaultDesktopFolderPath();
            values[SaveFolderPathKey] = defaultSaveFolderPath;
            values[SaveFolderClearedKey] = false;

            var defaultHotkey = GetDefaultHotkey();
            values[HotkeyModifiersKey] = (int)defaultHotkey.Modifiers;
            values[HotkeySequenceKey] = SerializeSequence(defaultHotkey.Sequence);
            if (defaultHotkey.Sequence.Count > 0)
            {
                values[HotkeyVirtualKeyKey] = (int)defaultHotkey.Sequence[0];
            }
            values[HotkeyDisplayKey] = defaultHotkey.Display;
            values[HotkeyClearedKey] = false;

            var defaultLiveDrawHotkey = GetDefaultLiveDrawHotkey();
            values[LiveDrawHotkeyModifiersKey] = (int)defaultLiveDrawHotkey.Modifiers;
            values[LiveDrawHotkeySequenceKey] = SerializeSequence(defaultLiveDrawHotkey.Sequence);
            if (defaultLiveDrawHotkey.Sequence.Count > 0)
            {
                values[LiveDrawHotkeyVirtualKeyKey] = (int)defaultLiveDrawHotkey.Sequence[0];
            }

            values[LiveDrawHotkeyDisplayKey] = defaultLiveDrawHotkey.Display;
            values[LiveDrawHotkeyClearedKey] = false;
            values[LiveDrawRectangleModifierKey] = (int)LiveDrawRectangleModifier.Shift;
            WriteDefaultLiveDrawShapeModifiers(values);
            values[CaptureHotkeyEnabledKey] = DefaultCaptureHotkeyEnabled;
            values[LiveDrawEnabledKey] = DefaultLiveDrawFeatureEnabled;
            values[LiveDrawFreeDrawEnabledKey] = DefaultLiveDrawFreeDrawEnabled;
            values[LiveDrawSparkleEnabledKey] = DefaultLiveDrawSparkleEnabled;

            values[ScreenshotBorderIntensityKey] = (int)DefaultScreenshotBorderIntensity;
            values[ScreenshotQualityModeKey] = (int)DefaultScreenshotQualityMode;
            values[ShowScreenshotOverlayInstructionsKey] = DefaultShowScreenshotOverlayInstructions;
            values[MinimizeToTrayOnCloseKey] = DefaultMinimizeToTrayOnClose;
            values[LiveDrawMainStrokeThicknessKey] = DefaultLiveDrawMainStrokeThickness;
            values[EditorPrimaryColorKey] = DefaultEditorPrimaryColor;
            values[EditorPrimaryThicknessKey] = DefaultEditorPrimaryThickness;
            values[EditorTextFontKey] = DefaultEditorTextFont;
            values[EditorTextSizeKey] = DefaultEditorTextSize;
            values[EditorTextBoldEnabledKey] = DefaultEditorTextBoldEnabled;
            values[EditorTextItalicEnabledKey] = DefaultEditorTextItalicEnabled;
            values[EditorTextBorderEnabledKey] = DefaultEditorTextBorderEnabled;
            values[EditorTextBorderColorKey] = DefaultEditorTextBorderColor;
            values[EditorTextBorderThicknessKey] = DefaultEditorTextBorderThickness;
            values[EditorTextShadowEnabledKey] = DefaultEditorTextShadowEnabled;
            values[EditorBorderShadowEnabledKey] = DefaultEditorBorderShadowEnabled;
            values[EditorArrowBorderEnabledKey] = DefaultEditorArrowBorderEnabled;
            values[EditorArrowBorderColorKey] = DefaultEditorArrowBorderColor;
            values[EditorArrowBorderThicknessKey] = DefaultEditorArrowBorderThickness;
            values[EditorArrowShadowEnabledKey] = DefaultEditorArrowShadowEnabled;
            values[EditorArrowFormStyleKey] = DefaultEditorArrowFormStyle;
            values[EditorBlurRadiusKey] = DefaultEditorBlurRadius;
            values[EditorBlurFeatherKey] = DefaultEditorBlurFeather;
            values[EditorBlurInvertModeKey] = DefaultEditorBlurInvertMode;
            values[EditorHighlightDimPercentKey] = DefaultEditorHighlightDimPercent;
            values[EditorHighlightInvertModeKey] = DefaultEditorHighlightInvertMode;
            values[EditorRegionCornerRadiusKey] = DefaultEditorRegionCornerRadius;
            values[EditorPerformanceModeEnabledKey] = DefaultEditorPerformanceModeEnabled;
            values[EditorGpuEffectsEnabledKey] = DefaultEditorGpuEffectsEnabled;
            values[EditorEmbedEditableMetadataKey] = DefaultEditorEmbedEditableMetadata;
        }

        private static bool IsValidHotkey(HotkeySettings hotkey)
        {
            return hotkey.Sequence.Count > 0 &&
                   hotkey.Sequence.Count <= MaxHotkeySequenceLength &&
                   hotkey.Sequence.All(key => key > 0) &&
                   !string.IsNullOrWhiteSpace(hotkey.Display);
        }

        private static IReadOnlyList<uint> ReadSequence(IPropertySet values)
        {
            if (!values.TryGetValue(HotkeySequenceKey, out var sequenceValue) || sequenceValue is not string serialized)
            {
                return Array.Empty<uint>();
            }

            return ParseSequence(serialized);
        }

        private static uint[] ParseSequence(string serialized)
        {
            if (string.IsNullOrWhiteSpace(serialized))
            {
                return Array.Empty<uint>();
            }

            return serialized
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => uint.TryParse(part, out var value) ? value : 0)
                .Where(value => value > 0)
                .Take(MaxHotkeySequenceLength)
                .ToArray();
        }

        private static string SerializeSequence(IReadOnlyList<uint> sequence)
        {
            if (sequence.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(',', sequence.Where(key => key > 0).Take(MaxHotkeySequenceLength));
        }

        private static bool TryEnsureFolderExists(string folderPath, bool shouldNotifyOnCreate)
        {
            try
            {
                var existedBefore = Directory.Exists(folderPath);
                Directory.CreateDirectory(folderPath);
                if (!existedBefore && shouldNotifyOnCreate)
                {
                    InAppToastService.Show($"Created save folder: {folderPath}", InAppToastSeverity.Success);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            var normalizedLeft = NormalizePath(left);
            var normalizedRight = NormalizePath(right);
            if (string.IsNullOrWhiteSpace(normalizedLeft) || string.IsNullOrWhiteSpace(normalizedRight))
            {
                return false;
            }

            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                var fullPath = Path.GetFullPath(path);
                return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private static int ReadInt(IPropertySet values, string key, int defaultValue, int minValue, int maxValue)
        {
            return values.TryGetValue(key, out var value) && value is int parsed
                ? Clamp(parsed, minValue, maxValue)
                : defaultValue;
        }

        private static bool ReadBool(IPropertySet values, string key, bool defaultValue)
        {
            return values.TryGetValue(key, out var value) && value is bool parsed
                ? parsed
                : defaultValue;
        }

        private static string ReadString(IPropertySet values, string key, string defaultValue)
        {
            return values.TryGetValue(key, out var value) &&
                   value is string parsed &&
                   !string.IsNullOrWhiteSpace(parsed)
                ? parsed
                : defaultValue;
        }

        private static int Clamp(int value, int minValue, int maxValue)
        {
            return Math.Max(minValue, Math.Min(maxValue, value));
        }
    }

    internal sealed record AppSettings(
        string? SaveFolderPath,
        HotkeySettings? Hotkey,
        bool IsHotkeyCleared,
        bool IsSaveFolderCleared,
        /// <summary>Global snap-border intensity for capture selection overlay and Live Draw.</summary>
        ScreenshotBorderIntensity ScreenshotBorderIntensity,
        ScreenshotQualityMode ScreenshotQualityMode,
        bool ShowScreenshotOverlayInstructions,
        bool MinimizeToTrayOnClose,
        bool CaptureHotkeyEnabled,
        bool LiveDrawEnabled);

    internal sealed record HotkeySettings(uint Modifiers, IReadOnlyList<uint> Sequence, string Display);

    internal sealed record EditorUiSettings(
        string PrimaryColorHex,
        int PrimaryThickness,
        string TextFont,
        int TextSize,
        bool TextBoldEnabled,
        bool TextItalicEnabled,
        bool TextBorderEnabled,
        string TextBorderColorHex,
        int TextBorderThickness,
        bool TextShadowEnabled,
        bool BorderShadowEnabled,
        bool ArrowBorderEnabled,
        string ArrowBorderColorHex,
        int ArrowBorderThickness,
        bool ArrowShadowEnabled,
        string ArrowFormStyle,
        int BlurRadius,
        int BlurFeather,
        bool BlurInvertMode,
        int HighlightDimPercent,
        bool HighlightInvertMode,
        int RegionCornerRadius,
        bool PerformanceModeEnabled,
        bool GpuEffectsEnabled);

    /// <summary>
    /// Visual strength of shared snap-border chrome (gradients, dashing, pulse) for frozen-screen capture and Live Draw.
    /// </summary>
    internal enum ScreenshotBorderIntensity
    {
        Subtle = 0,
        Balanced = 1,
        Bold = 2
    }

    internal enum ScreenshotQualityMode
    {
        Fast = 0,
        Optimized = 1,
        Heavy = 2
    }

    internal readonly record struct LiveDrawShapeModifiers(
        LiveDrawRectangleModifier Rectangle,
        LiveDrawRectangleModifier Arrow,
        LiveDrawRectangleModifier StraightLine,
        LiveDrawRectangleModifier CircleRight,
        LiveDrawRectangleModifier EllipseRight);

    internal sealed record LiveDrawDrawingSettings(
        int MainStrokeThickness,
        bool FreeDrawEnabled,
        bool SparkleEnabled);

    internal enum LiveDrawRectangleModifier
    {
        Shift = 0,
        Control = 1,
        Win = 2,
        Alt = 3,
        None = 4
    }

    internal sealed record GlobalSetupIssue(
        InfoBarSeverity Severity,
        string Title,
        string Message,
        string PrimaryActionText,
        string PrimaryActionTag,
        string SecondaryActionText,
        string SecondaryActionTag);
}
