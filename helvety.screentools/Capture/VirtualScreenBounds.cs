using System;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace helvety.screentools.Capture
{
    internal static class VirtualScreenBounds
    {
        private const int SmXvirtualscreen = 76;
        private const int SmYvirtualscreen = 77;
        private const int SmCxvirtualscreen = 78;
        private const int SmCyvirtualscreen = 79;

        internal static RectInt32 Get()
        {
            var left = GetSystemMetrics(SmXvirtualscreen);
            var top = GetSystemMetrics(SmYvirtualscreen);
            var width = GetSystemMetrics(SmCxvirtualscreen);
            var height = GetSystemMetrics(SmCyvirtualscreen);
            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("Unable to determine virtual screen bounds.");
            }

            return new RectInt32(left, top, width, height);
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
    }
}
