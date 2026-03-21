using helvety.screentools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace helvety.screentools.Views
{
    public sealed partial class SettingsPage
    {
        private HotkeyCaptureKind _hotkeyCaptureKind = HotkeyCaptureKind.Screenshot;
        private readonly uint?[] _liveDrawEditorSequence = new uint?[MaxSequenceLength];
        private uint _liveDrawEditorModifiers;
        private bool _isUpdatingLiveDrawRectangleModifier;

        private enum HotkeyCaptureKind
        {
            Screenshot,
            LiveDraw
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
                LiveDrawCurrentBindingText.Text = "Live Draw: (none)";
                LiveDrawBindingStatusText.Text = "No Live Draw hotkey set.";
                UpdateLiveDrawFeatureAvailability();
                return;
            }

            SetLiveDrawEditorFromBinding(new HotkeyBinding(effective.Modifiers, effective.Sequence.ToArray(), effective.Display));
            LiveDrawCurrentBindingText.Text = $"Live Draw: {effective.Display}";
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
            UpdateLiveDrawCapturePreview();
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
            LiveDrawStep1KeyText.Text = _liveDrawEditorSequence[0].HasValue ? GetKeyDisplayName(_liveDrawEditorSequence[0]!.Value) : "(not set)";
            LiveDrawStep2KeyText.Text = _liveDrawEditorSequence[1].HasValue ? GetKeyDisplayName(_liveDrawEditorSequence[1]!.Value) : "(not set)";
            LiveDrawStep3KeyText.Text = _liveDrawEditorSequence[2].HasValue ? GetKeyDisplayName(_liveDrawEditorSequence[2]!.Value) : "(not set)";
            LiveDrawStep4KeyText.Text = _liveDrawEditorSequence[3].HasValue ? GetKeyDisplayName(_liveDrawEditorSequence[3]!.Value) : "(not set)";
            LiveDrawStep5KeyText.Text = _liveDrawEditorSequence[4].HasValue ? GetKeyDisplayName(_liveDrawEditorSequence[4]!.Value) : "(not set)";
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
            UpdateLiveDrawCapturePreview();
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
            UpdateLiveDrawCapturePreview();
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

        private void UpdateLiveDrawCapturePreview()
        {
            var sequence = BuildLiveDrawEditorSequence();
            if (sequence.Count == 0)
            {
                LiveDrawCapturePreviewText.Text = string.Empty;
                return;
            }

            var keyNames = sequence.Select(GetKeyDisplayName).ToArray();
            LiveDrawCapturePreviewText.Text = $"Preview: {BuildBindingDisplay(_liveDrawEditorModifiers, keyNames)}";
        }

        private void UpdateLiveDrawFeatureAvailability()
        {
            var hasSeq = BuildLiveDrawEditorSequence().Count > 0;
            var dup = TryGetDuplicateHotkeyConflict(out _);
            ApplyLiveDrawHotkeyButton.IsEnabled = !_isCaptureMode && hasSeq && !dup;
            UseDefaultLiveDrawHotkeyButton.IsEnabled = !_isCaptureMode;
            RemoveLiveDrawHotkeyButton.IsEnabled = !_isCaptureMode;
            LiveDrawCaptureInstructionText.Text = DefaultCaptureInstructionText;
        }

        private bool TryGetDuplicateHotkeyConflict(out string message)
        {
            message = string.Empty;
            var liveSeq = BuildLiveDrawEditorSequence();
            if (liveSeq.Count == 0)
            {
                return false;
            }

            var liveDisplay = BuildBindingDisplay(_liveDrawEditorModifiers, liveSeq.Select(GetKeyDisplayName).ToArray());
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

        private void LiveDrawListenStep1Button_Click(object sender, RoutedEventArgs e) => StartStepCapture(0, HotkeyCaptureKind.LiveDraw);
        private void LiveDrawListenStep2Button_Click(object sender, RoutedEventArgs e) => StartStepCapture(1, HotkeyCaptureKind.LiveDraw);
        private void LiveDrawListenStep3Button_Click(object sender, RoutedEventArgs e) => StartStepCapture(2, HotkeyCaptureKind.LiveDraw);
        private void LiveDrawListenStep4Button_Click(object sender, RoutedEventArgs e) => StartStepCapture(3, HotkeyCaptureKind.LiveDraw);
        private void LiveDrawListenStep5Button_Click(object sender, RoutedEventArgs e) => StartStepCapture(4, HotkeyCaptureKind.LiveDraw);

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
            UpdateLiveDrawCapturePreview();
            UpdateLiveDrawFeatureAvailability();
        }

        private void ApplyLiveDrawHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (TryApplyLiveDrawEditorBinding(out var statusMessage))
            {
                LiveDrawBindingStatusText.Text = statusMessage;
                if (!string.IsNullOrWhiteSpace(statusMessage))
                {
                    AddMessage(statusMessage);
                }

                return;
            }

            LiveDrawBindingStatusText.Text = statusMessage;
            AddMessage(statusMessage);
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
                    AddMessage(statusMessage);
                }

                return;
            }

            LiveDrawBindingStatusText.Text = statusMessage;
            AddMessage(statusMessage);
        }

        private void RemoveLiveDrawHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            StopStepCapture();
            SettingsService.ClearLiveDrawHotkey();
            ResetLiveDrawEditor();
            LiveDrawCurrentBindingText.Text = "Live Draw: (none)";
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

            var display = BuildBindingDisplay(_liveDrawEditorModifiers, sequence.Select(GetKeyDisplayName).ToArray());
            SettingsService.SaveLiveDrawHotkey(_liveDrawEditorModifiers, sequence, display);
            LiveDrawCurrentBindingText.Text = $"Live Draw: {display}";
            statusMessage = string.Empty;
            UpdateLiveDrawFeatureAvailability();
            return true;
        }

        private void HandleLiveDrawCaptureKey(uint virtualKey, int stepIndex)
        {
            _liveDrawEditorSequence[stepIndex] = virtualKey;
            StopStepCapture();
            UpdateLiveDrawStepTexts();
            UpdateLiveDrawCapturePreview();
            LiveDrawBindingStatusText.Text = $"Step {stepIndex + 1} set to {GetKeyDisplayName(virtualKey)}.";
        }
    }
}
