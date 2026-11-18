using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics;

namespace helvety.screentools.Capture
{
    /// <summary>
    /// Window snapping for the screenshot overlay. Candidate window bounds are captured once, together with the
    /// freeze frame, so snapping matches the frozen pixels and pointer moves never enumerate live windows.
    /// </summary>
    internal static class WindowSnapHitTester
    {
        private const uint GaRoot = 2;
        private const int GwlExstyle = -20;
        private const int WsExToolwindow = 0x00000080;
        private const int DwmaExtendedFrameBounds = 9;
        private const int DwmaCloaked = 14;

        /// <summary>Returns snap candidates in z-order (topmost first).</summary>
        internal static RectInt32[] CaptureSnapTargets(nint excludedWindowHandle)
        {
            var context = new SnapshotContext(excludedWindowHandle);
            var contextHandle = GCHandle.Alloc(context);
            try
            {
                EnumWindows(EnumWindowsCallback, GCHandle.ToIntPtr(contextHandle));
            }
            finally
            {
                contextHandle.Free();
            }

            return [.. context.Targets];
        }

        internal static bool TryGetSnapBoundsAt(RectInt32[] snapTargets, int screenX, int screenY, out RectInt32 bounds)
        {
            foreach (var candidate in snapTargets)
            {
                if (screenX >= candidate.X &&
                    screenY >= candidate.Y &&
                    screenX < candidate.X + candidate.Width &&
                    screenY < candidate.Y + candidate.Height)
                {
                    bounds = candidate;
                    return true;
                }
            }

            bounds = default;
            return false;
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
            if (GCHandle.FromIntPtr(lParam).Target is not SnapshotContext context)
            {
                return false;
            }

            var rootWindow = GetAncestor(hwnd, GaRoot);
            if (rootWindow != nint.Zero &&
                IsCandidateWindow(rootWindow, context.ExcludedWindowHandle) &&
                TryGetWindowBounds(rootWindow, out var candidateBounds) &&
                candidateBounds.Width > 1 &&
                candidateBounds.Height > 1)
            {
                context.Targets.Add(candidateBounds);
            }

            return true;
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

        private sealed class SnapshotContext(nint excludedWindowHandle)
        {
            public nint ExcludedWindowHandle { get; } = excludedWindowHandle;
            public List<RectInt32> Targets { get; } = [];
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
