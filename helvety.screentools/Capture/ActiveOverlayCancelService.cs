using System;
using Microsoft.UI.Dispatching;

namespace helvety.screentools.Capture
{
    /// <summary>
    /// Routes Escape and repeat-hotkey cancel to the active overlay session (screenshot or Live Draw).
    /// Called from the low-level keyboard hook thread; cancel actions are marshaled to the UI dispatcher.
    /// </summary>
    internal static class ActiveOverlayCancelService
    {
        private static readonly object Lock = new();

        private static HotkeySessionKind? _activeKind;
        private static Action? _cancelAction;
        private static DispatcherQueue? _dispatcherQueue;

        internal static void Register(DispatcherQueue dispatcherQueue, HotkeySessionKind kind, Action cancelOnUiThread)
        {
            lock (Lock)
            {
                _dispatcherQueue = dispatcherQueue;
                _activeKind = kind;
                _cancelAction = cancelOnUiThread;
            }
        }

        internal static void Unregister()
        {
            lock (Lock)
            {
                _activeKind = null;
                _cancelAction = null;
                _dispatcherQueue = null;
            }
        }

        /// <returns>True if an overlay was active and cancel was queued on the UI thread.</returns>
        internal static bool TryCancelFromEscape()
        {
            Action? cancel;
            DispatcherQueue? dq;
            lock (Lock)
            {
                if (_activeKind is null || _cancelAction is null || _dispatcherQueue is null)
                {
                    return false;
                }

                cancel = _cancelAction;
                dq = _dispatcherQueue;
            }

            _ = dq.TryEnqueue(() => cancel());
            return true;
        }

        /// <returns>True if the active session matched <paramref name="kind"/> and cancel was queued.</returns>
        internal static bool TryCancelFromRepeatHotkey(HotkeySessionKind kind)
        {
            Action? cancel;
            DispatcherQueue? dq;
            lock (Lock)
            {
                if (_activeKind != kind || _cancelAction is null || _dispatcherQueue is null)
                {
                    return false;
                }

                cancel = _cancelAction;
                dq = _dispatcherQueue;
            }

            _ = dq.TryEnqueue(() => cancel());
            return true;
        }
    }
}
