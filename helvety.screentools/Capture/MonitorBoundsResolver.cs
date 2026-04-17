using System;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace helvety.screentools.Capture
{
    internal static class MonitorBoundsResolver
    {
        private const uint MonitorDefaultToNearest = 0x00000002;

        public static bool TryGetMonitorBoundsAtPoint(int screenX, int screenY, out RectInt32 bounds)
        {
            bounds = default;
            var point = new PointStruct
            {
                X = screenX,
                Y = screenY
            };

            var monitorHandle = MonitorFromPoint(point, MonitorDefaultToNearest);
            if (monitorHandle == nint.Zero)
            {
                return false;
            }

            var monitorInfo = new MonitorInfoStruct
            {
                CbSize = Marshal.SizeOf<MonitorInfoStruct>()
            };
            if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
            {
                return false;
            }

            var width = monitorInfo.RcMonitor.Right - monitorInfo.RcMonitor.Left;
            var height = monitorInfo.RcMonitor.Bottom - monitorInfo.RcMonitor.Top;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            bounds = new RectInt32(
                monitorInfo.RcMonitor.Left,
                monitorInfo.RcMonitor.Top,
                width,
                height);
            return true;
        }

        [DllImport("user32.dll")]
        private static extern nint MonitorFromPoint(PointStruct pt, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfoStruct lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct PointStruct
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RectStruct
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfoStruct
        {
            public int CbSize;
            public RectStruct RcMonitor;
            public RectStruct RcWork;
            public uint DwFlags;
        }
    }
}
