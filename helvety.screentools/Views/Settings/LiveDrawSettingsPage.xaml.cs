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
        private bool _isUpdatingLiveDrawShapeModifiers;
        private bool _isUpdatingLiveDrawToggle;
        private int _liveDrawHotkeyEditorSuppress;
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
            ApplyLiveDrawModuleState(enabled, persistSetting: false);
        }

        private void LiveDrawModuleToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingLiveDrawToggle)
            {
                return;
            }

            ApplyLiveDrawModuleState(LiveDrawModuleToggle.IsOn, persistSetting: true);
        }

        private void ApplyLiveDrawModuleState(bool enabled, bool persistSetting)
        {
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

            if (persistSetting)
            {
                SettingsService.SaveLiveDrawEnabled(enabled);
            }

            RefreshLiveDrawCurrentShortcutVisual();
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
            SetLiveDrawBindingStatus($"Step {stepIndex + 1} set to {HotkeyVisualMapper.GetKeyDisplayName(virtualKey)}.");
            TryAutoSaveLiveDrawHotkey();
        }

        private void ListenController_EscapePressed()
        {
            StopStepCapture();
            SetLiveDrawBindingStatus("Listen canceled.");
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
            InitializeLiveDrawShapeModifierCombos();

            if (SettingsService.TryGetEffectiveHotkey(out var shot) &&
                SettingsService.TryGetEffectiveLiveDrawHotkey(out var live) &&
                SettingsService.HotkeyModifiersAndSequenceEqual(shot, live))
            {
                SetLiveDrawBindingStatus("Live Draw matches capture hotkey; change one so both stay distinct.");
            }

            RegisterInitialLiveDrawBinding();
        }

        private void RegisterInitialLiveDrawBinding()
        {
            if (!SettingsService.TryGetEffectiveLiveDrawHotkey(out var effective))
            {
                ResetLiveDrawEditor();
                RefreshLiveDrawCurrentShortcutVisual(null);
                SetLiveDrawBindingStatus("No Live Draw hotkey set.");
                UpdateLiveDrawFeatureAvailability();
                return;
            }

            SetLiveDrawEditorFromBinding(new HotkeyBinding(effective.Modifiers, effective.Sequence.ToArray(), effective.Display));
            RefreshLiveDrawCurrentShortcutVisual(new HotkeyBinding(effective.Modifiers, effective.Sequence.ToArray(), effective.Display));
            SetLiveDrawBindingStatus(string.Empty);
            UpdateLiveDrawFeatureAvailability();
        }

        private void InitializeLiveDrawShapeModifierCombos()
        {
            _isUpdatingLiveDrawShapeModifiers = true;
            try
            {
                var mods = SettingsService.LoadLiveDrawShapeModifiers();
                SetShapeModifierComboIndex(LiveDrawShapeRectangleComboBox, mods.Rectangle);
                SetShapeModifierComboIndex(LiveDrawShapeArrowComboBox, mods.Arrow);
                SetShapeModifierComboIndex(LiveDrawShapeStraightLineComboBox, mods.StraightLine);
                SetShapeModifierComboIndex(LiveDrawShapeCircleRightComboBox, mods.CircleRight);
                SetShapeModifierComboIndex(LiveDrawShapeEllipseRightComboBox, mods.EllipseRight);
            }
            finally
            {
                _isUpdatingLiveDrawShapeModifiers = false;
            }
        }

        private static void SetShapeModifierComboIndex(ComboBox box, LiveDrawRectangleModifier modifier)
        {
            box.SelectedIndex = modifier switch
            {
                LiveDrawRectangleModifier.None => 0,
                LiveDrawRectangleModifier.Shift => 1,
                LiveDrawRectangleModifier.Control => 2,
                LiveDrawRectangleModifier.Alt => 3,
                LiveDrawRectangleModifier.Win => 4,
                _ => 0
            };
        }

        private static LiveDrawRectangleModifier GetShapeModifierFromCombo(ComboBox box)
        {
            if (box.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)
            {
                return LiveDrawRectangleModifier.None;
            }

            return tag switch
            {
                "None" => LiveDrawRectangleModifier.None,
                "Shift" => LiveDrawRectangleModifier.Shift,
                "Control" => LiveDrawRectangleModifier.Control,
                "Alt" => LiveDrawRectangleModifier.Alt,
                "Win" => LiveDrawRectangleModifier.Win,
                _ => LiveDrawRectangleModifier.None
            };
        }

        private void LiveDrawShapeModifierComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingLiveDrawShapeModifiers)
            {
                return;
            }

            var next = new LiveDrawShapeModifiers(
                GetShapeModifierFromCombo(LiveDrawShapeRectangleComboBox),
                GetShapeModifierFromCombo(LiveDrawShapeArrowComboBox),
                GetShapeModifierFromCombo(LiveDrawShapeStraightLineComboBox),
                GetShapeModifierFromCombo(LiveDrawShapeCircleRightComboBox),
                GetShapeModifierFromCombo(LiveDrawShapeEllipseRightComboBox));

            if (SettingsService.TrySaveLiveDrawShapeModifiers(next, out var errorMessage))
            {
                return;
            }

            InAppToastService.Show(errorMessage);
            InitializeLiveDrawShapeModifierCombos();
        }

        private void LiveDrawModifierCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _liveDrawEditorModifiers = BuildLiveDrawModifiersFromEditor();
            UpdateLiveDrawFeatureAvailability();
            TryAutoSaveLiveDrawHotkey();
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
            _liveDrawHotkeyEditorSuppress++;
            try
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
            }
            finally
            {
                _liveDrawHotkeyEditorSuppress--;
            }
        }

        private void ResetLiveDrawEditor()
        {
            _liveDrawHotkeyEditorSuppress++;
            try
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
            }
            finally
            {
                _liveDrawHotkeyEditorSuppress--;
            }
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

        private void UpdateLiveDrawFeatureAvailability()
        {
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
                SetLiveDrawBindingStatus("Keyboard hook failed to install.");
                return;
            }

            _isCaptureMode = true;
            _listenController.StartListen(stepIndex);
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
            UpdateLiveDrawFeatureAvailability();
            TryAutoSaveLiveDrawHotkey();
        }

        private void TryAutoSaveLiveDrawHotkey()
        {
            if (_liveDrawHotkeyEditorSuppress > 0 || _isCaptureMode)
            {
                return;
            }

            var sequence = BuildLiveDrawEditorSequence();
            if (sequence.Count == 0)
            {
                SettingsService.ClearLiveDrawHotkey();
                RefreshLiveDrawCurrentShortcutVisual(null);
                SetLiveDrawBindingStatus("No Live Draw hotkey set.");
                UpdateLiveDrawFeatureAvailability();
                return;
            }

            if (TryGetDuplicateHotkeyConflict(out var dupMsg))
            {
                SetLiveDrawBindingStatus(dupMsg);
                return;
            }

            if (TryApplyLiveDrawEditorBinding(out var statusMessage))
            {
                SetLiveDrawBindingStatus(statusMessage);
                return;
            }

            SetLiveDrawBindingStatus(statusMessage);
            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                InAppToastService.Show(statusMessage);
            }
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
            RefreshLiveDrawCurrentShortcutVisual(new HotkeyBinding(_liveDrawEditorModifiers, sequence.ToArray(), display));
            statusMessage = string.Empty;
            UpdateLiveDrawFeatureAvailability();
            return true;
        }

        private void SetLiveDrawBindingStatus(string message)
        {
            LiveDrawBindingStatusText.Text = message;
            LiveDrawBindingStatusText.Visibility = string.IsNullOrWhiteSpace(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void RefreshLiveDrawCurrentShortcutVisual(HotkeyBinding? binding = null)
        {
            if (binding is null)
            {
                if (!SettingsService.TryGetEffectiveLiveDrawHotkey(out var effective))
                {
                    LiveDrawCurrentChordStrip.SetEmpty("(none)", "Live Draw: (none)");
                    return;
                }

                binding = new HotkeyBinding(effective.Modifiers, effective.Sequence.ToArray(), effective.Display);
            }

            var value = binding.Value;
            LiveDrawCurrentChordStrip.SetChord(
                value.Modifiers,
                value.Sequence,
                GetCurrentShortcutAppearance(),
                $"Live Draw: {value.Display}");
        }

        private HotkeyChordAppearance GetCurrentShortcutAppearance()
        {
            return LiveDrawModuleToggle.IsOn
                ? HotkeyChordAppearance.Accent
                : HotkeyChordAppearance.Disabled;
        }

        private readonly record struct HotkeyBinding(uint Modifiers, uint[] Sequence, string Display);
    }
}
