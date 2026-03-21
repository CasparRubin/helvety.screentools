using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using helvety.screentools.Editor;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace helvety.screentools.Capture
{
    internal sealed partial class LiveDrawOverlayWindow : Window
    {
        private const int HwndTopmost = -1;
        private const uint SwpNomove = 0x0002;
        private const uint SwpNosize = 0x0001;
        private const uint SwpNoactivate = 0x0010;
        private const uint SwpFramechanged = 0x0020;
        private const int GwlStyle = -16;
        private const nint WsCaption = 0x00C00000;
        private const nint WsThickframe = 0x00040000;
        private const nint WsBorder = 0x00800000;
        private const nint WsDlgframe = 0x00400000;
        private const int VkMenu = 0x12;
        private const int VkLMenu = 0xA4;
        private const int VkRMenu = 0xA5;
        private const int VkShift = 0x10;
        private const int VkLShift = 0xA0;
        private const int VkRShift = 0xA1;
        private const int VkControl = 0x11;
        private const int VkLwin = 0x5B;
        private const int VkRwin = 0x5C;
        private const uint WdaNone = 0x00000000;
        private const uint WdaExcludeFromCapture = 0x00000011;
        private const int DwmwaTransitionsForceDisabled = 3;
        /// <summary>Full virtual-screen BitBlt refresh rate (~10 Hz); GDI is CPU-bound.</summary>
        private const int LiveRefreshIntervalMs = 100;
        private const int SwHide = 0;
        private const int SwShow = 5;
        private const double LiveDrawArrowSizeScale = 2.0;

        private readonly RectInt32 _virtualBounds;
        private readonly nint _windowHandle;
        private readonly SnapBorderChromeController _snapBorderChrome;
        private readonly GdiFreezeFrameProvider _gdiCapture = new();
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _liveRefreshTimer;
        private WriteableBitmap? _backgroundBitmap;
        private bool _underpaintApplied;
        private bool _affinityApplied;
        private volatile bool _captureInFlight;
        private EventHandler<object>? _activationRenderingHandler;
        private TaskCompletionSource<bool> _sessionCompletion = new();
        private LiveDrawTool _activeTool;
        private Point _pointerDownLocal;
        private Polyline? _currentPolyline;
        private PointCollection? _currentPoints;
        private bool _isPointerDown;

        public LiveDrawOverlayWindow(FreezeFrame freezeFrame)
        {
            _virtualBounds = freezeFrame.VirtualBounds;
            InitializeComponent();
            try
            {
                SystemBackdrop = null;
            }
            catch
            {
                // Some hosts may not allow clearing backdrop; ignore.
            }

            ApplyFreezeFrameToBackground(freezeFrame);
            DrawCanvas.Width = _virtualBounds.Width;
            DrawCanvas.Height = _virtualBounds.Height;
            PreviewCanvas.Width = _virtualBounds.Width;
            PreviewCanvas.Height = _virtualBounds.Height;
            BorderCanvas.Width = _virtualBounds.Width;
            BorderCanvas.Height = _virtualBounds.Height;

            _snapBorderChrome = new SnapBorderChromeController(
                RootGrid,
                SnapBorderRectangle,
                SnapBorderChaseRectangle,
                SnapBorderCornerGlowRectangle,
                SnapBorderGlowRectangle,
                BorderCanvas);

            _windowHandle = WindowNative.GetWindowHandle(this);
            ConfigureWindow();
            TryApplyExcludeFromCaptureAffinity();

            // Avoid a visible flash of an uncomposed / wrong-DPI background before the session is prepared.
            _ = ShowWindow(_windowHandle, SwHide);

            RootGrid.Loaded += RootGrid_Loaded;
            Activated += LiveDrawOverlayWindow_Activated;

            _snapBorderChrome.InitializeCompositionAnimations();

            var driftTimer = DispatcherQueue.CreateTimer();
            driftTimer.Interval = TimeSpan.FromMilliseconds(70);
            driftTimer.Tick += (_, _) => _snapBorderChrome.OnColorDriftTick(0.07);
            driftTimer.Start();

            Closed += (_, _) =>
            {
                Activated -= LiveDrawOverlayWindow_Activated;
                driftTimer.Stop();
                _liveRefreshTimer?.Stop();
                if (_activationRenderingHandler is not null)
                {
                    CompositionTarget.Rendering -= _activationRenderingHandler;
                    _activationRenderingHandler = null;
                }

                TryRestoreDwmTransitions();
                if (_affinityApplied && _windowHandle != nint.Zero)
                {
                    _ = SetWindowDisplayAffinity(_windowHandle, WdaNone);
                    _affinityApplied = false;
                }

                if (!_sessionCompletion.Task.IsCompleted)
                {
                    _sessionCompletion.TrySetResult(true);
                }
            };
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            TryApplyBackgroundImageDipSize();
        }

        private void LiveDrawOverlayWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            TryApplyBackgroundImageDipSize();
        }

        /// <summary>
        /// VirtualBounds are physical pixels; XAML layout uses effective (DIP) units so the bitmap is not bilinear-resampled.
        /// </summary>
        private void TryApplyBackgroundImageDipSize()
        {
            var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            if (scale <= 0)
            {
                return;
            }

            BackgroundImage.Width = _virtualBounds.Width / scale;
            BackgroundImage.Height = _virtualBounds.Height / scale;
        }

        /// <summary>
        /// Keeps the HWND hidden until the first desktop bitmap is applied, layout has run, then shows the window.
        /// On Windows 10 build 19041+, always BitBlts here (post-affinity when exclusion is active, or to replace a
        /// coordinator placeholder if exclusion failed). On older Windows the coordinator already supplied pixels, so
        /// no extra capture runs. The periodic live refresh timer starts after show only when capture exclusion is active.
        /// </summary>
        internal async Task PrepareVisibleSessionAsync()
        {
            if (_affinityApplied || LiveDrawPlatformSupport.IsLiveDesktopRefreshSupported)
            {
                var frame = await Task.Run(() => _gdiCapture.CaptureVirtualScreen()).ConfigureAwait(true);
                ApplyFreezeFrameToBackground(frame);
            }

            await WaitForRootGridLoadedAsync().ConfigureAwait(true);
            TryApplyBackgroundImageDipSize();
            await WaitForOneLayoutPassAsync().ConfigureAwait(true);

            _ = ShowWindow(_windowHandle, SwShow);

            if (_affinityApplied)
            {
                var timer = DispatcherQueue.CreateTimer();
                timer.Interval = TimeSpan.FromMilliseconds(LiveRefreshIntervalMs);
                timer.Tick += LiveRefreshTimer_Tick;
                timer.Start();
                _liveRefreshTimer = timer;
            }
        }

        private Task WaitForRootGridLoadedAsync()
        {
            if (RootGrid.IsLoaded)
            {
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource();
            void OnLoaded(object s, RoutedEventArgs e)
            {
                RootGrid.Loaded -= OnLoaded;
                tcs.TrySetResult();
            }

            RootGrid.Loaded += OnLoaded;
            return tcs.Task;
        }

        private Task WaitForOneLayoutPassAsync()
        {
            var tcs = new TaskCompletionSource();
            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            dq.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                RootGrid.UpdateLayout();
                dq.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => tcs.TrySetResult());
            });
            return tcs.Task;
        }

        /// <summary>
        /// Brings the overlay to the foreground: waits for one <see cref="CompositionTarget.Rendering"/> tick, then activates after disabling DWM transitions for this HWND.
        /// If <c>Rendering</c> never fires, a 250 ms <see cref="Microsoft.UI.Dispatching.DispatcherQueueTimer"/> runs the same activation path.
        /// </summary>
        internal Task RunSessionAsync()
        {
            var activationCompleted = false;

            void CompleteActivation()
            {
                if (activationCompleted)
                {
                    return;
                }

                activationCompleted = true;
                TryApplyDwmTransitionsDisabled();
                Activate();
                EnforceBorderlessWindowStyles();
                RootGrid.Focus(FocusState.Programmatic);
                if (_sessionCompletion.Task.IsCompleted)
                {
                    _sessionCompletion = new TaskCompletionSource<bool>();
                }
            }

            void UnsubscribeRendering()
            {
                if (_activationRenderingHandler is not null)
                {
                    CompositionTarget.Rendering -= _activationRenderingHandler;
                    _activationRenderingHandler = null;
                }
            }

            _activationRenderingHandler = (_, _) =>
            {
                UnsubscribeRendering();
                CompleteActivation();
            };
            CompositionTarget.Rendering += _activationRenderingHandler;

            var dq = DispatcherQueue;
            if (dq is not null)
            {
                var fallbackTimer = dq.CreateTimer();
                fallbackTimer.Interval = TimeSpan.FromMilliseconds(250);
                fallbackTimer.Tick += (_, _) =>
                {
                    fallbackTimer.Stop();
                    if (activationCompleted)
                    {
                        return;
                    }

                    UnsubscribeRendering();
                    CompleteActivation();
                };
                fallbackTimer.Start();
            }
            else
            {
                UnsubscribeRendering();
                CompleteActivation();
            }

            return _sessionCompletion.Task;
        }

        private void TryApplyDwmTransitionsDisabled()
        {
            if (_windowHandle == nint.Zero)
            {
                return;
            }

            var disable = 1;
            _ = DwmSetWindowAttribute(_windowHandle, DwmwaTransitionsForceDisabled, ref disable, sizeof(int));
        }

        private void TryRestoreDwmTransitions()
        {
            if (_windowHandle == nint.Zero)
            {
                return;
            }

            var enable = 0;
            _ = DwmSetWindowAttribute(_windowHandle, DwmwaTransitionsForceDisabled, ref enable, sizeof(int));
        }

        private void ConfigureWindow()
        {
            var appWindow = AppWindow.GetFromWindowId(
                Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_windowHandle));
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsAlwaysOnTop = true;
            }

            appWindow.MoveAndResize(_virtualBounds);
            EnforceBorderlessWindowStyles();
            SetWindowPos(_windowHandle, (nint)HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
        }

        private void TryApplyExcludeFromCaptureAffinity()
        {
            if (_windowHandle == nint.Zero || !LiveDrawPlatformSupport.IsLiveDesktopRefreshSupported)
            {
                return;
            }

            if (SetWindowDisplayAffinity(_windowHandle, WdaExcludeFromCapture))
            {
                _affinityApplied = true;
            }
        }

        private void LiveRefreshTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
        {
            if (!_affinityApplied || _isPointerDown || _captureInFlight)
            {
                return;
            }

            _captureInFlight = true;
            _ = Task.Run(() =>
            {
                try
                {
                    var frame = _gdiCapture.CaptureVirtualScreen();
                    var dq = DispatcherQueue;
                    if (dq is null)
                    {
                        _captureInFlight = false;
                        return;
                    }

                    var enqueued = dq.TryEnqueue(() =>
                    {
                        try
                        {
                            ApplyFreezeFrameToBackground(frame);
                        }
                        finally
                        {
                            _captureInFlight = false;
                        }
                    });

                    if (!enqueued)
                    {
                        _captureInFlight = false;
                    }
                }
                catch
                {
                    _captureInFlight = false;
                }
            });
        }

        private void ApplyFreezeFrameToBackground(FreezeFrame frame)
        {
            var expectedBytes = frame.VirtualBounds.Width * frame.VirtualBounds.Height * 4;
            if (!_underpaintApplied && frame.PixelData.Length >= expectedBytes)
            {
                RootGrid.Background = new SolidColorBrush(
                    SampleCenterBgra(frame.PixelData.AsSpan(0, expectedBytes), frame.VirtualBounds.Width, frame.VirtualBounds.Height, frame.Stride));
                _underpaintApplied = true;
            }

            if (_backgroundBitmap is null ||
                _backgroundBitmap.PixelWidth != frame.VirtualBounds.Width ||
                _backgroundBitmap.PixelHeight != frame.VirtualBounds.Height)
            {
                _backgroundBitmap = new WriteableBitmap(frame.VirtualBounds.Width, frame.VirtualBounds.Height);
                BackgroundImage.Source = _backgroundBitmap;
            }

            var pixelBuffer = _backgroundBitmap.PixelBuffer;
            var bufferBytes = (int)pixelBuffer.Length;
            if (frame.PixelData.Length < bufferBytes)
            {
                return;
            }

            using var stream = pixelBuffer.AsStream();
            stream.Position = 0;
            var src = frame.PixelData.AsSpan(0, bufferBytes);
            stream.Write(src);
            _backgroundBitmap.Invalidate();
        }

        private static Windows.UI.Color SampleCenterBgra(ReadOnlySpan<byte> pixels, int width, int height, int stride)
        {
            if (width < 1 || height < 1 || pixels.Length < 4)
            {
                return Windows.UI.Color.FromArgb(255, 28, 28, 28);
            }

            var cx = width / 2;
            var cy = height / 2;
            var offset = (cy * stride) + (cx * 4);
            if (offset + 3 >= pixels.Length)
            {
                return Windows.UI.Color.FromArgb(255, 28, 28, 28);
            }

            var b = pixels[offset];
            var g = pixels[offset + 1];
            var r = pixels[offset + 2];
            var a = pixels[offset + 3];
            if (a < 1)
            {
                a = 255;
            }

            return Windows.UI.Color.FromArgb(a, r, g, b);
        }

        private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                CompleteSession();
            }
        }

        private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(RootGrid);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            _isPointerDown = true;
            _pointerDownLocal = point.Position;
            _ = RootGrid.CapturePointer(e.Pointer);

            var rectMod = SettingsService.LoadLiveDrawRectangleModifier();
            // When rectangle uses Shift: Shift alone (without Alt) = rectangle; Ctrl+Shift, Alt, or Shift+Alt = arrow.
            if (rectMod == LiveDrawRectangleModifier.Shift && IsShiftDown(e) && !IsControlDown(e) &&
                !IsAltMenuWithoutCtrl(e))
            {
                _activeTool = LiveDrawTool.Rectangle;
            }
            else if (IsArrowModifierDown(rectMod, e))
            {
                _activeTool = LiveDrawTool.Arrow;
            }
            else if (IsRectangleModifierDown(e))
            {
                _activeTool = LiveDrawTool.Rectangle;
            }
            else
            {
                _activeTool = LiveDrawTool.FreeDraw;
            }

            switch (_activeTool)
            {
                case LiveDrawTool.FreeDraw:
                    _currentPoints = new PointCollection { _pointerDownLocal };
                    _currentPolyline = new Polyline
                    {
                        Stroke = new SolidColorBrush(ParsePrimaryColor()),
                        StrokeThickness = Math.Max(1, SettingsService.LoadEditorUiSettings().PrimaryThickness),
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        Points = _currentPoints
                    };
                    DrawCanvas.Children.Add(_currentPolyline);
                    break;

                case LiveDrawTool.Rectangle:
                    _snapBorderChrome.PickNextBorderPalette();
                    _snapBorderChrome.ResetDashSpeedToDefault();
                    break;

                case LiveDrawTool.Arrow:
                    _snapBorderChrome.PickNextBorderPalette();
                    _snapBorderChrome.ResetDashSpeedToDefault();
                    break;
            }
        }

        private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(RootGrid);
            var local = point.Position;

            if (!_isPointerDown)
            {
                return;
            }

            switch (_activeTool)
            {
                case LiveDrawTool.FreeDraw:
                    if (_currentPoints is not null)
                    {
                        _currentPoints.Add(local);
                    }

                    break;

                case LiveDrawTool.Rectangle:
                    {
                        var left = Math.Min(_pointerDownLocal.X, local.X);
                        var top = Math.Min(_pointerDownLocal.Y, local.Y);
                        var w = Math.Abs(local.X - _pointerDownLocal.X);
                        var h = Math.Abs(local.Y - _pointerDownLocal.Y);
                        _snapBorderChrome.UpdateChaseSpeedForPixelSize(w, h);
                        _snapBorderChrome.UpdateSnapBorderLayers(left, top, (int)Math.Max(1, w), (int)Math.Max(1, h));
                        _snapBorderChrome.SetSnapBorderLayersVisible(true);
                        _snapBorderChrome.StartSnapBorderAnimations();
                        break;
                    }

                case LiveDrawTool.Arrow:
                    PreviewCanvas.Children.Clear();
                    var settings = SettingsService.LoadEditorUiSettings();
                    var arrow = BuildArrowLayer(local, _pointerDownLocal, settings);
                    var segDx = local.X - _pointerDownLocal.X;
                    var segDy = local.Y - _pointerDownLocal.Y;
                    _snapBorderChrome.UpdateChaseSpeedForArrowLength(Math.Sqrt((segDx * segDx) + (segDy * segDy)));
                    _snapBorderChrome.DrawSnapChromeArrow(PreviewCanvas, arrow);
                    break;
            }
        }

        private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPointerDown)
            {
                return;
            }

            _isPointerDown = false;
            RootGrid.ReleasePointerCaptures();

            var point = e.GetCurrentPoint(RootGrid);
            var local = point.Position;

            switch (_activeTool)
            {
                case LiveDrawTool.Rectangle:
                    {
                        var left = Math.Min(_pointerDownLocal.X, local.X);
                        var top = Math.Min(_pointerDownLocal.Y, local.Y);
                        var w = Math.Abs(local.X - _pointerDownLocal.X);
                        var h = Math.Abs(local.Y - _pointerDownLocal.Y);
                        if (w > 2 && h > 2)
                        {
                            _snapBorderChrome.CommitSnapBorderToDrawCanvas(DrawCanvas, left, top, w, h);
                            _snapBorderChrome.StopSnapBorderAnimations();
                            _snapBorderChrome.SetSnapBorderLayersVisible(false);
                        }
                        else
                        {
                            _snapBorderChrome.StopSnapBorderAnimations();
                            _snapBorderChrome.SetSnapBorderLayersVisible(false);
                        }

                        break;
                    }

                case LiveDrawTool.Arrow:
                    {
                        PreviewCanvas.Children.Clear();
                        var settings = SettingsService.LoadEditorUiSettings();
                        var arrow = BuildArrowLayer(local, _pointerDownLocal, settings);
                        _snapBorderChrome.CommitSnapChromeArrowToDrawCanvas(DrawCanvas, arrow);
                        break;
                    }
            }

            _currentPolyline = null;
            _currentPoints = null;
        }

        private static ArrowLayer BuildArrowLayer(Point tail, Point tip, EditorUiSettings settings)
        {
            var scaledThickness = Math.Max(1, settings.PrimaryThickness) * LiveDrawArrowSizeScale;
            return new ArrowLayer(
                tail.X,
                tail.Y,
                tip.X,
                tip.Y,
                scaledThickness,
                settings.PrimaryColorHex,
                ArrowFormStyle.Straight)
            {
                HasBorder = false,
                BorderThickness = 0,
                HasShadow = false,
                ShadowColorHex = "#66000000",
                ShadowOffset = Math.Max(1, (int)Math.Round(2 * LiveDrawArrowSizeScale))
            };
        }

        private static Color ParsePrimaryColor()
        {
            var hex = SettingsService.LoadEditorUiSettings().PrimaryColorHex;
            return ParseColorHex(hex);
        }

        private static Color ParseColorHex(string colorHex)
        {
            if (string.IsNullOrWhiteSpace(colorHex))
            {
                return Microsoft.UI.Colors.White;
            }

            var value = colorHex.Trim().TrimStart('#');
            if (value.Length == 6)
            {
                var rgb = Convert.ToUInt32(value, 16);
                var r = (byte)((rgb & 0xFF0000) >> 16);
                var g = (byte)((rgb & 0x00FF00) >> 8);
                var b = (byte)(rgb & 0x0000FF);
                return Color.FromArgb(255, r, g, b);
            }

            if (value.Length == 8)
            {
                var argb = Convert.ToUInt32(value, 16);
                var a = (byte)((argb & 0xFF000000) >> 24);
                var r = (byte)((argb & 0x00FF0000) >> 16);
                var g = (byte)((argb & 0x0000FF00) >> 8);
                var b = (byte)(argb & 0x000000FF);
                return Color.FromArgb(a, r, g, b);
            }

            return Microsoft.UI.Colors.White;
        }

        private void CompleteSession()
        {
            if (_sessionCompletion.Task.IsCompleted)
            {
                return;
            }

            _sessionCompletion.TrySetResult(true);
            Close();
        }

        private static bool IsArrowModifierDown(LiveDrawRectangleModifier rectMod, PointerRoutedEventArgs? e)
        {
            if (rectMod == LiveDrawRectangleModifier.Shift)
            {
                return (IsShiftDown(e) && IsControlDown(e)) || IsAltMenuWithoutCtrl(e);
            }

            if (rectMod != LiveDrawRectangleModifier.Alt && IsAltMenuWithoutCtrl(e))
            {
                return true;
            }

            return IsShiftDown(e);
        }

        private static bool IsShiftDown(PointerRoutedEventArgs? e = null)
        {
            if (e is not null && (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0)
            {
                return true;
            }

            return (GetAsyncKeyState(VkShift) & 0x8000) != 0 ||
                   (GetAsyncKeyState(VkLShift) & 0x8000) != 0 ||
                   (GetAsyncKeyState(VkRShift) & 0x8000) != 0;
        }

        private static bool IsControlDown(PointerRoutedEventArgs? e = null)
        {
            if (e is not null && (e.KeyModifiers & VirtualKeyModifiers.Control) != 0)
            {
                return true;
            }

            return (GetAsyncKeyState(VkControl) & 0x8000) != 0;
        }

        /// <summary>
        /// Alt key without Ctrl (excludes AltGr / Ctrl+Alt) for rectangle mode when Alt is the configured modifier.
        /// </summary>
        private static bool IsAltMenuWithoutCtrl(PointerRoutedEventArgs? e = null)
        {
            if (e is not null)
            {
                var km = e.KeyModifiers;
                var menuDown = (km & VirtualKeyModifiers.Menu) != 0;
                var ctrlDown = (km & VirtualKeyModifiers.Control) != 0;
                if (menuDown && !ctrlDown)
                {
                    return true;
                }
            }

            var alt = (GetAsyncKeyState(VkMenu) & 0x8000) != 0 ||
                      (GetAsyncKeyState(VkLMenu) & 0x8000) != 0 ||
                      (GetAsyncKeyState(VkRMenu) & 0x8000) != 0;
            if (!alt)
            {
                return false;
            }

            if ((GetAsyncKeyState(VkControl) & 0x8000) != 0)
            {
                return false;
            }

            return true;
        }

        private static bool IsWinDown(PointerRoutedEventArgs? e = null)
        {
            if (e is not null && (e.KeyModifiers & VirtualKeyModifiers.Windows) != 0)
            {
                return true;
            }

            return (GetAsyncKeyState(VkLwin) & 0x8000) != 0 ||
                   (GetAsyncKeyState(VkRwin) & 0x8000) != 0;
        }

        private bool IsRectangleModifierDown(PointerRoutedEventArgs? e = null)
        {
            return SettingsService.LoadLiveDrawRectangleModifier() switch
            {
                LiveDrawRectangleModifier.Shift => IsShiftDown(e),
                LiveDrawRectangleModifier.Control => IsControlDown(e),
                LiveDrawRectangleModifier.Win => IsWinDown(e),
                LiveDrawRectangleModifier.Alt => IsAltMenuWithoutCtrl(e),
                _ => false
            };
        }

        private void EnforceBorderlessWindowStyles()
        {
            if (_windowHandle == nint.Zero)
            {
                return;
            }

            var currentStyle = GetWindowLongPtr(_windowHandle, GwlStyle);
            if (currentStyle == nint.Zero)
            {
                return;
            }

            var borderStyleMask = WsCaption | WsThickframe | WsBorder | WsDlgframe;
            var borderlessStyle = currentStyle & ~borderStyleMask;
            if (borderlessStyle != currentStyle)
            {
                _ = SetWindowLongPtr(_windowHandle, GwlStyle, borderlessStyle);
            }

            _ = SetWindowPos(
                _windowHandle,
                (nint)HwndTopmost,
                0,
                0,
                0,
                0,
                SwpNomove | SwpNosize | SwpNoactivate | SwpFramechanged);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(nint hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            nint hWnd,
            nint hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(nint hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowDisplayAffinity(nint hwnd, uint dwAffinity);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        private static nint GetWindowLongPtr(nint hWnd, int nIndex)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, nIndex)
                : new nint(GetWindowLong32(hWnd, nIndex));
        }

        private static nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                : new nint(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        private enum LiveDrawTool
        {
            FreeDraw,
            Rectangle,
            Arrow
        }
    }
}
