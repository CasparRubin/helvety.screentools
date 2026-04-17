using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using helvety.screentools;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace helvety.screentools.Capture
{
    internal sealed partial class SelectionOverlayWindow : Window
    {
        private const int IdcArrow = 32512;
        private const int HwndTopmost = -1;
        private const uint SwpNomove = 0x0002;
        private const uint SwpNosize = 0x0001;
        private const uint SwpFramechanged = 0x0020;
        private const uint WmKeydown = 0x0100;
        private const uint VkEscape = 0x1B;
        private const uint EscapeSubclassId = 0x48535431;
        private const int GwlStyle = -16;
        private const nint WsCaption = 0x00C00000;
        private const nint WsThickframe = 0x00040000;
        private const nint WsBorder = 0x00800000;
        private const nint WsDlgframe = 0x00400000;
        private const int DragThresholdPixels = 3;
        private const double InstructionPanelMargin = 24;
        private const double SessionToastTopSpacing = 10;
        private const int SessionToastDurationMilliseconds = 4500;
        private const double BorderFxTickMilliseconds = 70.0;

        private readonly FreezeFrame _freezeFrame;
        private readonly WindowSnapHitTester _hitTester;
        private readonly SnapBorderChromeController _snapBorderChrome;
        private readonly SolidColorBrush _crosshairBaseBrush = new(Color.FromArgb(255, 186, 92, 126));
        private readonly SolidColorBrush _crosshairAccentBrush = new(Color.FromArgb(255, 216, 27, 96));
        private readonly SolidColorBrush _crosshairAnimatedBrush = new(Color.FromArgb(255, 186, 92, 126));
        private TaskCompletionSource<SelectionAction> _selectionCompletionSource = new();
        private readonly nint _windowHandle;
        private readonly DispatcherQueueTimer _colorDriftTimer;
        private readonly DispatcherQueueTimer _sessionToastTimer;
        private Compositor? _compositor;
        private Visual? _verticalGuideVisual;
        private Visual? _horizontalGuideVisual;
        private ScalarKeyFrameAnimation? _crosshairOpacityAnimation;
        private bool _isSessionStarted;
        private bool _isPointerDown;
        private bool _isDragging;
        private bool _showInstructionPanel;
        private double _crosshairAccentElapsedSeconds;
        private SelectionCommitMode _activeCommitMode = SelectionCommitMode.LeftCommitExit;
        private Point _dragStartLocal;
        private Point _currentLocal;
        private RectInt32? _activeSnapBounds;

        private static SelectionOverlayWindow? s_activeOverlayForEscape;
        private static readonly SubclassProcDelegate s_escapeSubclassProc = EscapeSubclassProc;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate nint SubclassProcDelegate(
            nint hWnd,
            uint msg,
            nint wParam,
            nint lParam,
            nuint uIdSubclass,
            nuint dwRefData);

        public SelectionOverlayWindow(FreezeFrame freezeFrame, WindowSnapHitTester hitTester, bool showInstructionPanel)
        {
            _freezeFrame = freezeFrame;
            _hitTester = hitTester;
            _showInstructionPanel = showInstructionPanel;
            InitializeComponent();
            _snapBorderChrome = new SnapBorderChromeController(
                RootGrid,
                SnapBorderRectangle,
                SnapBorderChaseRectangle,
                SnapBorderCornerGlowRectangle,
                SnapBorderGlowRectangle,
                OverlayCanvas);
            VerticalCursorGuide.Stroke = _crosshairAnimatedBrush;
            HorizontalCursorGuide.Stroke = _crosshairAnimatedBrush;
            EnsureArrowCursor();
            _colorDriftTimer = DispatcherQueue.CreateTimer();
            _colorDriftTimer.Interval = TimeSpan.FromMilliseconds(BorderFxTickMilliseconds);
            _colorDriftTimer.Tick += ColorDriftTimer_Tick;
            _sessionToastTimer = DispatcherQueue.CreateTimer();
            _sessionToastTimer.Interval = TimeSpan.FromMilliseconds(SessionToastDurationMilliseconds);
            _sessionToastTimer.Tick += SessionToastTimer_Tick;

            _windowHandle = WindowNative.GetWindowHandle(this);
            ConfigureOverlayWindow();
            RenderBackground();
            InitializeCompositionAnimations();
            Closed += SelectionOverlayWindow_Closed;
            s_activeOverlayForEscape = this;
            _ = SetWindowSubclass(_windowHandle, s_escapeSubclassProc, EscapeSubclassId, 0);
        }

        public async Task<SelectionAction> RunSelectionAsync()
        {
            if (!_isSessionStarted)
            {
                Activate();
                EnforceBorderlessWindowStyles();
                _isSessionStarted = true;
            }

            EnsureFocusedForKeyboard();
            RootGrid.Focus(FocusState.Programmatic);

            if (_selectionCompletionSource.Task.IsCompleted)
            {
                _selectionCompletionSource = new TaskCompletionSource<SelectionAction>();
            }

            InitializeSnapAtCurrentCursor();
            EnsureArrowCursor();
            return await _selectionCompletionSource.Task;
        }

        private void ConfigureOverlayWindow()
        {
            var appWindow = GetAppWindowForCurrentWindow();
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsAlwaysOnTop = true;
            }

            appWindow.MoveAndResize(_freezeFrame.VirtualBounds);
            EnforceBorderlessWindowStyles();

            OverlayCanvas.Width = _freezeFrame.VirtualBounds.Width;
            OverlayCanvas.Height = _freezeFrame.VirtualBounds.Height;
            UpdateCursorGuides(new Point(OverlayCanvas.Width / 2, OverlayCanvas.Height / 2));
            HideWindowDimming();
            InstructionPanel.Visibility = _showInstructionPanel ? Visibility.Visible : Visibility.Collapsed;
            PlaceOverlayChrome();
            HideSessionToast();
            UpdateInstructionStatus("Waiting for selection...");

            NativeInterop.SetWindowPos(_windowHandle, (nint)HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpFramechanged);
        }

        private void RenderBackground()
        {
            var bitmap = new WriteableBitmap(_freezeFrame.VirtualBounds.Width, _freezeFrame.VirtualBounds.Height);
            using var stream = bitmap.PixelBuffer.AsStream();
            stream.Write(_freezeFrame.PixelData, 0, _freezeFrame.PixelData.Length);
            BackgroundImage.Source = bitmap;
        }

        private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CompleteSelection(new SelectionAction(SelectionCommitMode.Cancel, null));
            }
        }

        private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(RootGrid);
            if (point.Properties.IsLeftButtonPressed)
            {
                _activeCommitMode = SelectionCommitMode.LeftCommitExit;
            }
            else if (point.Properties.IsRightButtonPressed)
            {
                _activeCommitMode = SelectionCommitMode.RightCommitContinue;
            }
            else
            {
                return;
            }

            _isPointerDown = true;
            _isDragging = false;
            var clampedPress = ClampToOverlayBounds(point.Position);
            _dragStartLocal = clampedPress;
            _currentLocal = clampedPress;
            UpdateSnapBounds(clampedPress, suppressFullVirtualBounds: true);
            _ = RootGrid.CapturePointer(e.Pointer);
            EnsureFocusedForKeyboard();
        }

        private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            EnsureArrowCursor();
            var point = e.GetCurrentPoint(RootGrid);
            _currentLocal = point.Position;
            UpdateCursorGuides(point.Position);

            if (_isPointerDown && IsActiveButtonStillPressed(point))
            {
                var dragDeltaX = Math.Abs(_currentLocal.X - _dragStartLocal.X);
                var dragDeltaY = Math.Abs(_currentLocal.Y - _dragStartLocal.Y);
                if (!_isDragging && (dragDeltaX >= DragThresholdPixels || dragDeltaY >= DragThresholdPixels))
                {
                    _isDragging = true;
                }

                if (_isDragging)
                {
                    _activeSnapBounds = null;
                    _snapBorderChrome.SetSnapBorderLayersVisible(false);
                    HideWindowDimming();
                    _snapBorderChrome.StopSnapBorderAnimations();
                    UpdateDragRectangle();
                }
                return;
            }

            _isDragging = false;
            DragRectangle.Visibility = Visibility.Collapsed;
            UpdateSnapBounds(point.Position);
        }

        private void UpdateCursorGuides(Point localPoint)
        {
            var clampedPoint = ClampToOverlayBounds(localPoint);
            var clampedX = clampedPoint.X;
            var clampedY = clampedPoint.Y;

            VerticalCursorGuide.X1 = clampedX;
            VerticalCursorGuide.Y1 = 0;
            VerticalCursorGuide.X2 = clampedX;
            VerticalCursorGuide.Y2 = OverlayCanvas.Height;

            HorizontalCursorGuide.X1 = 0;
            HorizontalCursorGuide.Y1 = clampedY;
            HorizontalCursorGuide.X2 = OverlayCanvas.Width;
            HorizontalCursorGuide.Y2 = clampedY;
        }

        private void InitializeSnapAtCurrentCursor()
        {
            if (!GetCursorPos(out var cursorPosition))
            {
                return;
            }

            var localPoint = new Point(
                cursorPosition.X - _freezeFrame.VirtualBounds.X,
                cursorPosition.Y - _freezeFrame.VirtualBounds.Y);
            var clampedPoint = ClampToOverlayBounds(localPoint);
            _currentLocal = clampedPoint;
            UpdateCursorGuides(clampedPoint);
            UpdateSnapBounds(clampedPoint, suppressFullVirtualBounds: true);
        }

        private Point ClampToOverlayBounds(Point localPoint)
        {
            return new Point(
                Math.Clamp(localPoint.X, 0, OverlayCanvas.Width),
                Math.Clamp(localPoint.Y, 0, OverlayCanvas.Height));
        }

        private void EnsureArrowCursor()
        {
            var arrowCursor = LoadCursor(nint.Zero, (nint)IdcArrow);
            if (arrowCursor != nint.Zero)
            {
                SetCursor(arrowCursor);
            }
        }

        public void UpdateInstructionStatus(string message)
        {
            InstructionStatusText.Text = message;
        }

        public void ShowSessionToast(string title, string detail)
        {
            SessionToastTitleText.Text = title;
            SessionToastDetailText.Text = detail;
            PlaceOverlayChrome();
            SessionToastCard.Visibility = Visibility.Visible;
            _sessionToastTimer.Stop();
            _sessionToastTimer.Start();
        }

        private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPointerDown)
            {
                return;
            }

            var releasePoint = e.GetCurrentPoint(RootGrid);
            _currentLocal = ClampToOverlayBounds(releasePoint.Position);

            _isPointerDown = false;
            RootGrid.ReleasePointerCaptures();

            if (_isDragging)
            {
                var dragBounds = BuildScreenRectFromLocalPoints(_dragStartLocal, _currentLocal);
                if (dragBounds.Width > 1 && dragBounds.Height > 1)
                {
                    CompleteSelection(new SelectionAction(_activeCommitMode, dragBounds));
                    return;
                }

                _isDragging = false;
                DragRectangle.Visibility = Visibility.Collapsed;
                CommitSnapOrFullVirtualAt(_currentLocal);
                return;
            }

            CommitSnapOrFullVirtualAt(_currentLocal);
        }

        private void CommitSnapOrFullVirtualAt(Point localPoint)
        {
            var clamped = ClampToOverlayBounds(localPoint);
            UpdateSnapBounds(clamped, suppressFullVirtualBounds: false);
            if (_activeSnapBounds is RectInt32 snapBounds)
            {
                CompleteSelection(new SelectionAction(_activeCommitMode, snapBounds));
                return;
            }

            var screenX = _freezeFrame.VirtualBounds.X + (int)Math.Round(clamped.X);
            var screenY = _freezeFrame.VirtualBounds.Y + (int)Math.Round(clamped.Y);
            if (MonitorBoundsResolver.TryGetMonitorBoundsAtPoint(screenX, screenY, out var monitorBounds))
            {
                CompleteSelection(new SelectionAction(_activeCommitMode, monitorBounds));
                return;
            }

            CompleteSelection(new SelectionAction(_activeCommitMode, _freezeFrame.VirtualBounds));
        }

        private void UpdateSnapBounds(Point localPoint, bool suppressFullVirtualBounds = false)
        {
            var screenX = _freezeFrame.VirtualBounds.X + (int)Math.Round(localPoint.X);
            var screenY = _freezeFrame.VirtualBounds.Y + (int)Math.Round(localPoint.Y);
            if (!_hitTester.TryGetSnapBoundsAt(screenX, screenY, _windowHandle, out var bounds))
            {
                _activeSnapBounds = null;
                _snapBorderChrome.ResetDashSpeedToDefault();
                _snapBorderChrome.SetSnapBorderLayersVisible(false);
                HideWindowDimming();
                _snapBorderChrome.StopSnapBorderAnimations();
                return;
            }

            if (suppressFullVirtualBounds && IsFullVirtualBounds(bounds))
            {
                _activeSnapBounds = null;
                _snapBorderChrome.ResetDashSpeedToDefault();
                _snapBorderChrome.SetSnapBorderLayersVisible(false);
                HideWindowDimming();
                _snapBorderChrome.StopSnapBorderAnimations();
                return;
            }

            var isNewSnapBounds = !_activeSnapBounds.HasValue || !_activeSnapBounds.Value.Equals(bounds);
            if (isNewSnapBounds)
            {
                _snapBorderChrome.PickNextBorderPalette();
            }

            _snapBorderChrome.UpdateChaseSpeedForBounds(bounds);
            _activeSnapBounds = bounds;
            var localX = bounds.X - _freezeFrame.VirtualBounds.X;
            var localY = bounds.Y - _freezeFrame.VirtualBounds.Y;

            _snapBorderChrome.UpdateSnapBorderLayers(localX, localY, bounds.Width, bounds.Height);
            _snapBorderChrome.SetSnapBorderLayersVisible(true);
            UpdateWindowDimming(localX, localY, bounds.Width, bounds.Height);
            _snapBorderChrome.StartSnapBorderAnimations();
        }

        private void UpdateDragRectangle()
        {
            var localLeft = Math.Min(_dragStartLocal.X, _currentLocal.X);
            var localTop = Math.Min(_dragStartLocal.Y, _currentLocal.Y);
            var localWidth = Math.Abs(_dragStartLocal.X - _currentLocal.X);
            var localHeight = Math.Abs(_dragStartLocal.Y - _currentLocal.Y);

            Canvas.SetLeft(DragRectangle, localLeft);
            Canvas.SetTop(DragRectangle, localTop);
            DragRectangle.Width = Math.Max(1, localWidth);
            DragRectangle.Height = Math.Max(1, localHeight);
            DragRectangle.Visibility = Visibility.Visible;
        }

        private void UpdateWindowDimming(int localX, int localY, int width, int height)
        {
            var canvasWidth = (int)OverlayCanvas.Width;
            var canvasHeight = (int)OverlayCanvas.Height;

            var x = Math.Clamp(localX, 0, canvasWidth);
            var y = Math.Clamp(localY, 0, canvasHeight);
            var right = Math.Clamp(localX + width, 0, canvasWidth);
            var bottom = Math.Clamp(localY + height, 0, canvasHeight);
            var selectedWidth = Math.Max(0, right - x);
            var selectedHeight = Math.Max(0, bottom - y);

            SetDimRectangle(DimTop, 0, 0, canvasWidth, y);
            SetDimRectangle(DimLeft, 0, y, x, selectedHeight);
            SetDimRectangle(DimRight, right, y, Math.Max(0, canvasWidth - right), selectedHeight);
            SetDimRectangle(DimBottom, 0, bottom, canvasWidth, Math.Max(0, canvasHeight - bottom));
        }

        private static void SetDimRectangle(FrameworkElement rectangle, double x, double y, double width, double height)
        {
            if (width <= 0 || height <= 0)
            {
                rectangle.Visibility = Visibility.Collapsed;
                return;
            }

            Canvas.SetLeft(rectangle, x);
            Canvas.SetTop(rectangle, y);
            rectangle.Width = width;
            rectangle.Height = height;
            rectangle.Visibility = Visibility.Visible;
        }

        private void HideWindowDimming()
        {
            DimTop.Visibility = Visibility.Collapsed;
            DimLeft.Visibility = Visibility.Collapsed;
            DimRight.Visibility = Visibility.Collapsed;
            DimBottom.Visibility = Visibility.Collapsed;
        }

        private RectInt32 BuildScreenRectFromLocalPoints(Point a, Point b)
        {
            // Floor start coordinates and ceil end coordinates so tiny drags don't clip text edges.
            var localLeft = (int)Math.Floor(Math.Min(a.X, b.X));
            var localTop = (int)Math.Floor(Math.Min(a.Y, b.Y));
            var localRight = (int)Math.Ceiling(Math.Max(a.X, b.X));
            var localBottom = (int)Math.Ceiling(Math.Max(a.Y, b.Y));
            return new RectInt32(
                _freezeFrame.VirtualBounds.X + localLeft,
                _freezeFrame.VirtualBounds.Y + localTop,
                Math.Max(0, localRight - localLeft),
                Math.Max(0, localBottom - localTop));
        }

        private bool IsActiveButtonStillPressed(PointerPoint point)
        {
            return _activeCommitMode == SelectionCommitMode.RightCommitContinue
                ? point.Properties.IsRightButtonPressed
                : point.Properties.IsLeftButtonPressed;
        }

        private void PrepareForNextSelection()
        {
            _isPointerDown = false;
            _isDragging = false;
            _activeCommitMode = SelectionCommitMode.LeftCommitExit;
            DragRectangle.Visibility = Visibility.Collapsed;
            RootGrid.ReleasePointerCaptures();
            _snapBorderChrome.StopSnapBorderAnimations();
            _snapBorderChrome.PickNextBorderPalette();
            InitializeSnapAtCurrentCursor();
            UpdateInstructionStatus("Ready for next screenshot...");
        }

        private bool IsFullVirtualBounds(RectInt32 bounds)
        {
            return bounds.X == _freezeFrame.VirtualBounds.X &&
                   bounds.Y == _freezeFrame.VirtualBounds.Y &&
                   bounds.Width == _freezeFrame.VirtualBounds.Width &&
                   bounds.Height == _freezeFrame.VirtualBounds.Height;
        }

        private void EnforceBorderlessWindowStyles()
        {
            if (_windowHandle == nint.Zero)
            {
                return;
            }

            var currentStyle = NativeInterop.GetWindowLongPtr(_windowHandle, GwlStyle);
            if (currentStyle == nint.Zero)
            {
                return;
            }

            var borderStyleMask = WsCaption | WsThickframe | WsBorder | WsDlgframe;
            var borderlessStyle = currentStyle & ~borderStyleMask;
            if (borderlessStyle != currentStyle)
            {
                _ = NativeInterop.SetWindowLongPtr(_windowHandle, GwlStyle, borderlessStyle);
            }

            _ = NativeInterop.SetWindowPos(
                _windowHandle,
                (nint)HwndTopmost,
                0,
                0,
                0,
                0,
                SwpNomove | SwpNosize | SwpFramechanged);
        }

        private void EnsureFocusedForKeyboard()
        {
            if (_windowHandle == nint.Zero)
            {
                return;
            }

            _ = NativeInterop.SetForegroundWindow(_windowHandle);
            _ = NativeInterop.SetFocus(_windowHandle);
        }

        private void QueueEscapeFromNative()
        {
            var dq = DispatcherQueue.GetForCurrentThread();
            if (dq is null)
            {
                return;
            }

            _ = dq.TryEnqueue(() =>
                CompleteSelection(new SelectionAction(SelectionCommitMode.Cancel, null)));
        }

        /// <summary>Cancel from low-level hotkey hook; must run on the UI thread (caller enqueues).</summary>
        internal void RequestCancelFromExternal()
        {
            CompleteSelection(new SelectionAction(SelectionCommitMode.Cancel, null));
        }

        private static nint EscapeSubclassProc(
            nint hWnd,
            uint msg,
            nint wParam,
            nint lParam,
            nuint uIdSubclass,
            nuint dwRefData)
        {
            if (msg == WmKeydown && (uint)(nint)wParam == VkEscape)
            {
                s_activeOverlayForEscape?.QueueEscapeFromNative();
                return 0;
            }

            return DefSubclassProc(hWnd, msg, wParam, lParam);
        }

        private void EnsureChromeMeasured(FrameworkElement element)
        {
            if (element.ActualWidth > 0 && element.ActualHeight > 0)
            {
                return;
            }

            element.Measure(new Size(OverlayCanvas.Width, OverlayCanvas.Height));
        }

        private void PlaceOverlayChrome()
        {
            EnsureChromeMeasured(InstructionPanel);
            var width = InstructionPanel.ActualWidth > 0 ? InstructionPanel.ActualWidth : InstructionPanel.DesiredSize.Width;
            var height = InstructionPanel.ActualHeight > 0 ? InstructionPanel.ActualHeight : InstructionPanel.DesiredSize.Height;
            var top = InstructionPanelMargin;
            var right = Math.Max(InstructionPanelMargin, OverlayCanvas.Width - width - InstructionPanelMargin);
            Canvas.SetLeft(InstructionPanel, right);
            Canvas.SetTop(InstructionPanel, top);

            EnsureChromeMeasured(SessionToastCard);
            var toastWidth = SessionToastCard.ActualWidth > 0 ? SessionToastCard.ActualWidth : SessionToastCard.DesiredSize.Width;
            var toastHeight = SessionToastCard.ActualHeight > 0 ? SessionToastCard.ActualHeight : SessionToastCard.DesiredSize.Height;
            var toastTop = top + (_showInstructionPanel ? height + SessionToastTopSpacing : 0);
            var toastLeft = Math.Max(InstructionPanelMargin, OverlayCanvas.Width - toastWidth - InstructionPanelMargin);
            Canvas.SetLeft(SessionToastCard, toastLeft);
            Canvas.SetTop(SessionToastCard, Math.Max(InstructionPanelMargin, Math.Min(toastTop, OverlayCanvas.Height - toastHeight - InstructionPanelMargin)));
        }

        private void HideSessionToast()
        {
            _sessionToastTimer.Stop();
            SessionToastCard.Visibility = Visibility.Collapsed;
        }

        private void SessionToastTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            HideSessionToast();
        }

        private void CompleteSelection(SelectionAction action)
        {
            if (_selectionCompletionSource.Task.IsCompleted)
            {
                return;
            }

            _selectionCompletionSource.TrySetResult(action);
            if (action.Mode == SelectionCommitMode.RightCommitContinue)
            {
                PrepareForNextSelection();
                return;
            }

            _snapBorderChrome.StopSnapBorderAnimations();
            Close();
        }

        private void SelectionOverlayWindow_Closed(object sender, WindowEventArgs args)
        {
            if (s_activeOverlayForEscape == this)
            {
                s_activeOverlayForEscape = null;
            }

            _ = RemoveWindowSubclass(_windowHandle, s_escapeSubclassProc, EscapeSubclassId);
            _colorDriftTimer.Stop();
            _sessionToastTimer.Stop();
            if (!_selectionCompletionSource.Task.IsCompleted)
            {
                _selectionCompletionSource.TrySetResult(new SelectionAction(SelectionCommitMode.Cancel, null));
            }
        }

        private void InitializeCompositionAnimations()
        {
            _compositor = ElementCompositionPreview.GetElementVisual(RootGrid).Compositor;
            _snapBorderChrome.InitializeCompositionAnimations();
            _verticalGuideVisual = ElementCompositionPreview.GetElementVisual(VerticalCursorGuide);
            _horizontalGuideVisual = ElementCompositionPreview.GetElementVisual(HorizontalCursorGuide);

            _crosshairOpacityAnimation = _compositor.CreateScalarKeyFrameAnimation();
            _crosshairOpacityAnimation.InsertKeyFrame(0.0f, 0.62f);
            _crosshairOpacityAnimation.InsertKeyFrame(0.5f, 0.88f);
            _crosshairOpacityAnimation.InsertKeyFrame(1.0f, 0.62f);
            _crosshairOpacityAnimation.Duration = TimeSpan.FromMilliseconds(1850);
            _crosshairOpacityAnimation.IterationBehavior = AnimationIterationBehavior.Forever;

            _verticalGuideVisual?.StartAnimation("Opacity", _crosshairOpacityAnimation);
            _horizontalGuideVisual?.StartAnimation("Opacity", _crosshairOpacityAnimation);
            _colorDriftTimer.Start();
        }

        private void ColorDriftTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            var deltaSeconds = sender.Interval.TotalSeconds;
            _snapBorderChrome.OnColorDriftTick(deltaSeconds);
            _crosshairAccentElapsedSeconds += deltaSeconds;

            var crosshairBlend = (Math.Sin(_crosshairAccentElapsedSeconds * 1.75) + 1.0) / 2.0;
            var crosshairColor = LerpColor(_crosshairBaseBrush.Color, _crosshairAccentBrush.Color, crosshairBlend);
            _crosshairAnimatedBrush.Color = crosshairColor;
        }

        private static Color LerpColor(Color from, Color to, double amount)
        {
            var t = Math.Clamp(amount, 0.0, 1.0);
            var a = (byte)Math.Round(from.A + ((to.A - from.A) * t));
            var r = (byte)Math.Round(from.R + ((to.R - from.R) * t));
            var g = (byte)Math.Round(from.G + ((to.G - from.G) * t));
            var b = (byte)Math.Round(from.B + ((to.B - from.B) * t));
            return Color.FromArgb(a, r, g, b);
        }

        private AppWindow GetAppWindowForCurrentWindow()
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_windowHandle);
            return AppWindow.GetFromWindowId(windowId);
        }

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowSubclass(
            nint hWnd,
            SubclassProcDelegate pfnSubclass,
            uint uIdSubclass,
            uint dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveWindowSubclass(nint hWnd, SubclassProcDelegate pfnSubclass, uint uIdSubclass);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern nint DefSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint LoadCursor(nint hInstance, nint lpCursorName);

        [DllImport("user32.dll")]
        private static extern nint SetCursor(nint hCursor);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out PointStruct lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct PointStruct
        {
            public int X;
            public int Y;
        }
    }

    internal enum SelectionCommitMode
    {
        LeftCommitExit,
        RightCommitContinue,
        Cancel
    }

    internal readonly record struct SelectionAction(SelectionCommitMode Mode, RectInt32? Bounds);
}
