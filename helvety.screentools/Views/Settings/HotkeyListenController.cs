using Microsoft.UI.Dispatching;
using System;
using System.Runtime.InteropServices;

namespace helvety.screentools.Views.Settings
{
    /// <summary>
    /// Low-level keyboard hook for "Listen" steps in settings hotkey editors. One instance per owning page.
    /// </summary>
    internal sealed class HotkeyListenController : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private const uint WmKeydown = 0x0100;
        private const uint WmSyskeydown = 0x0104;
        private const int VkEscape = 0x1B;
        private const int VkShift = 0x10;
        private const int VkControl = 0x11;
        private const int VkMenu = 0x12;
        private const int VkLwin = 0x5B;
        private const int VkRwin = 0x5C;

        private readonly DispatcherQueue _dispatcher;
        private nint _keyboardHookHandle;
        private KeyboardHookProc _keyboardHookProc;
        private bool _isInstalled;
        private bool _isCaptureMode;
        private int? _activeStepIndex;

        internal HotkeyListenController(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;
            _keyboardHookProc = KeyboardHookCallback;
            // Lazy-install the hook: the settings page is constructed during navigation, and
            // hook installation can be unstable on some systems. We only need the hook once
            // the user clicks "Listen" for a step.
            _keyboardHookHandle = nint.Zero;
            _isInstalled = false;
        }

        internal bool IsInstalled => _isInstalled;

        internal bool StartListen(int stepIndex)
        {
            EnsureHookInstalled();
            if (!_isInstalled)
            {
                _isCaptureMode = false;
                _activeStepIndex = null;
                return false;
            }

            _isCaptureMode = true;
            _activeStepIndex = stepIndex;
            return true;
        }

        internal void StopListen()
        {
            _isCaptureMode = false;
            _activeStepIndex = null;
        }

        private void EnsureHookInstalled()
        {
            if (_isInstalled)
            {
                return;
            }

            // SetWindowsHookEx returns 0 on failure; do not throw.
            _keyboardHookHandle = SetWindowsHookEx(WhKeyboardLl, _keyboardHookProc, nint.Zero, 0);
            _isInstalled = _keyboardHookHandle != nint.Zero;
        }

        internal event Action<int, uint>? NonModifierKeyCaptured;
        internal event Action? EscapePressed;

        private nint KeyboardHookCallback(int nCode, nuint wParam, nint lParam)
        {
            if (nCode >= 0 && _isCaptureMode)
            {
                var message = (uint)wParam;
                var keyData = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                HandleCaptureKeyEvent(message, keyData.VkCode);
            }

            return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
        }

        private void HandleCaptureKeyEvent(uint message, uint virtualKey)
        {
            if (!_activeStepIndex.HasValue)
            {
                return;
            }

            if (message is WmKeydown or WmSyskeydown)
            {
                if (virtualKey == VkEscape)
                {
                    _dispatcher.TryEnqueue(() =>
                    {
                        StopListen();
                        EscapePressed?.Invoke();
                    });
                    return;
                }

                if (IsModifierKey(virtualKey))
                {
                    return;
                }

                var stepIndex = _activeStepIndex.Value;
                _dispatcher.TryEnqueue(() =>
                {
                    StopListen();
                    NonModifierKeyCaptured?.Invoke(stepIndex, virtualKey);
                });
            }
        }

        private static bool IsModifierKey(uint virtualKey)
        {
            return virtualKey is VkShift or VkControl or VkMenu or VkLwin or VkRwin;
        }

        public void Dispose()
        {
            if (_isInstalled)
            {
                UnhookWindowsHookEx(_keyboardHookHandle);
                _isInstalled = false;
            }

            // Keep the callback delegate rooted; nothing else references it after dispose.
        }

        private delegate nint KeyboardHookProc(int nCode, nuint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(nint hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint SetWindowsHookEx(int idHook, KeyboardHookProc lpfn, nint hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern nint CallNextHookEx(nint hhk, int nCode, nuint wParam, nint lParam);

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
