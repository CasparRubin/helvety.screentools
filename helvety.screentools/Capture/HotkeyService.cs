using System;
using System.Linq;
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
        private HotkeyBinding? _currentBinding;
        private int _sequenceMatchIndex;
        private uint? _runtimePressedKey;
        private long _lastMatchedStepAt;

        public event Action<string>? HotkeyPressed;

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
            if (!SettingsService.TryGetEffectiveHotkey(out var hotkey) || !_hasValidSaveFolder)
            {
                _currentBinding = null;
                ResetRuntimeSequenceState();
                return;
            }

            _currentBinding = new HotkeyBinding(hotkey.Modifiers, hotkey.Sequence.ToArray(), hotkey.Display);
            ResetRuntimeSequenceState();
        }

        private nint KeyboardHookCallback(int nCode, nuint wParam, nint lParam)
        {
            if (nCode >= 0 && _currentBinding is not null && _hasValidSaveFolder)
            {
                var message = (uint)wParam;
                var keyData = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                HandleRuntimeKeyEvent(message, keyData.VkCode, _currentBinding.Value);
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
            HotkeyPressed?.Invoke(binding.Display);
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
