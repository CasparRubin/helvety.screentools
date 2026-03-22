using helvety.screentools;
using static helvety.screentools.HotkeyVisualMapper;
using helvety.screentools.Views.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace helvety.screentools.Views
{
    /// <summary>
    /// Application settings: save folder, capture and Live Draw hotkeys (Listen/Clear per step, visual chord preview),
    /// capture mode, editor, and app behavior options.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        private const int WhKeyboardLl = 13;
        private const uint WmKeydown = 0x0100;
        private const uint WmKeyup = 0x0101;
        private const uint WmSyskeydown = 0x0104;
        private const uint WmSyskeyup = 0x0105;
        private const int ProbeHotkeyId = 40001;
        private const int VkEscape = 0x1B;
        private const int VkPrintScreen = 0x2C;
        private const int VkShift = 0x10;
        private const int VkControl = 0x11;
        private const int VkMenu = 0x12;
        private const int VkLwin = 0x5B;
        private const int VkRwin = 0x5C;
        private const int MaxSequenceLength = 5;
        private const int SequenceStepTimeoutMilliseconds = 700;
        private const string DefaultHotkeySequenceInstructionText = "Click Listen for a step, then press any non-modifier key.";
        private const string CaptureBlockedInstructionText = "Choose a save location first.";

        private string _saveFolderPath = string.Empty;
        private bool _hasValidSaveFolder;
        private HotkeyBinding? _currentBinding;
        private HotkeyBinding? _startupBinding;
        private nint _keyboardHookHandle;
        private KeyboardHookProc? _keyboardHookProc;
        private bool _isKeyboardHookInstalled;
        private int _sequenceMatchIndex;
        private uint? _runtimePressedKey;
        private long _lastMatchedStepAt;
        private bool _isCaptureMode;
        private int? _activeCaptureStepIndex;
        private readonly uint?[] _editorSequence = new uint?[MaxSequenceLength];
        private uint _editorModifiers;
        private bool _isUpdatingBorderIntensitySelection;
        private bool _isUpdatingScreenshotQualitySelection;
        private bool _isUpdatingOverlayInstructionSelection;
        private bool _isUpdatingMinimizeToTraySelection;
        private bool _isUpdatingEditorPerformanceModeSelection;

        public SettingsPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;

            InitializeSettings();
            InitializeHotkeyInfrastructure();
            RegisterInitialBinding();

            SettingsService.SaveFolderPathChanged += SettingsService_SaveFolderPathChanged;
            if (App.MainAppWindow is not null)
            {
                App.MainAppWindow.Closed += MainWindow_Closed;
            }

            Unloaded += SettingsPage_Unloaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            RefreshSaveFolderState();
        }

        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            SettingsService.SaveFolderPathChanged -= SettingsService_SaveFolderPathChanged;
            if (App.MainAppWindow is not null)
            {
                App.MainAppWindow.Closed -= MainWindow_Closed;
            }

            Unloaded -= SettingsPage_Unloaded;
        }

        private void SettingsService_SaveFolderPathChanged()
        {
            DispatcherQueue.TryEnqueue(RefreshSaveFolderState);
        }

        private void InitializeSettings()
        {
            InitializeBorderIntensitySelection();
            InitializeScreenshotQualitySelection();
            InitializeOverlayInstructionSelection();
            InitializeMinimizeToTraySelection();
            InitializeEditorPerformanceModeSelection();

            if (SettingsService.TryGetEffectiveSaveFolderPath(out var effectiveSaveFolderPath))
            {
                _saveFolderPath = effectiveSaveFolderPath;
            }
            else
            {
                var settings = SettingsService.Load();
                _saveFolderPath = settings.IsSaveFolderCleared
                    ? string.Empty
                    : settings.SaveFolderPath ?? string.Empty;
            }

            _startupBinding = SettingsService.TryGetEffectiveHotkey(out var effectiveHotkey)
                ? new HotkeyBinding(effectiveHotkey.Modifiers, effectiveHotkey.Sequence.ToArray(), effectiveHotkey.Display)
                : null;

            RefreshSaveFolderState();
            InitializeLiveDrawHotkeyUi();
        }

        private void InitializeBorderIntensitySelection()
        {
            var settings = SettingsService.Load();
            _isUpdatingBorderIntensitySelection = true;
            try
            {
                BorderIntensityComboBox.SelectedIndex = settings.ScreenshotBorderIntensity switch
                {
                    ScreenshotBorderIntensity.Subtle => 0,
                    ScreenshotBorderIntensity.Bold => 2,
                    _ => 1
                };
            }
            finally
            {
                _isUpdatingBorderIntensitySelection = false;
            }
        }

        private void InitializeOverlayInstructionSelection()
        {
            var settings = SettingsService.Load();
            _isUpdatingOverlayInstructionSelection = true;
            try
            {
                ShowOverlayInstructionsCheckBox.IsChecked = settings.ShowScreenshotOverlayInstructions;
            }
            finally
            {
                _isUpdatingOverlayInstructionSelection = false;
            }
        }

        private void InitializeScreenshotQualitySelection()
        {
            var settings = SettingsService.Load();
            _isUpdatingScreenshotQualitySelection = true;
            try
            {
                ScreenshotQualityModeComboBox.SelectedIndex = settings.ScreenshotQualityMode switch
                {
                    ScreenshotQualityMode.Optimized => 1,
                    ScreenshotQualityMode.Heavy => 2,
                    _ => 0
                };
            }
            finally
            {
                _isUpdatingScreenshotQualitySelection = false;
            }
        }

        private void InitializeMinimizeToTraySelection()
        {
            var settings = SettingsService.Load();
            _isUpdatingMinimizeToTraySelection = true;
            try
            {
                MinimizeToTrayOnCloseCheckBox.IsChecked = settings.MinimizeToTrayOnClose;
            }
            finally
            {
                _isUpdatingMinimizeToTraySelection = false;
            }
        }

        private void InitializeEditorPerformanceModeSelection()
        {
            var settings = SettingsService.LoadEditorUiSettings();
            _isUpdatingEditorPerformanceModeSelection = true;
            try
            {
                EditorPerformanceModeCheckBox.IsChecked = settings.PerformanceModeEnabled;
            }
            finally
            {
                _isUpdatingEditorPerformanceModeSelection = false;
            }
        }

        private void RefreshSaveFolderState()
        {
            if (SettingsService.TryGetEffectiveSaveFolderPath(out var effectiveSaveFolderPath))
            {
                _saveFolderPath = effectiveSaveFolderPath;
            }
            else
            {
                var settings = SettingsService.Load();
                _saveFolderPath = settings.IsSaveFolderCleared
                    ? string.Empty
                    : settings.SaveFolderPath ?? string.Empty;
            }

            UpdateSaveFolderState();
        }

        private void UpdateSaveFolderState()
        {
            if (string.IsNullOrWhiteSpace(_saveFolderPath))
            {
                _hasValidSaveFolder = false;
                SaveFolderText.Text = "Save Folder: (none)";
                SaveFolderStatusText.Text = "No save location set.";
                RemoveSaveFolderButton.IsEnabled = false;
                UpdateFeatureAvailability();
                return;
            }

            if (SettingsService.TryValidateWritableFolder(_saveFolderPath, out var validationError))
            {
                _hasValidSaveFolder = true;
                SettingsService.SaveFolderPath(_saveFolderPath);
                SaveFolderText.Text = $"Save Folder: {_saveFolderPath}";
                SaveFolderStatusText.Text = string.Empty;
                RemoveSaveFolderButton.IsEnabled = true;
                UpdateFeatureAvailability();
                return;
            }

            _hasValidSaveFolder = false;
            SaveFolderText.Text = $"Save Folder: {_saveFolderPath}";
            SaveFolderStatusText.Text = $"Choose a writable folder ({validationError}).";
            RemoveSaveFolderButton.IsEnabled = true;
            UpdateFeatureAvailability();
        }

        private async void ChooseSaveFolderButton_Click(object sender, RoutedEventArgs e)
        {
            ChooseSaveFolderButton.IsEnabled = false;
            UseDefaultSaveFolderButton.IsEnabled = false;
            RemoveSaveFolderButton.IsEnabled = false;
            SaveFolderStatusText.Text = "Choosing folder...";

            try
            {
                var folderPicker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.Desktop
                };
                folderPicker.FileTypeFilter.Add("*");

                if (App.MainAppWindow is null)
                {
                    SaveFolderStatusText.Text = "Unable to open folder picker.";
                    return;
                }

                var windowHandle = WindowNative.GetWindowHandle(App.MainAppWindow);
                InitializeWithWindow.Initialize(folderPicker, windowHandle);

                var selectedFolder = await folderPicker.PickSingleFolderAsync();
                if (selectedFolder is null)
                {
                    SaveFolderStatusText.Text = _hasValidSaveFolder
                        ? string.Empty
                        : "Choose a writable folder.";
                    return;
                }

                var candidatePath = selectedFolder.Path;
                if (!SettingsService.TryValidateWritableFolder(candidatePath, out var validationError))
                {
                    SaveFolderStatusText.Text = _hasValidSaveFolder
                        ? $"Folder not writable ({validationError})."
                        : $"Choose a writable folder ({validationError}).";
                    return;
                }

                _saveFolderPath = candidatePath;
                SettingsService.SaveFolderPath(_saveFolderPath);
                UpdateSaveFolderState();
            }
            catch (Exception ex)
            {
                SaveFolderStatusText.Text = $"Could not set folder ({ex.Message}).";
            }
            finally
            {
                ChooseSaveFolderButton.IsEnabled = true;
                UseDefaultSaveFolderButton.IsEnabled = true;
                RemoveSaveFolderButton.IsEnabled = !string.IsNullOrWhiteSpace(_saveFolderPath);
            }
        }

        private void UseDefaultSaveFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (!SettingsService.TryEnsureDefaultDesktopFolder(out var defaultPath))
            {
                SaveFolderStatusText.Text = "Could not create default folder.";
                return;
            }

            if (!SettingsService.TryValidateWritableFolder(defaultPath, out var validationError))
            {
                SaveFolderStatusText.Text = $"Default folder not writable ({validationError}).";
                return;
            }

            _saveFolderPath = defaultPath;
            SettingsService.SaveFolderPath(_saveFolderPath);
            UpdateSaveFolderState();
        }

        private void RemoveSaveFolderButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsService.ClearSaveFolderPath();
            _saveFolderPath = string.Empty;
            UpdateSaveFolderState();
        }

        private void UpdateFeatureAvailability()
        {
            ApplyHotkeyButton.IsEnabled = _hasValidSaveFolder && !_isCaptureMode && BuildEditorSequence().Count > 0 &&
                !TryScreenshotEditorConflictsWithLiveHotkey();
            UseDefaultHotkeyButton.IsEnabled = _hasValidSaveFolder && !_isCaptureMode;
            RemoveHotkeyButton.IsEnabled = !_isCaptureMode && _currentBinding is not null;
            CaptureInstructionText.Text = _hasValidSaveFolder
                ? DefaultHotkeySequenceInstructionText
                : CaptureBlockedInstructionText;

            if (_hasValidSaveFolder && BindingStatusText.Text.StartsWith("Save location needed", StringComparison.Ordinal))
            {
                BindingStatusText.Text = string.Empty;
            }
            else if (!_hasValidSaveFolder)
            {
                if (string.IsNullOrWhiteSpace(_saveFolderPath))
                {
                    BindingStatusText.Text = "Save location needed: no folder selected.";
                }
                else if (SettingsService.TryValidateWritableFolder(_saveFolderPath, out var validationError))
                {
                    BindingStatusText.Text = "Save location needed: choose a writable folder.";
                }
                else
                {
                    BindingStatusText.Text = $"Save location needed: {validationError}";
                }
            }

            UpdateLiveDrawFeatureAvailability();
        }

        private void InitializeHotkeyInfrastructure()
        {
            _keyboardHookProc = KeyboardHookCallback;
            _keyboardHookHandle = SetWindowsHookEx(WhKeyboardLl, _keyboardHookProc, nint.Zero, 0);
            _isKeyboardHookInstalled = _keyboardHookHandle != nint.Zero;
        }

        private void RegisterInitialBinding()
        {
            if (_startupBinding is null)
            {
                _currentBinding = null;
                ResetRuntimeSequenceState();
                ResetEditor();
                CaptureCurrentChordStrip.SetEmpty("(none)", "Capture hotkey: (none)");
                BindingStatusText.Text = "No capture hotkey set.";
                UpdateFeatureAvailability();
                return;
            }

            if (TryApplyBinding(_startupBinding.Value, out var statusMessage))
            {
                BindingStatusText.Text = string.Empty;
                return;
            }

            BindingStatusText.Text = statusMessage;
            AddMessage(statusMessage);
        }

        private void ResetEditor()
        {
            _editorModifiers = 0;
            for (var i = 0; i < MaxSequenceLength; i++)
            {
                _editorSequence[i] = null;
            }

            CtrlModifierCheckBox.IsChecked = false;
            AltModifierCheckBox.IsChecked = false;
            ShiftModifierCheckBox.IsChecked = false;
            UpdateStepTexts();
            UpdateCapturePreview();
        }

        private void SetEditorFromBinding(HotkeyBinding binding)
        {
            _editorModifiers = binding.Modifiers & (ModControl | ModAlt | ModShift);
            CtrlModifierCheckBox.IsChecked = (_editorModifiers & ModControl) != 0;
            AltModifierCheckBox.IsChecked = (_editorModifiers & ModAlt) != 0;
            ShiftModifierCheckBox.IsChecked = (_editorModifiers & ModShift) != 0;

            for (var i = 0; i < MaxSequenceLength; i++)
            {
                _editorSequence[i] = i < binding.Sequence.Length
                    ? binding.Sequence[i]
                    : null;
            }

            UpdateStepTexts();
            UpdateCapturePreview();
        }

        private List<uint> BuildEditorSequence()
        {
            var sequence = new List<uint>(MaxSequenceLength);
            for (var i = 0; i < MaxSequenceLength; i++)
            {
                if (!_editorSequence[i].HasValue)
                {
                    break;
                }

                sequence.Add(_editorSequence[i]!.Value);
            }

            return sequence;
        }

        private uint BuildModifiersFromEditor()
        {
            uint modifiers = 0;
            if (CtrlModifierCheckBox.IsChecked == true)
            {
                modifiers |= ModControl;
            }

            if (AltModifierCheckBox.IsChecked == true)
            {
                modifiers |= ModAlt;
            }

            if (ShiftModifierCheckBox.IsChecked == true)
            {
                modifiers |= ModShift;
            }

            return modifiers;
        }

        private void UpdateStepTexts()
        {
            Step1KeyChordStrip.SetSingleKey(_editorSequence[0], HotkeyChordAppearance.Default, null);
            Step2KeyChordStrip.SetSingleKey(_editorSequence[1], HotkeyChordAppearance.Default, null);
            Step3KeyChordStrip.SetSingleKey(_editorSequence[2], HotkeyChordAppearance.Default, null);
            Step4KeyChordStrip.SetSingleKey(_editorSequence[3], HotkeyChordAppearance.Default, null);
            Step5KeyChordStrip.SetSingleKey(_editorSequence[4], HotkeyChordAppearance.Default, null);
        }

        private void UseDefaultHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_hasValidSaveFolder)
            {
                const string blockedMessage = "Set a save location before changing hotkeys.";
                BindingStatusText.Text = blockedMessage;
                AddMessage(blockedMessage);
                return;
            }

            var defaultHotkey = SettingsService.GetDefaultHotkey();
            SetEditorFromBinding(new HotkeyBinding(defaultHotkey.Modifiers, defaultHotkey.Sequence.ToArray(), defaultHotkey.Display));

            if (TryApplyEditorBinding(out var statusMessage))
            {
                BindingStatusText.Text = statusMessage;
                if (!string.IsNullOrWhiteSpace(statusMessage))
                {
                    AddMessage(statusMessage);
                }

                return;
            }

            BindingStatusText.Text = statusMessage;
            AddMessage(statusMessage);
        }

        private bool TryApplyBinding(HotkeyBinding requestedBinding, out string statusMessage)
        {
            if (!_isKeyboardHookInstalled)
            {
                var hookError = Marshal.GetLastWin32Error();
                statusMessage = $"Keyboard hook failed to install (error {hookError}).";
                return false;
            }

            _currentBinding = requestedBinding;
            ResetRuntimeSequenceState();
            CaptureCurrentChordStrip.SetChord(
                requestedBinding.Modifiers,
                requestedBinding.Sequence,
                HotkeyChordAppearance.Accent,
                $"Capture hotkey: {requestedBinding.Display}");
            SettingsService.SaveHotkey(requestedBinding.Modifiers, requestedBinding.Sequence, requestedBinding.Display);
            UpdateFeatureAvailability();
            SetEditorFromBinding(requestedBinding);
            statusMessage = IsLikelyClaimedByAnotherHotkey(requestedBinding)
                ? "This combo appears to be claimed/reserved by another app or Windows, but this app still listens with its keyboard hook."
                : string.Empty;

            return true;
        }

        private void RemoveHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            StopStepCapture();
            _currentBinding = null;
            ResetRuntimeSequenceState();
            SettingsService.ClearHotkey();
            ResetEditor();
            CaptureCurrentChordStrip.SetEmpty("(none)", "Capture hotkey: (none)");
            BindingStatusText.Text = "No capture hotkey set.";
            UpdateFeatureAvailability();
        }

        private void ApplyHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (TryApplyEditorBinding(out var statusMessage))
            {
                BindingStatusText.Text = statusMessage;
                if (!string.IsNullOrWhiteSpace(statusMessage))
                {
                    AddMessage(statusMessage);
                }
                return;
            }

            BindingStatusText.Text = statusMessage;
            AddMessage(statusMessage);
        }

        private bool TryApplyEditorBinding(out string statusMessage)
        {
            if (!_hasValidSaveFolder)
            {
                statusMessage = "Set a save location before changing hotkeys.";
                return false;
            }

            var sequence = BuildEditorSequence();
            if (sequence.Count == 0)
            {
                statusMessage = "Set at least one sequence key.";
                return false;
            }

            var display = HotkeyVisualMapper.BuildBindingDisplay(_editorModifiers, sequence.Select(HotkeyVisualMapper.GetKeyDisplayName).ToArray());
            var candidate = new HotkeySettings(_editorModifiers, sequence, display);
            if (SettingsService.TryGetEffectiveLiveDrawHotkey(out var live) &&
                SettingsService.HotkeyModifiersAndSequenceEqual(candidate, live))
            {
                statusMessage = "Capture hotkey must differ from the Live Draw hotkey.";
                return false;
            }

            var binding = new HotkeyBinding(_editorModifiers, sequence.ToArray(), display);
            return TryApplyBinding(binding, out statusMessage);
        }

        private bool TryScreenshotEditorConflictsWithLiveHotkey()
        {
            var sequence = BuildEditorSequence();
            if (sequence.Count == 0)
            {
                return false;
            }

            if (!SettingsService.TryGetEffectiveLiveDrawHotkey(out var live))
            {
                return false;
            }

            var display = HotkeyVisualMapper.BuildBindingDisplay(_editorModifiers, sequence.Select(HotkeyVisualMapper.GetKeyDisplayName).ToArray());
            var candidate = new HotkeySettings(_editorModifiers, sequence, display);
            return SettingsService.HotkeyModifiersAndSequenceEqual(candidate, live);
        }

        private void ModifierCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _editorModifiers = BuildModifiersFromEditor();
            UpdateCapturePreview();
            UpdateFeatureAvailability();
            UpdateLiveDrawFeatureAvailability();
        }

        private void BorderIntensityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingBorderIntensitySelection)
            {
                return;
            }

            var selectedIntensity = BorderIntensityComboBox.SelectedIndex switch
            {
                0 => ScreenshotBorderIntensity.Subtle,
                2 => ScreenshotBorderIntensity.Bold,
                _ => ScreenshotBorderIntensity.Balanced
            };

            SettingsService.SaveScreenshotBorderIntensity(selectedIntensity);
        }

        private void ShowOverlayInstructionsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingOverlayInstructionSelection)
            {
                return;
            }

            var shouldShowOverlayInstructions = ShowOverlayInstructionsCheckBox.IsChecked != false;
            SettingsService.SaveShowScreenshotOverlayInstructions(shouldShowOverlayInstructions);
        }

        private void ScreenshotQualityModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingScreenshotQualitySelection)
            {
                return;
            }

            var selectedQualityMode = ScreenshotQualityModeComboBox.SelectedIndex switch
            {
                1 => ScreenshotQualityMode.Optimized,
                2 => ScreenshotQualityMode.Heavy,
                _ => ScreenshotQualityMode.Fast
            };

            SettingsService.SaveScreenshotQualityMode(selectedQualityMode);
        }

        private void MinimizeToTrayOnCloseCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingMinimizeToTraySelection)
            {
                return;
            }

            var shouldMinimizeToTray = MinimizeToTrayOnCloseCheckBox.IsChecked != false;
            SettingsService.SaveMinimizeToTrayOnClose(shouldMinimizeToTray);
        }

        private void EditorPerformanceModeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingEditorPerformanceModeSelection)
            {
                return;
            }

            var isEnabled = EditorPerformanceModeCheckBox.IsChecked != false;
            SettingsService.SaveEditorPerformanceModeEnabled(isEnabled);
        }

        private async void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var confirmationDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Reset all settings to defaults?",
                Content = "This clears all saved app settings and restores defaults. Files on disk (captures and exports) are not deleted.",
                PrimaryButtonText = "Reset",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var dialogResult = await confirmationDialog.ShowAsync();
            if (dialogResult != ContentDialogResult.Primary)
            {
                return;
            }

            StopStepCapture();
            SettingsService.ResetAllSettingsToDefaults();
            InitializeSettings();
            RegisterInitialBinding();
            InAppToastService.Show("All settings were reset to defaults.", InAppToastSeverity.Success);
        }

        private void ListenStep1Button_Click(object sender, RoutedEventArgs e) => StartStepCapture(0);
        private void ListenStep2Button_Click(object sender, RoutedEventArgs e) => StartStepCapture(1);
        private void ListenStep3Button_Click(object sender, RoutedEventArgs e) => StartStepCapture(2);
        private void ListenStep4Button_Click(object sender, RoutedEventArgs e) => StartStepCapture(3);
        private void ListenStep5Button_Click(object sender, RoutedEventArgs e) => StartStepCapture(4);

        private void ClearStep1Button_Click(object sender, RoutedEventArgs e) => ClearStep(0);
        private void ClearStep2Button_Click(object sender, RoutedEventArgs e) => ClearStep(1);
        private void ClearStep3Button_Click(object sender, RoutedEventArgs e) => ClearStep(2);
        private void ClearStep4Button_Click(object sender, RoutedEventArgs e) => ClearStep(3);
        private void ClearStep5Button_Click(object sender, RoutedEventArgs e) => ClearStep(4);

        private void StartStepCapture(int stepIndex)
        {
            StartStepCapture(stepIndex, HotkeyCaptureKind.Screenshot);
        }

        private void StartStepCapture(int stepIndex, HotkeyCaptureKind kind)
        {
            if (kind == HotkeyCaptureKind.Screenshot && !_hasValidSaveFolder)
            {
                const string blockedMessage = "Set a save location before changing hotkeys.";
                BindingStatusText.Text = blockedMessage;
                AddMessage(blockedMessage);
                return;
            }

            _hotkeyCaptureKind = kind;
            _isCaptureMode = true;
            _activeCaptureStepIndex = stepIndex;
            ListeningInfoBar.Title = kind == HotkeyCaptureKind.LiveDraw
                ? $"Live Draw — listening for step {stepIndex + 1}"
                : $"Listening for step {stepIndex + 1}";
            ListeningInfoBar.Message = "Press a non-modifier key. Esc cancels.";
            ListeningInfoBar.IsOpen = true;
            UpdateFeatureAvailability();
            UpdateLiveDrawFeatureAvailability();
        }

        private void StopStepCapture()
        {
            _isCaptureMode = false;
            _activeCaptureStepIndex = null;
            ListeningInfoBar.IsOpen = false;
            _hotkeyCaptureKind = HotkeyCaptureKind.Screenshot;
            UpdateFeatureAvailability();
            UpdateLiveDrawFeatureAvailability();
        }

        private void ClearStep(int stepIndex)
        {
            _editorSequence[stepIndex] = null;
            for (var i = stepIndex + 1; i < MaxSequenceLength; i++)
            {
                if (_editorSequence[i] is null)
                {
                    continue;
                }

                _editorSequence[i - 1] = _editorSequence[i];
                _editorSequence[i] = null;
            }

            UpdateStepTexts();
            UpdateCapturePreview();
            UpdateFeatureAvailability();
        }

        private static bool IsLikelyClaimedByAnotherHotkey(HotkeyBinding binding)
        {
            if (binding.Sequence.Length != 1)
            {
                return false;
            }

            if (RegisterHotKey(nint.Zero, ProbeHotkeyId, binding.Modifiers, binding.Sequence[0]))
            {
                UnregisterHotKey(nint.Zero, ProbeHotkeyId);
                return false;
            }

            return true;
        }

        private static bool IsModifierKey(uint virtualKey)
        {
            return virtualKey is VkShift or VkControl or VkMenu or VkLwin or VkRwin;
        }

        private void HandleCaptureKeyEvent(uint message, uint virtualKey)
        {
            if (!_activeCaptureStepIndex.HasValue)
            {
                return;
            }

            if (message is WmKeydown or WmSyskeydown)
            {
                if (virtualKey == VkEscape)
                {
                    var escapeCaptureKind = _hotkeyCaptureKind;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        StopStepCapture();
                        if (escapeCaptureKind == HotkeyCaptureKind.Screenshot)
                        {
                            BindingStatusText.Text = "Capture canceled.";
                        }
                        else
                        {
                            LiveDrawBindingStatusText.Text = "Capture canceled.";
                        }
                    });
                    return;
                }

                if (IsModifierKey(virtualKey))
                {
                    return;
                }

                var stepIndex = _activeCaptureStepIndex.Value;
                var stepCaptureKind = _hotkeyCaptureKind;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (stepCaptureKind == HotkeyCaptureKind.Screenshot)
                    {
                        _editorSequence[stepIndex] = virtualKey;
                        StopStepCapture();
                        UpdateStepTexts();
                        UpdateCapturePreview();
                        BindingStatusText.Text = $"Step {stepIndex + 1} set to {HotkeyVisualMapper.GetKeyDisplayName(virtualKey)}.";
                    }
                    else
                    {
                        HandleLiveDrawHotkeySequenceKey(virtualKey, stepIndex);
                    }
                });
                return;
            }
        }

        private void UpdateCapturePreview()
        {
            var sequence = BuildEditorSequence();
            if (sequence.Count == 0)
            {
                CapturePreviewPanel.Visibility = Visibility.Collapsed;
                return;
            }

            CapturePreviewPanel.Visibility = Visibility.Visible;
            var keyNames = sequence.Select(HotkeyVisualMapper.GetKeyDisplayName).ToArray();
            var display = HotkeyVisualMapper.BuildBindingDisplay(_editorModifiers, keyNames);
            CapturePreviewChordStrip.SetChord(
                _editorModifiers,
                sequence,
                HotkeyChordAppearance.Default,
                $"Preview: {display}");
        }

        private static uint GetCurrentModifiers()
        {
            uint modifiers = 0;

            if (IsKeyDown(VkControl))
            {
                modifiers |= ModControl;
            }

            if (IsKeyDown(VkMenu))
            {
                modifiers |= ModAlt;
            }

            if (IsKeyDown(VkShift))
            {
                modifiers |= ModShift;
            }

            if (IsKeyDown(VkLwin) || IsKeyDown(VkRwin))
            {
                modifiers |= ModWin;
            }

            return modifiers;
        }

        private nint KeyboardHookCallback(int nCode, nuint wParam, nint lParam)
        {
            if (nCode >= 0)
            {
                var message = (uint)wParam;
                var keyData = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);

                if (_isCaptureMode)
                {
                    HandleCaptureKeyEvent(message, keyData.VkCode);
                }
            }

            return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
        }

        private void HandleRuntimeKeyEvent(uint message, uint virtualKey, HotkeyBinding binding)
        {
            if (binding.Sequence.Length == 0)
            {
                return;
            }

            if (message is WmKeydown or WmSyskeydown)
            {
                HandleRuntimeKeyDown(virtualKey, binding);
                return;
            }

            if (message is WmKeyup or WmSyskeyup)
            {
                HandleRuntimeKeyUp(virtualKey, binding);
            }
        }

        private void HandleRuntimeKeyDown(uint virtualKey, HotkeyBinding binding)
        {
            if (IsModifierKey(virtualKey))
            {
                return;
            }

            if (!ModifiersMatch(binding.Modifiers))
            {
                ResetRuntimeSequenceState();
                return;
            }

            var expectedKey = binding.Sequence[Math.Min(_sequenceMatchIndex, binding.Sequence.Length - 1)];
            if (_runtimePressedKey == virtualKey)
            {
                return;
            }

            if (_runtimePressedKey is null && virtualKey == expectedKey)
            {
                _runtimePressedKey = virtualKey;
                return;
            }

            ResetRuntimeSequenceState();
        }

        private void HandleRuntimeKeyUp(uint virtualKey, HotkeyBinding binding)
        {
            if (IsModifierKey(virtualKey))
            {
                if (!ModifiersMatch(binding.Modifiers))
                {
                    ResetRuntimeSequenceState();
                }
                return;
            }

            if (_runtimePressedKey is null || _runtimePressedKey.Value != virtualKey)
            {
                return;
            }

            if (!ModifiersMatch(binding.Modifiers))
            {
                ResetRuntimeSequenceState();
                return;
            }

            var now = Environment.TickCount64;
            if (_sequenceMatchIndex > 0 && now - _lastMatchedStepAt > SequenceStepTimeoutMilliseconds)
            {
                ResetRuntimeSequenceState();
                return;
            }

            var expectedKey = binding.Sequence[_sequenceMatchIndex];
            if (virtualKey != expectedKey)
            {
                ResetRuntimeSequenceState();
                return;
            }

            _runtimePressedKey = null;
            _sequenceMatchIndex++;
            _lastMatchedStepAt = now;

            if (_sequenceMatchIndex < binding.Sequence.Length)
            {
                return;
            }

            ResetRuntimeSequenceState();
            DispatcherQueue.TryEnqueue(OnHotkeyPressed);
        }

        private void ResetRuntimeSequenceState()
        {
            _sequenceMatchIndex = 0;
            _runtimePressedKey = null;
            _lastMatchedStepAt = 0;
        }

        private static bool ModifiersMatch(uint requiredModifiers)
        {
            var currentModifiers = GetCurrentModifiers();
            var ctrlDown = (currentModifiers & ModControl) != 0;
            var altDown = (currentModifiers & ModAlt) != 0;
            var shiftDown = (currentModifiers & ModShift) != 0;
            var winDown = (currentModifiers & ModWin) != 0;

            var requiresCtrl = (requiredModifiers & ModControl) != 0;
            var requiresAlt = (requiredModifiers & ModAlt) != 0;
            var requiresShift = (requiredModifiers & ModShift) != 0;
            var requiresWin = (requiredModifiers & ModWin) != 0;

            return ctrlDown == requiresCtrl &&
                   altDown == requiresAlt &&
                   shiftDown == requiresShift &&
                   winDown == requiresWin;
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private void OnHotkeyPressed()
        {
            var binding = _currentBinding?.Display ?? "Unknown";
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            AddMessage($"Capture hotkey ({binding}) pressed at {timestamp}");
        }

        private void AddMessage(string message)
        {
            InAppToastService.Show(message);
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (_isKeyboardHookInstalled)
            {
                UnhookWindowsHookEx(_keyboardHookHandle);
                _isKeyboardHookInstalled = false;
            }
        }

        private readonly record struct HotkeyBinding(uint Modifiers, uint[] Sequence, string Display);

        private delegate nint KeyboardHookProc(int nCode, nuint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(nint hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint SetWindowsHookEx(int idHook, KeyboardHookProc lpfn, nint hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern nint CallNextHookEx(nint hhk, int nCode, nuint wParam, nint lParam);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(nint hWnd, int id);

        [StructLayout(LayoutKind.Sequential)]
        private struct KbdLlHookStruct
        {
            public uint VkCode;
            public uint ScanCode;
            public uint Flags;
            public uint Time;
            public nuint DwExtraInfo;
        }
    }
}
