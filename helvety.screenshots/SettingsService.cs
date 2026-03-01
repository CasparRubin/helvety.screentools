using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation.Collections;
using Windows.Storage;

namespace helvety.screenshots
{
    internal static class SettingsService
    {
        internal const int MaxHotkeySequenceLength = 5;
        private const uint DefaultHotkeyModifiers = 0x0004; // Shift
        private const string DefaultHotkeyDisplay = "Shift+S+S+S";
        private const string DefaultHotkeySequence = "83,83,83";

        internal static event Action? SaveFolderPathChanged;
        internal static event Action? SettingsChanged;

        private const int CurrentSettingsVersion = 1;
        private const string DefaultScreenshotsFolderName = "Screenshots (Helvety)";
        private const string SettingsVersionKey = "SettingsVersion";
        private const string SaveFolderPathKey = "SaveFolderPath";
        private const string HotkeyModifiersKey = "HotkeyModifiers";
        private const string HotkeyVirtualKeyKey = "HotkeyVirtualKey";
        private const string HotkeySequenceKey = "HotkeySequence";
        private const string HotkeyDisplayKey = "HotkeyDisplay";
        private const string HotkeyClearedKey = "HotkeyCleared";
        private const string SaveFolderClearedKey = "SaveFolderCleared";

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

            return new AppSettings(saveFolderPath, hotkey, isHotkeyCleared, isSaveFolderCleared);
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

        internal static HotkeySettings GetDefaultHotkey()
        {
            return new HotkeySettings(DefaultHotkeyModifiers, ParseSequence(DefaultHotkeySequence), DefaultHotkeyDisplay);
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

        internal static IReadOnlyList<GlobalSetupIssue> GetGlobalSetupIssues()
        {
            var issues = new List<GlobalSetupIssue>();
            if (!TryGetEffectiveSaveFolderPath(out _))
            {
                var defaultSaveFolderPath = GetDefaultDesktopFolderPath();
                issues.Add(new GlobalSetupIssue(
                    InfoBarSeverity.Error,
                    "Save location required",
                    "Choose a writable folder to enable screenshot features.",
                    $"Use Default ({defaultSaveFolderPath})",
                    "use-default-save-folder",
                    "Open Settings",
                    "settings"));
            }

            if (!TryGetEffectiveHotkey(out _))
            {
                var defaultHotkey = GetDefaultHotkey();
                issues.Add(new GlobalSetupIssue(
                    InfoBarSeverity.Error,
                    "Hotkey required",
                    "Set a key-binding before using screenshot features.",
                    $"Use Default ({defaultHotkey.Display})",
                    "use-default-hotkey",
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
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            var screenshotsSubfolder = Path.Combine(desktopPath, DefaultScreenshotsFolderName);
            try
            {
                Directory.CreateDirectory(screenshotsSubfolder);
                return screenshotsSubfolder;
            }
            catch
            {
                return desktopPath;
            }
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
            if (values.TryGetValue(SettingsVersionKey, out var versionValue) &&
                versionValue is int version &&
                version >= CurrentSettingsVersion)
            {
                return;
            }

            values[SettingsVersionKey] = CurrentSettingsVersion;
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
    }

    internal sealed record AppSettings(string? SaveFolderPath, HotkeySettings? Hotkey, bool IsHotkeyCleared, bool IsSaveFolderCleared);

    internal sealed record HotkeySettings(uint Modifiers, IReadOnlyList<uint> Sequence, string Display);

    internal sealed record GlobalSetupIssue(
        InfoBarSeverity Severity,
        string Title,
        string Message,
        string PrimaryActionText,
        string PrimaryActionTag,
        string SecondaryActionText,
        string SecondaryActionTag);
}
