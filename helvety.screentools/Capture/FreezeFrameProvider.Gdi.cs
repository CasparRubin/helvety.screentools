using System;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace helvety.screentools.Capture
{
    internal interface IFreezeFrameProvider
    {
        FreezeFrame CaptureVirtualScreen();
    }

    internal sealed class GdiFreezeFrameProvider : IFreezeFrameProvider
    {
        private const int SmXvirtualscreen = 76;
        private const int SmYvirtualscreen = 77;
        private const int SmCxvirtualscreen = 78;
        private const int SmCyvirtualscreen = 79;
        private const int Srccopy = 0x00CC0020;
        private const uint DibRgbColors = 0;
        private const int BiRgb = 0;

        public FreezeFrame CaptureVirtualScreen()
        {
            var left = GetSystemMetrics(SmXvirtualscreen);
            var top = GetSystemMetrics(SmYvirtualscreen);
            var width = GetSystemMetrics(SmCxvirtualscreen);
            var height = GetSystemMetrics(SmCyvirtualscreen);
            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("Unable to determine virtual screen bounds.");
            }

            var screenDc = GetDC(nint.Zero);
            if (screenDc == nint.Zero)
            {
                throw new InvalidOperationException("Failed to get desktop device context.");
            }

            nint memoryDc = nint.Zero;
            nint bitmap = nint.Zero;
            nint oldObject = nint.Zero;

            try
            {
                memoryDc = CreateCompatibleDC(screenDc);
                if (memoryDc == nint.Zero)
                {
                    throw new InvalidOperationException("Failed to create compatible device context.");
                }

                bitmap = CreateCompatibleBitmap(screenDc, width, height);
                if (bitmap == nint.Zero)
                {
                    throw new InvalidOperationException("Failed to create compatible bitmap.");
                }

                oldObject = SelectObject(memoryDc, bitmap);
                if (oldObject == nint.Zero)
                {
                    throw new InvalidOperationException("Failed to select bitmap into device context.");
                }

                if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, left, top, Srccopy))
                {
                    throw new InvalidOperationException("BitBlt failed while capturing virtual screen.");
                }

                var stride = width * 4;
                var pixelData = new byte[stride * height];

                var bitmapInfo = new BitmapInfo
                {
                    BmiHeader = new BitmapInfoHeader
                    {
                        BiSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                        BiWidth = width,
                        BiHeight = -height,
                        BiPlanes = 1,
                        BiBitCount = 32,
                        BiCompression = BiRgb,
                        BiSizeImage = (uint)pixelData.Length
                    }
                };

                var copiedRows = GetDIBits(
                    memoryDc,
                    bitmap,
                    0,
                    (uint)height,
                    pixelData,
                    ref bitmapInfo,
                    DibRgbColors);

                if (copiedRows == 0)
                {
                    throw new InvalidOperationException("Failed to copy bitmap data from captured frame.");
                }

                return new FreezeFrame(new RectInt32(left, top, width, height), stride, pixelData);
            }
            finally
            {
                if (memoryDc != nint.Zero && oldObject != nint.Zero)
                {
                    SelectObject(memoryDc, oldObject);
                }

                if (bitmap != nint.Zero)
                {
                    DeleteObject(bitmap);
                }

                if (memoryDc != nint.Zero)
                {
                    DeleteDC(memoryDc);
                }

                if (screenDc != nint.Zero)
                {
                    ReleaseDC(nint.Zero, screenDc);
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern nint GetDC(nint hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(nint hWnd, nint hDc);

        [DllImport("gdi32.dll")]
        private static extern nint CreateCompatibleDC(nint hDc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(nint hDc);

        [DllImport("gdi32.dll")]
        private static extern nint CreateCompatibleBitmap(nint hDc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern nint SelectObject(nint hDc, nint hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(nint hObject);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BitBlt(
            nint hdcDest,
            int nXDest,
            int nYDest,
            int nWidth,
            int nHeight,
            nint hdcSrc,
            int nXSrc,
            int nYSrc,
            int dwRop);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int GetDIBits(
            nint hdc,
            nint hbm,
            uint start,
            uint cLines,
            [Out] byte[] lpvBits,
            ref BitmapInfo lpbmi,
            uint usage);

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint BiSize;
            public int BiWidth;
            public int BiHeight;
            public ushort BiPlanes;
            public ushort BiBitCount;
            public int BiCompression;
            public uint BiSizeImage;
            public int BiXPelsPerMeter;
            public int BiYPelsPerMeter;
            public uint BiClrUsed;
            public uint BiClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public BitmapInfoHeader BmiHeader;
            public uint BmiColors;
        }
    }

    internal sealed record FreezeFrame(RectInt32 VirtualBounds, int Stride, byte[] PixelData);
}
