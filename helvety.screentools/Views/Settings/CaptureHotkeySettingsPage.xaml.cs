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

namespace helvety.screentools.Views.Settings
{
    public sealed partial class CaptureHotkeySettingsPage : Page
    {
        private const int ProbeHotkeyId = 40001;
        private const string DefaultHotkeySequenceInstructionText = "Click Listen for a step, then press any non-modifier key.";
        private const string CaptureBlockedInstructionText = "Choose a save location first.";
        private static readonly int MaxSequenceLength = SettingsService.MaxHotkeySequenceLength;

        private string _saveFolderPath = string.Empty;
        private bool _hasValidSaveFolder;
        private HotkeyBinding? _currentBinding;
        private HotkeyBinding? _startupBinding;
        private bool _isCaptureMode;
        private readonly uint?[] _editorSequence = new uint?[MaxSequenceLength];
        private uint _editorModifiers;
        private bool _isUpdatingCaptureToggle;
        private HotkeyListenController? _listenController;
        private bool _isUpdatingScreenshotQualitySelection;
        private bool _isUpdatingOverlayInstructionSelection;

        public CaptureHotkeySettingsPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;

            _listenController = new HotkeyListenController(DispatcherQueue);
            _listenController.NonModifierKeyCaptured += ListenController_NonModifierKeyCaptured;
            _listenController.EscapePressed += ListenController_EscapePressed;

            InitializeCaptureModuleToggle();
            InitializeSettings();
            RegisterInitialBinding();
            InitializeScreenshotQualitySelection();
            InitializeOverlayInstructionSelection();

            SettingsService.SaveFolderPathChanged += SettingsService_SaveFolderPathChanged;
            SettingsService.SettingsChanged += SettingsService_SettingsChanged;
            Unloaded += CaptureHotkeySettingsPage_Unloaded;
        }

        private void InitializeCaptureModuleToggle()
        {
            var enabled = SettingsService.Load().CaptureHotkeyEnabled;
            _isUpdatingCaptureToggle = true;
            try
            {
                CaptureModuleToggle.IsOn = enabled;
                CaptureDetailsPanel.IsEnabled = enabled;
            }
            finally
            {
                _isUpdatingCaptureToggle = false;
            }
        }

        private void CaptureModuleToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingCaptureToggle)
            {
                return;
            }

            var on = CaptureModuleToggle.IsOn;
            SettingsService.SaveCaptureHotkeyEnabled(on);
            CaptureDetailsPanel.IsEnabled = on;
        }

        private void SettingsService_SettingsChanged()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                InitializeCaptureModuleToggle();
                InitializeSettings();
                RegisterInitialBinding();
                InitializeScreenshotQualitySelection();
                InitializeOverlayInstructionSelection();
            });
        }

        private void ListenController_NonModifierKeyCaptured(int stepIndex, uint virtualKey)
        {
            _editorSequence[stepIndex] = virtualKey;
            StopStepCapture();
            UpdateStepTexts();
            UpdateCapturePreview();
            BindingStatusText.Text = $"Step {stepIndex + 1} set to {HotkeyVisualMapper.GetKeyDisplayName(virtualKey)}.";
        }

        private void ListenController_EscapePressed()
        {
            StopStepCapture();
            BindingStatusText.Text = "Capture canceled.";
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            RefreshSaveFolderState();
            InitializeScreenshotQualitySelection();
            InitializeOverlayInstructionSelection();
        }

        private void CaptureHotkeySettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            SettingsService.SaveFolderPathChanged -= SettingsService_SaveFolderPathChanged;
            SettingsService.SettingsChanged -= SettingsService_SettingsChanged;

            if (_listenController is not null)
            {
                _listenController.NonModifierKeyCaptured -= ListenController_NonModifierKeyCaptured;
                _listenController.EscapePressed -= ListenController_EscapePressed;
                _listenController.Dispose();
                _listenController = null;
            }

            Unloaded -= CaptureHotkeySettingsPage_Unloaded;
        }

        private void SettingsService_SaveFolderPathChanged()
        {
            DispatcherQueue.TryEnqueue(RefreshSaveFolderState);
        }

        private void InitializeSettings()
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

            _startupBinding = SettingsService.TryGetEffectiveHotkey(out var effectiveHotkey)
                ? new HotkeyBinding(effectiveHotkey.Modifiers, effectiveHotkey.Sequence.ToArray(), effectiveHotkey.Display)
                : null;

            RefreshSaveFolderState();
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
            }
            else if (SettingsService.TryValidateWritableFolder(_saveFolderPath, out var validationError))
            {
                _hasValidSaveFolder = true;
                SettingsService.SaveFolderPath(_saveFolderPath);
                SaveFolderText.Text = $"Save Folder: {_saveFolderPath}";
                SaveFolderStatusText.Text = string.Empty;
                RemoveSaveFolderButton.IsEnabled = true;
            }
            else
            {
                _hasValidSaveFolder = false;
                SaveFolderText.Text = $"Save Folder: {_saveFolderPath}";
                SaveFolderStatusText.Text = $"Choose a writable folder ({validationError}).";
                RemoveSaveFolderButton.IsEnabled = true;
            }

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
                if (!SettingsService.TryValidateWritableFolder(candidatePath, out var pickValidationError))
                {
                    SaveFolderStatusText.Text = _hasValidSaveFolder
                        ? $"Folder not writable ({pickValidationError})."
                        : $"Choose a writable folder ({pickValidationError}).";
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

        private void InitializeOverlayInstructionSelection()
        {
            var settings = SettingsService.Load();
            _isUpdatingOverlayInstructionSelection = true;
            try
            {
                ShowOverlayInstructionsToggle.IsOn = settings.ShowScreenshotOverlayInstructions;
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

        private void ShowOverlayInstructionsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingOverlayInstructionSelection)
            {
                return;
            }

            SettingsService.SaveShowScreenshotOverlayInstructions(ShowOverlayInstructionsToggle.IsOn);
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
        }

        private void RegisterInitialBinding()
        {
            if (_startupBinding is null)
            {
                _currentBinding = null;
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
            if (_listenController is null || !_listenController.IsInstalled)
            {
                statusMessage = "Keyboard hook failed to install.";
                return false;
            }

            _currentBinding = requestedBinding;
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
            if (!_hasValidSaveFolder)
            {
                const string blockedMessage = "Set a save location before changing hotkeys.";
                BindingStatusText.Text = blockedMessage;
                AddMessage(blockedMessage);
                return;
            }

            if (_listenController is null || !_listenController.IsInstalled)
            {
                BindingStatusText.Text = "Keyboard hook failed to install.";
                return;
            }

            _isCaptureMode = true;
            _listenController.StartListen(stepIndex);
            ListeningInfoBar.Title = $"Listening for step {stepIndex + 1}";
            ListeningInfoBar.Message = "Press a non-modifier key. Esc cancels.";
            ListeningInfoBar.IsOpen = true;
            UpdateFeatureAvailability();
        }

        private void StopStepCapture()
        {
            _isCaptureMode = false;
            _listenController?.StopListen();
            ListeningInfoBar.IsOpen = false;
            UpdateFeatureAvailability();
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

        private void AddMessage(string message)
        {
            InAppToastService.Show(message);
        }

        private readonly record struct HotkeyBinding(uint Modifiers, uint[] Sequence, string Display);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(nint hWnd, int id);
    }
}
