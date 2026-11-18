using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace helvety.screentools.Capture
{
    /// <summary>
    /// Global hotkey sequences via a low-level keyboard hook. The hook lives on its own message-loop thread so a busy
    /// UI thread can never delay system-wide typing or exceed LowLevelHooksTimeout (which silently removes the hook).
    /// </summary>
    internal sealed class HotkeyService : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private const uint WmQuit = 0x0012;
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
        private const int VkEscape = 0x1B;
        private const int SequenceStepTimeoutMilliseconds = 700;

        private nint _keyboardHookHandle;
        private KeyboardHookProc? _keyboardHookProc;
        private Thread? _hookThread;
        private uint _hookThreadId;
        private HotkeyBinding? _captureBinding;
        private HotkeyBinding? _liveDrawBinding;
        private readonly object _stateLock = new();

        private HotkeySequenceRuntimeState _captureState;
        private HotkeySequenceRuntimeState _liveDrawState;

        /// <summary>Raised on the hook thread; subscribers must marshal to the UI thread.</summary>
        public event Action<HotkeySessionKind, string>? HotkeyPressed;

        public void Start()
        {
            if (_hookThread is not null)
            {
                return;
            }

            SettingsService.SettingsChanged += SettingsService_SettingsChanged;
            ReloadConfiguration();

            using var hookInstalled = new ManualResetEventSlim();
            _hookThread = new Thread(() => RunHookThread(hookInstalled))
            {
                IsBackground = true,
                Name = "Global hotkey hook"
            };
            _hookThread.Start();
            hookInstalled.Wait();
        }

        public void Dispose()
        {
            SettingsService.SettingsChanged -= SettingsService_SettingsChanged;
            if (_hookThread is null)
            {
                return;
            }

            _ = PostThreadMessage(_hookThreadId, WmQuit, 0, 0);
            _ = _hookThread.Join(TimeSpan.FromSeconds(1));
            _hookThread = null;
        }

        private void RunHookThread(ManualResetEventSlim hookInstalled)
        {
            _hookThreadId = GetCurrentThreadId();
            _keyboardHookProc = KeyboardHookCallback;
            _keyboardHookHandle = SetWindowsHookEx(WhKeyboardLl, _keyboardHookProc, GetModuleHandle(null), 0);
            var error = Marshal.GetLastWin32Error();
            hookInstalled.Set();
            if (_keyboardHookHandle == nint.Zero)
            {
                AppLog.Error($"Global hotkey hook could not be installed (Win32 error {error}).");
                return;
            }

            // Low-level hook callbacks are delivered while this thread pumps messages.
            while (GetMessage(out var message, nint.Zero, 0, 0) > 0)
            {
                _ = TranslateMessage(in message);
                _ = DispatchMessage(in message);
            }

            _ = UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = nint.Zero;
        }

        private void SettingsService_SettingsChanged()
        {
            ReloadConfiguration();
        }

        private void ReloadConfiguration()
        {
            var app = SettingsService.Load();
            if (!app.GlobalHotkeyListenersEnabled)
            {
                lock (_stateLock)
                {
                    _captureBinding = null;
                    _liveDrawBinding = null;
                    ResetCaptureSequenceState();
                    ResetLiveDrawSequenceState();
                }

                return;
            }

            var hasValidSaveFolder = SettingsService.TryGetEffectiveSaveFolderPath(out _);
            HotkeyBinding? captureBinding = (!app.CaptureHotkeyEnabled ||
                                             !SettingsService.TryGetEffectiveHotkey(out var captureHotkey) ||
                                             !hasValidSaveFolder)
                ? null
                : new HotkeyBinding(captureHotkey.Modifiers, CopySequence(captureHotkey.Sequence), captureHotkey.Display);
            HotkeyBinding? liveDrawBinding = app.LiveDrawEnabled &&
                                             SettingsService.TryGetEffectiveLiveDrawHotkey(out var liveHotkey)
                ? new HotkeyBinding(liveHotkey.Modifiers, CopySequence(liveHotkey.Sequence), liveHotkey.Display)
                : null;

            lock (_stateLock)
            {
                _captureBinding = captureBinding;
                _liveDrawBinding = liveDrawBinding;
                ResetCaptureSequenceState();
                ResetLiveDrawSequenceState();
            }
        }

        private nint KeyboardHookCallback(int nCode, nuint wParam, nint lParam)
        {
            HotkeySessionKind? triggeredSession = null;
            string? triggeredDisplay = null;

            if (nCode >= 0)
            {
                var message = (uint)wParam;
                var keyData = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);

                lock (_stateLock)
                {
                    if (message is WmKeydown or WmSyskeydown)
                    {
                        if (keyData.VkCode == VkEscape && ActiveOverlayCancelService.TryCancelFromEscape())
                        {
                            return (nint)1;
                        }

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
                        var captureBinding = _captureBinding;
                        var liveDrawBinding = _liveDrawBinding;

                        if (captureBinding is not null)
                        {
                            captureCompleted = TryCompleteSequenceStepOnKeyUp(keyData.VkCode, captureBinding.Value, ref _captureState);
                        }

                        if (liveDrawBinding is not null)
                        {
                            liveCompleted = TryCompleteSequenceStepOnKeyUp(keyData.VkCode, liveDrawBinding.Value, ref _liveDrawState);
                        }

                        if (captureCompleted && liveCompleted && BindingsEqual(captureBinding, liveDrawBinding))
                        {
                            ResetLiveDrawSequenceState();
                            if (ActiveOverlayCancelService.TryCancelFromRepeatHotkey(HotkeySessionKind.Screenshot) ||
                                ActiveOverlayCancelService.TryCancelFromRepeatHotkey(HotkeySessionKind.LiveDraw))
                            {
                                return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
                            }

                            triggeredSession = HotkeySessionKind.Screenshot;
                            triggeredDisplay = captureBinding?.Display;
                        }
                        else if (captureCompleted)
                        {
                            if (!ActiveOverlayCancelService.TryCancelFromRepeatHotkey(HotkeySessionKind.Screenshot))
                            {
                                triggeredSession = HotkeySessionKind.Screenshot;
                                triggeredDisplay = captureBinding?.Display;
                            }
                        }
                        else if (liveCompleted)
                        {
                            if (!ActiveOverlayCancelService.TryCancelFromRepeatHotkey(HotkeySessionKind.LiveDraw))
                            {
                                triggeredSession = HotkeySessionKind.LiveDraw;
                                triggeredDisplay = liveDrawBinding?.Display;
                            }
                        }
                    }
                }
            }

            if (triggeredSession.HasValue && !string.IsNullOrWhiteSpace(triggeredDisplay))
            {
                HotkeyPressed?.Invoke(triggeredSession.Value, triggeredDisplay);
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

            return a.Value.Modifiers == b.Value.Modifiers &&
                   a.Value.Sequence.AsSpan().SequenceEqual(b.Value.Sequence);
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

        private static uint[] CopySequence(IReadOnlyList<uint> sequence) => [.. sequence];

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

        [DllImport("user32.dll")]
        private static extern int GetMessage(out Msg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(in Msg lpMsg);

        [DllImport("user32.dll")]
        private static extern nint DispatchMessage(in Msg lpMsg);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostThreadMessage(uint idThread, uint msg, nuint wParam, nint lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern nint GetModuleHandle(string? lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct Msg
        {
            public nint Hwnd;
            public uint Message;
            public nuint WParam;
            public nint LParam;
            public uint Time;
            public int PtX;
            public int PtY;
            public uint LPrivate;
        }

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
