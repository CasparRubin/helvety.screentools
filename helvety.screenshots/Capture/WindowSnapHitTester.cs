using System;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics;

namespace helvety.screenshots.Capture
{
    internal sealed class WindowSnapHitTester
    {
        private const uint GaRoot = 2;
        private const int GwlExstyle = -20;
        private const int WsExToolwindow = 0x00000080;
        private const int DwmaExtendedFrameBounds = 9;
        private const int DwmaCloaked = 14;
        private readonly IBrowserContentSnapProbe _browserContentSnapProbe;

        public WindowSnapHitTester(IBrowserContentSnapProbe? browserContentSnapProbe = null)
        {
            _browserContentSnapProbe = browserContentSnapProbe ?? new NoopBrowserContentSnapProbe();
        }

        public bool TryGetSnapBoundsAt(int screenX, int screenY, nint excludedWindowHandle, out RectInt32 bounds)
        {
            if (_browserContentSnapProbe.TryGetContentBoundsAt(screenX, screenY, out bounds) &&
                bounds.Width > 1 &&
                bounds.Height > 1)
            {
                return true;
            }

            bounds = default;
            var context = new HitTestContext(screenX, screenY, excludedWindowHandle);
            var contextHandle = GCHandle.Alloc(context);
            try
            {
                EnumWindows(EnumWindowsCallback, GCHandle.ToIntPtr(contextHandle));
            }
            finally
            {
                contextHandle.Free();
            }

            if (!context.HasMatch || !context.MatchBounds.HasValue)
            {
                return false;
            }

            bounds = context.MatchBounds.Value;
            return bounds.Width > 1 && bounds.Height > 1;
        }

        private static bool IsCandidateWindow(nint hwnd, nint excludedWindowHandle)
        {
            if (hwnd == nint.Zero || hwnd == excludedWindowHandle)
            {
                return false;
            }

            if (!IsWindowVisible(hwnd))
            {
                return false;
            }

            var exStyle = GetWindowLong(hwnd, GwlExstyle);
            if ((exStyle & WsExToolwindow) != 0)
            {
                return false;
            }

            if (DwmGetWindowAttribute(hwnd, DwmaCloaked, out int cloaked, Marshal.SizeOf<int>()) == 0 && cloaked != 0)
            {
                return false;
            }

            var classNameBuilder = new StringBuilder(256);
            _ = GetClassName(hwnd, classNameBuilder, classNameBuilder.Capacity);
            var className = classNameBuilder.ToString();
            if (string.Equals(className, "Progman", StringComparison.Ordinal) ||
                string.Equals(className, "WorkerW", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private static bool EnumWindowsCallback(nint hwnd, nint lParam)
        {
            var handle = GCHandle.FromIntPtr(lParam);
            if (handle.Target is not HitTestContext context)
            {
                return true;
            }

            var rootWindow = GetAncestor(hwnd, GaRoot);
            if (rootWindow == nint.Zero || !IsCandidateWindow(rootWindow, context.ExcludedWindowHandle))
            {
                return true;
            }

            if (!TryGetWindowBounds(rootWindow, out var candidateBounds))
            {
                return true;
            }

            var isInsideBounds =
                context.ScreenX >= candidateBounds.X &&
                context.ScreenY >= candidateBounds.Y &&
                context.ScreenX < candidateBounds.X + candidateBounds.Width &&
                context.ScreenY < candidateBounds.Y + candidateBounds.Height;

            if (!isInsideBounds)
            {
                return true;
            }

            context.MatchBounds = candidateBounds;
            context.HasMatch = true;
            return false;
        }

        private static bool TryGetWindowBounds(nint hwnd, out RectInt32 bounds)
        {
            bounds = default;
            if (DwmGetWindowAttribute(hwnd, DwmaExtendedFrameBounds, out RectStruct frameRect, Marshal.SizeOf<RectStruct>()) == 0)
            {
                bounds = ToRectInt32(frameRect);
                return bounds.Width > 0 && bounds.Height > 0;
            }

            if (!GetWindowRect(hwnd, out frameRect))
            {
                return false;
            }

            bounds = ToRectInt32(frameRect);
            return bounds.Width > 0 && bounds.Height > 0;
        }

        private static RectInt32 ToRectInt32(RectStruct rect)
        {
            return new RectInt32(
                rect.Left,
                rect.Top,
                Math.Max(0, rect.Right - rect.Left),
                Math.Max(0, rect.Bottom - rect.Top));
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

        [DllImport("user32.dll")]
        private static extern nint GetAncestor(nint hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(nint hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(nint hWnd, int nIndex);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(nint hWnd, out RectStruct lpRect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RectStruct pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

        private sealed class HitTestContext
        {
            public HitTestContext(int screenX, int screenY, nint excludedWindowHandle)
            {
                ScreenX = screenX;
                ScreenY = screenY;
                ExcludedWindowHandle = excludedWindowHandle;
            }

            public int ScreenX { get; }
            public int ScreenY { get; }
            public nint ExcludedWindowHandle { get; }
            public RectInt32? MatchBounds { get; set; }
            public bool HasMatch { get; set; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RectStruct
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
