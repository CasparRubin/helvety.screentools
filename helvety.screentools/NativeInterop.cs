using System;
using System.Runtime.InteropServices;

namespace helvety.screentools
{
    internal static class NativeInterop
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(nint hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(nint hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern nint SetFocus(nint hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(
            nint hWnd,
            nint hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        internal static nint GetWindowLongPtr(nint hWnd, int nIndex)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, nIndex)
                : new nint(GetWindowLong32(hWnd, nIndex));
        }

        internal static nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                : new nint(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }
    }
}
