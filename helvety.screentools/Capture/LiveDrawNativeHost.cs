using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics;

namespace helvety.screentools.Capture
{
    /// <summary>
    /// Fullscreen Win32 <c>WS_EX_LAYERED</c> + <c>SetLayeredWindowAttributes(..., LWA_COLORKEY)</c> with a
    /// <see cref="DesktopWindowXamlSource"/> island. WinUI does not honor <c>LWA_COLORKEY</c> on its DirectComposition
    /// surface (see microsoft-ui-xaml #8469), so we paint a GDI chroma-key fill in <c>WM_ERASEBKGND</c> and key out
    /// <b>magenta</b>; the XAML root stays transparent so ink is drawn on top while the desktop shows through.
    /// </summary>
    internal sealed class LiveDrawNativeHost : IDisposable
    {
        private const int GwlExstyle = -20;
        private const int GwlStyle = -16;
        private const uint WsPopup = 0x80000000;
        private const uint WsBorder = 0x00800000;
        private const uint WsCaption = 0x00C00000;
        private const uint WsThickframe = 0x00040000;
        private const uint WsDlgframe = 0x00400000;
        private const uint WsMaximize = 0x01000000;
        private const uint WsMinimize = 0x20000000;
        private const uint WsSysmenu = 0x00080000;
        private const uint WsClipchildren = 0x02000000;
        private const uint WsClipsiblings = 0x04000000;

        private const uint WsExLayered = 0x00080000;
        private const uint WsExTopmost = 0x00000008;
        private const uint WsExToolwindow = 0x00000080;

        private const uint LwaColorkey = 0x00000001;
        /// <summary>COLORREF magenta (GDI + layered color key); must not be used for ink strokes.</summary>
        private const uint ColorkeyMagenta = 0x00FF00FF;

        private const uint WmErasebkgnd = 0x0014;
        private const uint WmKeydown = 0x0100;
        private const uint VkEscape = 0x1B;

        private const int HwndTopmost = -1;
        private const uint SwpNomove = 0x0002;
        private const uint SwpNosize = 0x0001;
        private const uint SwpNoactivate = 0x0010;
        private const uint SwpShowwindow = 0x0040;
        private const uint SwpFramechanged = 0x0020;
        private const int SwShow = 5;

        private const int DwmwaTransitionsForceDisabled = 3;
        private static readonly object ClassRegistrationLock = new();
        private static bool _classRegistered;
        private static WndProcDelegate? _pinnedWndProc;

        /// <summary>At most one Live Draw host is shown; used from static <see cref="WindowProc"/> for Escape.</summary>
        private static LiveDrawNativeHost? s_activeHostForEscape;

        private nint _hwnd;
        private DesktopWindowXamlSource? _xamlSource;
        private LiveDrawOverlayContent? _content;
        private DispatcherQueue? _uiDispatcherQueue;
        private Action? _requestExitLiveDraw;
        private bool _disposed;

        internal void ShowAndHost(RectInt32 bounds, LiveDrawOverlayContent content, Action requestExitLiveDraw)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(requestExitLiveDraw);

            if (_hwnd != nint.Zero)
            {
                throw new InvalidOperationException("Host already shown.");
            }

            EnsureWindowClassRegistered();
            _content = content;
            _requestExitLiveDraw = requestExitLiveDraw;
            _uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();

            var hInstance = GetModuleHandle(nint.Zero);
            _hwnd = CreateWindowExW(
                WsExLayered | WsExTopmost | WsExToolwindow,
                GetClassName(),
                "Live Draw",
                WsPopup | WsClipchildren | WsClipsiblings,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                nint.Zero,
                nint.Zero,
                hInstance,
                nint.Zero);

            if (_hwnd == nint.Zero)
            {
                throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
            }

            var exStyle = (uint)NativeInterop.GetWindowLongPtr(_hwnd, GwlExstyle).ToInt64();
            if ((exStyle & WsExLayered) == 0)
            {
                _ = NativeInterop.SetWindowLongPtr(_hwnd, GwlExstyle, new nint(exStyle | WsExLayered));
            }

            if (!SetLayeredWindowAttributes(_hwnd, ColorkeyMagenta, 0, LwaColorkey))
            {
                throw new InvalidOperationException($"SetLayeredWindowAttributes failed: {Marshal.GetLastWin32Error()}");
            }

            StripDecorations();

            var disable = 1;
            _ = DwmSetWindowAttribute(_hwnd, DwmwaTransitionsForceDisabled, ref disable, sizeof(int));

            _xamlSource = new DesktopWindowXamlSource();
            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            _xamlSource.Initialize(windowId);
            _xamlSource.Content = _content;

            _ = NativeInterop.SetWindowPos(
                _hwnd,
                (nint)HwndTopmost,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                SwpShowwindow | SwpFramechanged);
            _ = ShowWindow(_hwnd, SwShow);
            _ = NativeInterop.SetForegroundWindow(_hwnd);
            _ = NativeInterop.SetFocus(_hwnd);
            _content.AttachHost(this);
            s_activeHostForEscape = this;
            _ = InvalidateRect(_hwnd, nint.Zero, true);
        }

        /// <summary>Brings the host to the foreground and gives it keyboard focus so Esc / XAML keys work.</summary>
        internal void EnsureFocusedForKeyboard()
        {
            if (_hwnd == nint.Zero)
            {
                return;
            }

            _ = NativeInterop.SetForegroundWindow(_hwnd);
            _ = NativeInterop.SetFocus(_hwnd);
        }

        internal void Close()
        {
            if (s_activeHostForEscape == this)
            {
                s_activeHostForEscape = null;
            }

            _content?.DetachHost();
            _requestExitLiveDraw = null;
            _uiDispatcherQueue = null;

            if (_xamlSource is not null)
            {
                _xamlSource.Content = null;
                _xamlSource = null;
            }

            _content = null;

            if (_hwnd != nint.Zero)
            {
                _ = DestroyWindow(_hwnd);
                _hwnd = nint.Zero;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Close();
        }

        private void StripDecorations()
        {
            if (_hwnd == nint.Zero)
            {
                return;
            }

            var style = (uint)NativeInterop.GetWindowLongPtr(_hwnd, GwlStyle).ToInt64();
            var mask = WsCaption | WsThickframe | WsBorder | WsDlgframe | WsSysmenu | WsMinimize | WsMaximize;
            var borderless = style & ~mask;
            if (borderless != style)
            {
                _ = NativeInterop.SetWindowLongPtr(_hwnd, GwlStyle, new nint((long)borderless));
            }

            _ = NativeInterop.SetWindowPos(_hwnd, (nint)HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate | SwpFramechanged);
        }

        private static string GetClassName() => "HelvetyScreenTools.LiveDrawNativeHost";

        private static void EnsureWindowClassRegistered()
        {
            lock (ClassRegistrationLock)
            {
                if (_classRegistered)
                {
                    return;
                }

                _pinnedWndProc = WindowProc;
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    style = 0,
                    lpfnWndProc = _pinnedWndProc,
                    cbClsExtra = 0,
                    cbWndExtra = 0,
                    hInstance = GetModuleHandle(nint.Zero),
                    hIcon = nint.Zero,
                    hCursor = nint.Zero,
                    hbrBackground = nint.Zero,
                    lpszMenuName = null,
                    lpszClassName = GetClassName(),
                    hIconSm = nint.Zero
                };

                var atom = RegisterClassExW(ref wc);
                if (atom == 0)
                {
                    throw new InvalidOperationException($"RegisterClassExW failed: {Marshal.GetLastWin32Error()}");
                }

                _classRegistered = true;
            }
        }

        private static nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
        {
            if (msg == WmErasebkgnd)
            {
                if (GetClientRect(hWnd, out var rect))
                {
                    var brush = CreateSolidBrush((int)ColorkeyMagenta);
                    if (brush != nint.Zero)
                    {
                        _ = FillRect(wParam, ref rect, brush);
                        _ = DeleteObject(brush);
                    }
                }

                return 1;
            }

            if (msg == WmKeydown && (uint)(nint)wParam == VkEscape)
            {
                s_activeHostForEscape?.QueueExitFromEscapeKey();
                return 0;
            }

            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        private void QueueExitFromEscapeKey()
        {
            var dq = _uiDispatcherQueue;
            var exit = _requestExitLiveDraw;
            if (dq is null || exit is null)
            {
                return;
            }

            _ = dq.TryEnqueue(() => exit());
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(nint hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int FillRect(nint hDC, ref RECT lprc, nint hbr);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern nint CreateSolidBrush(int crColor);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(nint hObject);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEXW
        {
            public uint cbSize;
            public uint style;
            public WndProcDelegate lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public nint hInstance;
            public nint hIcon;
            public nint hCursor;
            public nint hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public nint hIconSm;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern nint GetModuleHandle(nint lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowExW(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            nint hWndParent,
            nint hMenu,
            nint hInstance,
            nint lpParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(nint hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(nint hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InvalidateRect(nint hWnd, nint lpRect, bool bErase);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
    }
}
