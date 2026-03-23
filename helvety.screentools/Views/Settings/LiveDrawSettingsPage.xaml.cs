using helvety.screentools;
using static helvety.screentools.HotkeyVisualMapper;
using helvety.screentools.Views.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace helvety.screentools.Views.Settings
{
    public sealed partial class LiveDrawSettingsPage : Page
    {
        private const string DefaultHotkeySequenceInstructionText = "Click Listen for a step, then press any non-modifier key.";
        private static readonly int MaxSequenceLength = SettingsService.MaxHotkeySequenceLength;

        private readonly uint?[] _liveDrawEditorSequence = new uint?[MaxSequenceLength];
        private uint _liveDrawEditorModifiers;
        private bool _isUpdatingLiveDrawRectangleModifier;
        private bool _isUpdatingLiveDrawToggle;
        private bool _isCaptureMode;
        private HotkeyListenController? _listenController;

        public LiveDrawSettingsPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;

            _listenController = new HotkeyListenController(DispatcherQueue);
            _listenController.NonModifierKeyCaptured += ListenController_NonModifierKeyCaptured;
            _listenController.EscapePressed += ListenController_EscapePressed;

            InitializeLiveDrawModuleToggle();
            InitializeLiveDrawHotkeyUi();
            SettingsService.SettingsChanged += SettingsService_SettingsChanged;
            Unloaded += LiveDrawSettingsPage_Unloaded;
        }

        private void InitializeLiveDrawModuleToggle()
        {
            var enabled = SettingsService.Load().LiveDrawEnabled;
            _isUpdatingLiveDrawToggle = true;
            try
            {
                LiveDrawModuleToggle.IsOn = enabled;
                LiveDrawDetailsPanel.IsEnabled = enabled;
            }
            finally
            {
                _isUpdatingLiveDrawToggle = false;
            }
        }

        private void LiveDrawModuleToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingLiveDrawToggle)
            {
                return;
            }

            SettingsService.SaveLiveDrawEnabled(LiveDrawModuleToggle.IsOn);
            LiveDrawDetailsPanel.IsEnabled = LiveDrawModuleToggle.IsOn;
        }

        private void SettingsService_SettingsChanged()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                InitializeLiveDrawModuleToggle();
                InitializeLiveDrawHotkeyUi();
            });
        }

        private void ListenController_NonModifierKeyCaptured(int stepIndex, uint virtualKey)
        {
            _liveDrawEditorSequence[stepIndex] = virtualKey;
            StopStepCapture();
            UpdateLiveDrawStepTexts();
            UpdateLiveDrawHotkeyPreview();
            LiveDrawBindingStatusText.Text = $"Step {stepIndex + 1} set to {HotkeyVisualMapper.GetKeyDisplayName(virtualKey)}.";
        }

        private void ListenController_EscapePressed()
        {
            StopStepCapture();
            LiveDrawBindingStatusText.Text = "Listen canceled.";
        }

        private void LiveDrawSettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            SettingsService.SettingsChanged -= SettingsService_SettingsChanged;
            if (_listenController is not null)
            {
                _listenController.NonModifierKeyCaptured -= ListenController_NonModifierKeyCaptured;
                _listenController.EscapePressed -= ListenController_EscapePressed;
                _listenController.Dispose();
                _listenController = null;
            }

            Unloaded -= LiveDrawSettingsPage_Unloaded;
        }

        private void InitializeLiveDrawHotkeyUi()
        {
            _isUpdatingLiveDrawRectangleModifier = true;
            try
            {
                var mod = SettingsService.LoadLiveDrawRectangleModifier();
                LiveDrawRectangleModifierComboBox.SelectedIndex = mod switch
                {
                    LiveDrawRectangleModifier.Control => 1,
                    LiveDrawRectangleModifier.Alt => 2,
                    LiveDrawRectangleModifier.Win => 3,
                    _ => 0
                };
            }
            finally
            {
                _isUpdatingLiveDrawRectangleModifier = false;
            }

            if (SettingsService.TryGetEffectiveHotkey(out var shot) &&
                SettingsService.TryGetEffectiveLiveDrawHotkey(out var live) &&
                SettingsService.HotkeyModifiersAndSequenceEqual(shot, live))
            {
                LiveDrawBindingStatusText.Text = "Live Draw matches capture hotkey; change one so both stay distinct.";
            }

            RegisterInitialLiveDrawBinding();
        }

        private void RegisterInitialLiveDrawBinding()
        {
            if (!SettingsService.TryGetEffectiveLiveDrawHotkey(out var effective))
            {
                ResetLiveDrawEditor();
                LiveDrawCurrentChordStrip.SetEmpty("(none)", "Live Draw: (none)");
                LiveDrawBindingStatusText.Text = "No Live Draw hotkey set.";
                UpdateLiveDrawFeatureAvailability();
                return;
            }

            SetLiveDrawEditorFromBinding(new HotkeyBinding(effective.Modifiers, effective.Sequence.ToArray(), effective.Display));
            LiveDrawCurrentChordStrip.SetChord(
                effective.Modifiers,
                effective.Sequence,
                HotkeyChordAppearance.Accent,
                $"Live Draw: {effective.Display}");
            LiveDrawBindingStatusText.Text = string.Empty;
            UpdateLiveDrawFeatureAvailability();
        }

        private void LiveDrawRectangleModifierComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingLiveDrawRectangleModifier || LiveDrawRectangleModifierComboBox.SelectedItem is not ComboBoxItem item ||
                item.Tag is not string tag)
            {
                return;
            }

            var modifier = tag switch
            {
                "Control" => LiveDrawRectangleModifier.Control,
                "Alt" => LiveDrawRectangleModifier.Alt,
                "Win" => LiveDrawRectangleModifier.Win,
                _ => LiveDrawRectangleModifier.Shift
            };
            SettingsService.SaveLiveDrawRectangleModifier(modifier);
        }

        private void LiveDrawModifierCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _liveDrawEditorModifiers = BuildLiveDrawModifiersFromEditor();
            UpdateLiveDrawHotkeyPreview();
            UpdateLiveDrawFeatureAvailability();
        }

        private uint BuildLiveDrawModifiersFromEditor()
        {
            uint modifiers = 0;
            if (LiveDrawCtrlModifierCheckBox.IsChecked == true)
            {
                modifiers |= ModControl;
            }

            if (LiveDrawAltModifierCheckBox.IsChecked == true)
            {
                modifiers |= ModAlt;
            }

            if (LiveDrawShiftModifierCheckBox.IsChecked == true)
            {
                modifiers |= ModShift;
            }

            return modifiers;
        }

        private void UpdateLiveDrawStepTexts()
        {
            LiveDrawStep1KeyChordStrip.SetSingleKey(_liveDrawEditorSequence[0], HotkeyChordAppearance.Default, null);
            LiveDrawStep2KeyChordStrip.SetSingleKey(_liveDrawEditorSequence[1], HotkeyChordAppearance.Default, null);
            LiveDrawStep3KeyChordStrip.SetSingleKey(_liveDrawEditorSequence[2], HotkeyChordAppearance.Default, null);
            LiveDrawStep4KeyChordStrip.SetSingleKey(_liveDrawEditorSequence[3], HotkeyChordAppearance.Default, null);
            LiveDrawStep5KeyChordStrip.SetSingleKey(_liveDrawEditorSequence[4], HotkeyChordAppearance.Default, null);
        }

        private void SetLiveDrawEditorFromBinding(HotkeyBinding binding)
        {
            _liveDrawEditorModifiers = binding.Modifiers & (ModControl | ModAlt | ModShift);
            LiveDrawCtrlModifierCheckBox.IsChecked = (_liveDrawEditorModifiers & ModControl) != 0;
            LiveDrawAltModifierCheckBox.IsChecked = (_liveDrawEditorModifiers & ModAlt) != 0;
            LiveDrawShiftModifierCheckBox.IsChecked = (_liveDrawEditorModifiers & ModShift) != 0;

            for (var i = 0; i < MaxSequenceLength; i++)
            {
                _liveDrawEditorSequence[i] = i < binding.Sequence.Length
                    ? binding.Sequence[i]
                    : null;
            }

            UpdateLiveDrawStepTexts();
            UpdateLiveDrawHotkeyPreview();
        }

        private void ResetLiveDrawEditor()
        {
            _liveDrawEditorModifiers = 0;
            for (var i = 0; i < MaxSequenceLength; i++)
            {
                _liveDrawEditorSequence[i] = null;
            }

            LiveDrawCtrlModifierCheckBox.IsChecked = false;
            LiveDrawAltModifierCheckBox.IsChecked = false;
            LiveDrawShiftModifierCheckBox.IsChecked = false;
            UpdateLiveDrawStepTexts();
            UpdateLiveDrawHotkeyPreview();
        }

        private List<uint> BuildLiveDrawEditorSequence()
        {
            var sequence = new List<uint>(MaxSequenceLength);
            for (var i = 0; i < MaxSequenceLength; i++)
            {
                if (!_liveDrawEditorSequence[i].HasValue)
                {
                    break;
                }

                sequence.Add(_liveDrawEditorSequence[i]!.Value);
            }

            return sequence;
        }

        private void UpdateLiveDrawHotkeyPreview()
        {
            var sequence = BuildLiveDrawEditorSequence();
            if (sequence.Count == 0)
            {
                LiveDrawPreviewPanel.Visibility = Visibility.Collapsed;
                return;
            }

            LiveDrawPreviewPanel.Visibility = Visibility.Visible;
            var keyNames = sequence.Select(HotkeyVisualMapper.GetKeyDisplayName).ToArray();
            var display = HotkeyVisualMapper.BuildBindingDisplay(_liveDrawEditorModifiers, keyNames);
            LiveDrawPreviewChordStrip.SetChord(
                _liveDrawEditorModifiers,
                sequence,
                HotkeyChordAppearance.Default,
                $"Preview: {display}");
        }

        private void UpdateLiveDrawFeatureAvailability()
        {
            var hasSeq = BuildLiveDrawEditorSequence().Count > 0;
            var dup = TryGetDuplicateHotkeyConflict(out _);
            ApplyLiveDrawHotkeyButton.IsEnabled = !_isCaptureMode && hasSeq && !dup;
            UseDefaultLiveDrawHotkeyButton.IsEnabled = !_isCaptureMode;
            RemoveLiveDrawHotkeyButton.IsEnabled = !_isCaptureMode;
            LiveDrawHotkeyInstructionText.Text = DefaultHotkeySequenceInstructionText;
        }

        private bool TryGetDuplicateHotkeyConflict(out string message)
        {
            message = string.Empty;
            var liveSeq = BuildLiveDrawEditorSequence();
            if (liveSeq.Count == 0)
            {
                return false;
            }

            var liveDisplay = HotkeyVisualMapper.BuildBindingDisplay(_liveDrawEditorModifiers, liveSeq.Select(HotkeyVisualMapper.GetKeyDisplayName).ToArray());
            var candidate = new HotkeySettings(_liveDrawEditorModifiers, liveSeq, liveDisplay);

            if (!SettingsService.TryGetEffectiveHotkey(out var shot))
            {
                return false;
            }

            if (SettingsService.HotkeyModifiersAndSequenceEqual(candidate, shot))
            {
                message = "Live Draw hotkey must differ from the capture hotkey.";
                return true;
            }

            return false;
        }

        private void LiveDrawListenStep1Button_Click(object sender, RoutedEventArgs e) => StartStepCapture(0);
        private void LiveDrawListenStep2Button_Click(object sender, RoutedEventArgs e) => StartStepCapture(1);
        private void LiveDrawListenStep3Button_Click(object sender, RoutedEventArgs e) => StartStepCapture(2);
        private void LiveDrawListenStep4Button_Click(object sender, RoutedEventArgs e) => StartStepCapture(3);
        private void LiveDrawListenStep5Button_Click(object sender, RoutedEventArgs e) => StartStepCapture(4);

        private void StartStepCapture(int stepIndex)
        {
            if (_listenController is null || !_listenController.IsInstalled)
            {
                LiveDrawBindingStatusText.Text = "Keyboard hook failed to install.";
                return;
            }

            _isCaptureMode = true;
            _listenController.StartListen(stepIndex, HotkeyListenKind.LiveDraw);
            ListeningInfoBar.Title = $"Live Draw — listening for step {stepIndex + 1}";
            ListeningInfoBar.Message = "Press a non-modifier key. Esc cancels.";
            ListeningInfoBar.IsOpen = true;
            UpdateLiveDrawFeatureAvailability();
        }

        private void StopStepCapture()
        {
            _isCaptureMode = false;
            _listenController?.StopListen();
            ListeningInfoBar.IsOpen = false;
            UpdateLiveDrawFeatureAvailability();
        }

        private void LiveDrawClearStep1Button_Click(object sender, RoutedEventArgs e) => ClearLiveDrawStep(0);
        private void LiveDrawClearStep2Button_Click(object sender, RoutedEventArgs e) => ClearLiveDrawStep(1);
        private void LiveDrawClearStep3Button_Click(object sender, RoutedEventArgs e) => ClearLiveDrawStep(2);
        private void LiveDrawClearStep4Button_Click(object sender, RoutedEventArgs e) => ClearLiveDrawStep(3);
        private void LiveDrawClearStep5Button_Click(object sender, RoutedEventArgs e) => ClearLiveDrawStep(4);

        private void ClearLiveDrawStep(int stepIndex)
        {
            _liveDrawEditorSequence[stepIndex] = null;
            for (var i = stepIndex + 1; i < MaxSequenceLength; i++)
            {
                if (_liveDrawEditorSequence[i] is null)
                {
                    continue;
                }

                _liveDrawEditorSequence[i - 1] = _liveDrawEditorSequence[i];
                _liveDrawEditorSequence[i] = null;
            }

            UpdateLiveDrawStepTexts();
            UpdateLiveDrawHotkeyPreview();
            UpdateLiveDrawFeatureAvailability();
        }

        private void ApplyLiveDrawHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (TryApplyLiveDrawEditorBinding(out var statusMessage))
            {
                LiveDrawBindingStatusText.Text = statusMessage;
                if (!string.IsNullOrWhiteSpace(statusMessage))
                {
                    InAppToastService.Show(statusMessage);
                }

                return;
            }

            LiveDrawBindingStatusText.Text = statusMessage;
            InAppToastService.Show(statusMessage);
        }

        private void UseDefaultLiveDrawHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            var def = SettingsService.GetDefaultLiveDrawHotkey();
            SetLiveDrawEditorFromBinding(new HotkeyBinding(def.Modifiers, def.Sequence.ToArray(), def.Display));
            if (TryApplyLiveDrawEditorBinding(out var statusMessage))
            {
                LiveDrawBindingStatusText.Text = statusMessage;
                if (!string.IsNullOrWhiteSpace(statusMessage))
                {
                    InAppToastService.Show(statusMessage);
                }

                return;
            }

            LiveDrawBindingStatusText.Text = statusMessage;
            InAppToastService.Show(statusMessage);
        }

        private void RemoveLiveDrawHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            StopStepCapture();
            SettingsService.ClearLiveDrawHotkey();
            ResetLiveDrawEditor();
            LiveDrawCurrentChordStrip.SetEmpty("(none)", "Live Draw: (none)");
            LiveDrawBindingStatusText.Text = "No Live Draw hotkey set.";
            UpdateLiveDrawFeatureAvailability();
        }

        private bool TryApplyLiveDrawEditorBinding(out string statusMessage)
        {
            if (TryGetDuplicateHotkeyConflict(out var dupMsg))
            {
                statusMessage = dupMsg;
                return false;
            }

            var sequence = BuildLiveDrawEditorSequence();
            if (sequence.Count == 0)
            {
                statusMessage = "Set at least one sequence key.";
                return false;
            }

            var display = HotkeyVisualMapper.BuildBindingDisplay(_liveDrawEditorModifiers, sequence.Select(HotkeyVisualMapper.GetKeyDisplayName).ToArray());
            SettingsService.SaveLiveDrawHotkey(_liveDrawEditorModifiers, sequence, display);
            LiveDrawCurrentChordStrip.SetChord(
                _liveDrawEditorModifiers,
                sequence,
                HotkeyChordAppearance.Accent,
                $"Live Draw: {display}");
            statusMessage = string.Empty;
            UpdateLiveDrawFeatureAvailability();
            return true;
        }

        private readonly record struct HotkeyBinding(uint Modifiers, uint[] Sequence, string Display);
    }
}
