using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace helvety.screenshots.Views
{
    public sealed partial class SettingsPage : Page
    {
        private const int WhKeyboardLl = 13;
        private const uint WmKeydown = 0x0100;
        private const uint WmKeyup = 0x0101;
        private const uint WmSyskeydown = 0x0104;
        private const uint WmSyskeyup = 0x0105;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModWin = 0x0008;
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
        private const string DefaultCaptureInstructionText = "Click Listen for a step, then press any non-modifier key.";
        private const string CaptureBlockedInstructionText = "Choose a save location first.";

        private readonly ObservableCollection<string> _messages = new();
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

        public SettingsPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;

            MessageListView.ItemsSource = _messages;
            InitializeSettings();
            InitializeHotkeyInfrastructure();
            RegisterInitialBinding();

            SettingsService.SaveFolderPathChanged += SettingsService_SaveFolderPathChanged;
            App.CaptureStatusPublished += App_CaptureStatusPublished;
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
            App.CaptureStatusPublished -= App_CaptureStatusPublished;
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

        private void App_CaptureStatusPublished(string message)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                AddMessage($"{message} ({timestamp})");
            });
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
            var defaultPath = SettingsService.GetDefaultDesktopFolderPath();
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
            ApplyHotkeyButton.IsEnabled = _hasValidSaveFolder && !_isCaptureMode && BuildEditorSequence().Count > 0;
            UseDefaultHotkeyButton.IsEnabled = _hasValidSaveFolder && !_isCaptureMode;
            RemoveHotkeyButton.IsEnabled = !_isCaptureMode && _currentBinding is not null;
            CaptureInstructionText.Text = _hasValidSaveFolder
                ? DefaultCaptureInstructionText
                : CaptureBlockedInstructionText;

            if (_hasValidSaveFolder && BindingStatusText.Text.StartsWith("Save location needed", StringComparison.Ordinal))
            {
                BindingStatusText.Text = string.Empty;
                return;
            }

            if (!_hasValidSaveFolder)
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
                CurrentBindingText.Text = "Current Binding: (none)";
                BindingStatusText.Text = "No hotkey set.";
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
            Step1KeyText.Text = _editorSequence[0].HasValue ? GetKeyDisplayName(_editorSequence[0]!.Value) : "(not set)";
            Step2KeyText.Text = _editorSequence[1].HasValue ? GetKeyDisplayName(_editorSequence[1]!.Value) : "(not set)";
            Step3KeyText.Text = _editorSequence[2].HasValue ? GetKeyDisplayName(_editorSequence[2]!.Value) : "(not set)";
            Step4KeyText.Text = _editorSequence[3].HasValue ? GetKeyDisplayName(_editorSequence[3]!.Value) : "(not set)";
            Step5KeyText.Text = _editorSequence[4].HasValue ? GetKeyDisplayName(_editorSequence[4]!.Value) : "(not set)";
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
            CurrentBindingText.Text = $"Current Binding: {requestedBinding.Display}";
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
            CurrentBindingText.Text = "Current Binding: (none)";
            BindingStatusText.Text = "No hotkey set.";
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

            var display = BuildBindingDisplay(_editorModifiers, sequence.Select(GetKeyDisplayName).ToArray());
            var binding = new HotkeyBinding(_editorModifiers, sequence.ToArray(), display);
            return TryApplyBinding(binding, out statusMessage);
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

            _isCaptureMode = true;
            _activeCaptureStepIndex = stepIndex;
            ListeningInfoBar.Title = $"Listening for step {stepIndex + 1}";
            ListeningInfoBar.Message = "Press a non-modifier key. Esc cancels.";
            ListeningInfoBar.IsOpen = true;
            UpdateFeatureAvailability();
        }

        private void StopStepCapture()
        {
            _isCaptureMode = false;
            _activeCaptureStepIndex = null;
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
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        StopStepCapture();
                        BindingStatusText.Text = "Capture canceled.";
                    });
                    return;
                }

                if (IsModifierKey(virtualKey))
                {
                    return;
                }

                var stepIndex = _activeCaptureStepIndex.Value;
                DispatcherQueue.TryEnqueue(() =>
                {
                    _editorSequence[stepIndex] = virtualKey;
                    StopStepCapture();
                    UpdateStepTexts();
                    UpdateCapturePreview();
                    BindingStatusText.Text = $"Step {stepIndex + 1} set to {GetKeyDisplayName(virtualKey)}.";
                });
                return;
            }
        }

        private void UpdateCapturePreview()
        {
            var sequence = BuildEditorSequence();
            if (sequence.Count == 0)
            {
                CapturePreviewText.Text = string.Empty;
                return;
            }

            var keyNames = sequence.Select(GetKeyDisplayName).ToArray();
            CapturePreviewText.Text = $"Preview: {BuildBindingDisplay(_editorModifiers, keyNames)}";
        }

        private static string BuildModifierPreview(uint modifiers)
        {
            var parts = new Collection<string>();

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

        private static string BuildBindingDisplay(uint modifiers, IReadOnlyList<string> keyNames)
        {
            var modifiersPart = BuildModifierPreview(modifiers);
            var sequencePart = string.Join('+', keyNames);
            return string.IsNullOrEmpty(modifiersPart) ? sequencePart : $"{modifiersPart}+{sequencePart}";
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

        private static string GetKeyDisplayName(uint virtualKey)
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
                return $"F{virtualKey - 0x6F}";
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
                _ => $"VK_{virtualKey:X2}"
            };
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
            AddMessage($"Hotkey {binding} pressed at {timestamp}");
        }

        private void AddMessage(string message)
        {
            _messages.Insert(0, message);
            if (_messages.Count > 100)
            {
                _messages.RemoveAt(_messages.Count - 1);
            }
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
