using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace helvety.screentools.Capture
{
    internal sealed class HotkeyService : IDisposable
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
        private const int VkShift = 0x10;
        private const int VkControl = 0x11;
        private const int VkMenu = 0x12;
        private const int VkLwin = 0x5B;
        private const int VkRwin = 0x5C;
        private const int SequenceStepTimeoutMilliseconds = 700;

        private nint _keyboardHookHandle;
        private KeyboardHookProc? _keyboardHookProc;
        private bool _isKeyboardHookInstalled;
        private bool _hasValidSaveFolder;
        private HotkeyBinding? _captureBinding;
        private HotkeyBinding? _liveDrawBinding;

        private HotkeySequenceRuntimeState _captureState;
        private HotkeySequenceRuntimeState _liveDrawState;

        public event Action<HotkeySessionKind, string>? HotkeyPressed;

        public void Start()
        {
            if (_isKeyboardHookInstalled)
            {
                return;
            }

            _keyboardHookProc = KeyboardHookCallback;
            _keyboardHookHandle = SetWindowsHookEx(WhKeyboardLl, _keyboardHookProc, nint.Zero, 0);
            _isKeyboardHookInstalled = _keyboardHookHandle != nint.Zero;
            SettingsService.SettingsChanged += SettingsService_SettingsChanged;
            ReloadConfiguration();
        }

        public void Dispose()
        {
            SettingsService.SettingsChanged -= SettingsService_SettingsChanged;
            if (_isKeyboardHookInstalled)
            {
                UnhookWindowsHookEx(_keyboardHookHandle);
                _isKeyboardHookInstalled = false;
            }
        }

        private void SettingsService_SettingsChanged()
        {
            ReloadConfiguration();
        }

        private void ReloadConfiguration()
        {
            _hasValidSaveFolder = SettingsService.TryGetEffectiveSaveFolderPath(out _);

            if (!SettingsService.TryGetEffectiveHotkey(out var captureHotkey) || !_hasValidSaveFolder)
            {
                _captureBinding = null;
            }
            else
            {
                _captureBinding = new HotkeyBinding(captureHotkey.Modifiers, CopySequence(captureHotkey.Sequence), captureHotkey.Display);
            }

            _liveDrawBinding = SettingsService.TryGetEffectiveLiveDrawHotkey(out var liveHotkey)
                ? new HotkeyBinding(liveHotkey.Modifiers, CopySequence(liveHotkey.Sequence), liveHotkey.Display)
                : null;

            ResetCaptureSequenceState();
            ResetLiveDrawSequenceState();
        }

        private nint KeyboardHookCallback(int nCode, nuint wParam, nint lParam)
        {
            if (nCode >= 0)
            {
                var message = (uint)wParam;
                var keyData = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);

                if (message is WmKeydown or WmSyskeydown)
                {
                    if (_captureBinding is not null)
                    {
                        HandleRuntimeKeyDown(keyData.VkCode, _captureBinding.Value, ref _captureState);
                    }

                    if (_liveDrawBinding is not null)
                    {
                        HandleRuntimeKeyDown(keyData.VkCode, _liveDrawBinding.Value, ref _liveDrawState);
                    }
                }
                else if (message is WmKeyup or WmSyskeyup)
                {
                    var captureCompleted = false;
                    var liveCompleted = false;

                    if (_captureBinding is not null)
                    {
                        captureCompleted = TryCompleteSequenceStepOnKeyUp(keyData.VkCode, _captureBinding.Value, ref _captureState);
                    }

                    if (_liveDrawBinding is not null)
                    {
                        liveCompleted = TryCompleteSequenceStepOnKeyUp(keyData.VkCode, _liveDrawBinding.Value, ref _liveDrawState);
                    }

                    if (captureCompleted && liveCompleted && BindingsEqual(_captureBinding, _liveDrawBinding))
                    {
                        ResetLiveDrawSequenceState();
                        HotkeyPressed?.Invoke(HotkeySessionKind.Screenshot, _captureBinding!.Value.Display);
                    }
                    else if (captureCompleted)
                    {
                        HotkeyPressed?.Invoke(HotkeySessionKind.Screenshot, _captureBinding!.Value.Display);
                    }
                    else if (liveCompleted)
                    {
                        HotkeyPressed?.Invoke(HotkeySessionKind.LiveDraw, _liveDrawBinding!.Value.Display);
                    }
                }
            }

            return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
        }

        private void HandleRuntimeKeyDown(uint virtualKey, HotkeyBinding binding, ref HotkeySequenceRuntimeState state)
        {
            if (binding.Sequence.Length == 0)
            {
                return;
            }

            if (IsModifierKey(virtualKey))
            {
                return;
            }

            if (!ModifiersMatch(binding.Modifiers))
            {
                ResetSequenceState(ref state);
                return;
            }

            var expectedKey = binding.Sequence[Math.Min(state.SequenceMatchIndex, binding.Sequence.Length - 1)];
            if (state.RuntimePressedKey == virtualKey)
            {
                return;
            }

            if (state.RuntimePressedKey is null && virtualKey == expectedKey)
            {
                state.RuntimePressedKey = virtualKey;
                return;
            }

            ResetSequenceState(ref state);
        }

        private bool TryCompleteSequenceStepOnKeyUp(uint virtualKey, HotkeyBinding binding, ref HotkeySequenceRuntimeState state)
        {
            if (binding.Sequence.Length == 0)
            {
                return false;
            }

            if (IsModifierKey(virtualKey))
            {
                if (!ModifiersMatch(binding.Modifiers))
                {
                    ResetSequenceState(ref state);
                }

                return false;
            }

            if (state.RuntimePressedKey is null || state.RuntimePressedKey.Value != virtualKey)
            {
                return false;
            }

            if (!ModifiersMatch(binding.Modifiers))
            {
                ResetSequenceState(ref state);
                return false;
            }

            var now = Environment.TickCount64;
            if (state.SequenceMatchIndex > 0 && now - state.LastMatchedStepAt > SequenceStepTimeoutMilliseconds)
            {
                ResetSequenceState(ref state);
                return false;
            }

            var expectedKey = binding.Sequence[state.SequenceMatchIndex];
            if (virtualKey != expectedKey)
            {
                ResetSequenceState(ref state);
                return false;
            }

            state.RuntimePressedKey = null;
            state.SequenceMatchIndex++;
            state.LastMatchedStepAt = now;

            if (state.SequenceMatchIndex < binding.Sequence.Length)
            {
                return false;
            }

            ResetSequenceState(ref state);
            return true;
        }

        private static bool BindingsEqual(HotkeyBinding? a, HotkeyBinding? b)
        {
            if (a is null || b is null)
            {
                return false;
            }

            if (a.Value.Modifiers != b.Value.Modifiers || a.Value.Sequence.Length != b.Value.Sequence.Length)
            {
                return false;
            }

            for (var i = 0; i < a.Value.Sequence.Length; i++)
            {
                if (a.Value.Sequence[i] != b.Value.Sequence[i])
                {
                    return false;
                }
            }

            return true;
        }

        private void ResetCaptureSequenceState()
        {
            ResetSequenceState(ref _captureState);
        }

        private void ResetLiveDrawSequenceState()
        {
            ResetSequenceState(ref _liveDrawState);
        }

        private static void ResetSequenceState(ref HotkeySequenceRuntimeState state)
        {
            state.SequenceMatchIndex = 0;
            state.RuntimePressedKey = null;
            state.LastMatchedStepAt = 0;
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

        private static bool IsModifierKey(uint virtualKey)
        {
            return virtualKey is VkShift or VkControl or VkMenu or VkLwin or VkRwin;
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private struct HotkeySequenceRuntimeState
        {
            internal int SequenceMatchIndex;
            internal uint? RuntimePressedKey;
            internal long LastMatchedStepAt;
        }

        private static uint[] CopySequence(IReadOnlyList<uint> sequence)
        {
            var copy = new uint[sequence.Count];
            for (var i = 0; i < sequence.Count; i++)
            {
                copy[i] = sequence[i];
            }

            return copy;
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
