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
using System.Numerics;
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
        private const uint SwpNoactivate = 0x0010;
        private const uint SwpFramechanged = 0x0020;
        private const int GwlStyle = -16;
        private const nint WsCaption = 0x00C00000;
        private const nint WsThickframe = 0x00040000;
        private const nint WsBorder = 0x00800000;
        private const nint WsDlgframe = 0x00400000;
        private const int DragThresholdPixels = 3;
        private const double InstructionPanelMargin = 24;
        private const double SessionToastTopSpacing = 10;
        private const int SessionToastDurationMilliseconds = 4500;
        private const double BaseBorderPulseMaxScale = 1.004;
        private const double BaseBorderGlowPulseMaxScale = 1.0045;
        private const double BaseBorderChasePulseMaxScale = 1.006;
        private const double BaseBorderGlowPadding = 2.0;
        private const double BorderFxTickMilliseconds = 70.0;
        private const double BaseBorderHueShiftDegreesPerSecond = 22.0;
        private const double BaseBorderDashSpeedUnitsPerSecond = 92.0;
        private const double BaseBorderDriftSpeed = 0.28;
        private const double BaseBorderStrokeThickness = 3.0;
        private const double BaseChaseStrokeThickness = 2.0;
        private const double BaseCornerGlowStrokeThickness = 5.0;
        private const double BaseOuterGlowStrokeThickness = 7.0;

        private static readonly double[][] BorderPaletteHueOffsets =
        {
            new[] { 336.0, 344.0, 352.0, 2.0, 12.0 },
            new[] { 328.0, 336.0, 346.0, 356.0, 8.0 },
            new[] { 322.0, 332.0, 342.0, 354.0, 6.0 },
            new[] { 330.0, 340.0, 350.0, 0.0, 10.0 },
            new[] { 334.0, 344.0, 354.0, 4.0, 14.0 }
        };

        private readonly FreezeFrame _freezeFrame;
        private readonly WindowSnapHitTester _hitTester;
        private readonly BorderFxProfile _borderFxProfile;
        private readonly Random _random = new();
        private readonly LinearGradientBrush _snapBorderGradientBrush;
        private readonly LinearGradientBrush _snapBorderChaseGradientBrush;
        private readonly LinearGradientBrush _snapBorderGlowGradientBrush;
        private readonly SolidColorBrush _snapBorderCornerGlowBrush = new(Color.FromArgb(170, 255, 255, 255));
        private readonly GradientStop[] _snapBorderGradientStops;
        private readonly GradientStop[] _snapBorderChaseGradientStops;
        private readonly GradientStop[] _snapBorderGlowGradientStops;
        private readonly SolidColorBrush _crosshairBaseBrush = new(Color.FromArgb(255, 186, 92, 126));
        private readonly SolidColorBrush _crosshairAccentBrush = new(Color.FromArgb(255, 216, 27, 96));
        private readonly SolidColorBrush _crosshairAnimatedBrush = new(Color.FromArgb(255, 186, 92, 126));
        private TaskCompletionSource<SelectionAction> _selectionCompletionSource = new();
        private readonly nint _windowHandle;
        private readonly DispatcherQueueTimer _colorDriftTimer;
        private readonly DispatcherQueueTimer _sessionToastTimer;
        private Compositor? _compositor;
        private Visual? _snapBorderVisual;
        private Visual? _snapBorderChaseVisual;
        private Visual? _snapBorderCornerGlowVisual;
        private Visual? _snapBorderGlowVisual;
        private Visual? _verticalGuideVisual;
        private Visual? _horizontalGuideVisual;
        private ScalarKeyFrameAnimation? _borderOpacityAnimation;
        private Vector3KeyFrameAnimation? _borderScaleAnimation;
        private ScalarKeyFrameAnimation? _borderGlowOpacityAnimation;
        private Vector3KeyFrameAnimation? _borderGlowScaleAnimation;
        private ScalarKeyFrameAnimation? _borderChaseOpacityAnimation;
        private Vector3KeyFrameAnimation? _borderChaseScaleAnimation;
        private ScalarKeyFrameAnimation? _crosshairOpacityAnimation;
        private bool _isSessionStarted;
        private bool _isPointerDown;
        private bool _isDragging;
        private bool _isBorderAnimationRunning;
        private bool _showInstructionPanel;
        private double _borderEffectElapsedSeconds;
        private double _crosshairAccentElapsedSeconds;
        private double _currentDashSpeedUnitsPerSecond;
        private int _activePaletteIndex = 0;
        private double[] _activePaletteHueOffsets = BorderPaletteHueOffsets[0];
        private SelectionCommitMode _activeCommitMode = SelectionCommitMode.LeftCommitExit;
        private Point _dragStartLocal;
        private Point _currentLocal;
        private RectInt32? _activeSnapBounds;

        public SelectionOverlayWindow(FreezeFrame freezeFrame, WindowSnapHitTester hitTester, bool showInstructionPanel)
        {
            _freezeFrame = freezeFrame;
            _hitTester = hitTester;
            _showInstructionPanel = showInstructionPanel;
            var configuredIntensity = SettingsService.Load().ScreenshotBorderIntensity;
            _borderFxProfile = CreateBorderFxProfile(configuredIntensity);
            _currentDashSpeedUnitsPerSecond = _borderFxProfile.DashSpeedUnitsPerSecond;
            (_snapBorderGradientBrush, _snapBorderGradientStops) = CreateAnimatedGradientBrush();
            (_snapBorderChaseGradientBrush, _snapBorderChaseGradientStops) = CreateAnimatedGradientBrush();
            (_snapBorderGlowGradientBrush, _snapBorderGlowGradientStops) = CreateAnimatedGradientBrush();
            InitializeComponent();
            SnapBorderRectangle.Stroke = _snapBorderGradientBrush;
            SnapBorderChaseRectangle.Stroke = _snapBorderChaseGradientBrush;
            SnapBorderCornerGlowRectangle.Stroke = _snapBorderCornerGlowBrush;
            SnapBorderGlowRectangle.Stroke = _snapBorderGlowGradientBrush;
            SnapBorderRectangle.StrokeThickness = _borderFxProfile.BorderStrokeThickness;
            SnapBorderChaseRectangle.StrokeThickness = _borderFxProfile.ChaseStrokeThickness;
            SnapBorderCornerGlowRectangle.StrokeThickness = _borderFxProfile.CornerGlowStrokeThickness;
            SnapBorderGlowRectangle.StrokeThickness = _borderFxProfile.OuterGlowStrokeThickness;
            VerticalCursorGuide.Stroke = _crosshairAnimatedBrush;
            HorizontalCursorGuide.Stroke = _crosshairAnimatedBrush;
            EnsureArrowCursor();
            _colorDriftTimer = DispatcherQueue.CreateTimer();
            _colorDriftTimer.Interval = TimeSpan.FromMilliseconds(BorderFxTickMilliseconds);
            _colorDriftTimer.Tick += ColorDriftTimer_Tick;
            _sessionToastTimer = DispatcherQueue.CreateTimer();
            _sessionToastTimer.Interval = TimeSpan.FromMilliseconds(SessionToastDurationMilliseconds);
            _sessionToastTimer.Tick += SessionToastTimer_Tick;
            PickNextBorderPalette();

            _windowHandle = WindowNative.GetWindowHandle(this);
            ConfigureOverlayWindow();
            RenderBackground();
            InitializeCompositionAnimations();
            Closed += SelectionOverlayWindow_Closed;
        }

        public async Task<SelectionAction> RunSelectionAsync()
        {
            if (!_isSessionStarted)
            {
                Activate();
                EnforceBorderlessWindowStyles();
                RootGrid.Focus(FocusState.Programmatic);
                _isSessionStarted = true;
            }

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

            SetWindowPos(_windowHandle, (nint)HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
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
            _dragStartLocal = point.Position;
            _currentLocal = point.Position;
            _ = RootGrid.CapturePointer(e.Pointer);
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
                    SetSnapBorderLayersVisible(false);
                    HideWindowDimming();
                    StopSnapBorderAnimations();
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
                return;
            }

            if (_activeSnapBounds is RectInt32 snapBounds)
            {
                CompleteSelection(new SelectionAction(_activeCommitMode, snapBounds));
            }
        }

        private void UpdateSnapBounds(Point localPoint, bool suppressFullVirtualBounds = false)
        {
            var screenX = _freezeFrame.VirtualBounds.X + (int)Math.Round(localPoint.X);
            var screenY = _freezeFrame.VirtualBounds.Y + (int)Math.Round(localPoint.Y);
            if (!_hitTester.TryGetSnapBoundsAt(screenX, screenY, _windowHandle, out var bounds))
            {
                _activeSnapBounds = null;
                _currentDashSpeedUnitsPerSecond = _borderFxProfile.DashSpeedUnitsPerSecond;
                SetSnapBorderLayersVisible(false);
                HideWindowDimming();
                StopSnapBorderAnimations();
                return;
            }

            if (suppressFullVirtualBounds && IsFullVirtualBounds(bounds))
            {
                _activeSnapBounds = null;
                _currentDashSpeedUnitsPerSecond = _borderFxProfile.DashSpeedUnitsPerSecond;
                SetSnapBorderLayersVisible(false);
                HideWindowDimming();
                StopSnapBorderAnimations();
                return;
            }

            var isNewSnapBounds = !_activeSnapBounds.HasValue || !_activeSnapBounds.Value.Equals(bounds);
            if (isNewSnapBounds)
            {
                PickNextBorderPalette();
            }

            UpdateChaseSpeedForBounds(bounds);
            _activeSnapBounds = bounds;
            var localX = bounds.X - _freezeFrame.VirtualBounds.X;
            var localY = bounds.Y - _freezeFrame.VirtualBounds.Y;

            UpdateSnapBorderLayers(localX, localY, bounds.Width, bounds.Height);
            SetSnapBorderLayersVisible(true);
            UpdateWindowDimming(localX, localY, bounds.Width, bounds.Height);
            if (_snapBorderVisual is not null)
            {
                var center = new Vector3((float)(SnapBorderRectangle.Width / 2.0), (float)(SnapBorderRectangle.Height / 2.0), 0f);
                _snapBorderVisual.CenterPoint = center;
                if (_snapBorderGlowVisual is not null)
                {
                    _snapBorderGlowVisual.CenterPoint = center;
                }

                if (_snapBorderChaseVisual is not null)
                {
                    _snapBorderChaseVisual.CenterPoint = center;
                }

                if (_snapBorderCornerGlowVisual is not null)
                {
                    _snapBorderCornerGlowVisual.CenterPoint = center;
                }
            }
            StartSnapBorderAnimations();
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
            StopSnapBorderAnimations();
            PickNextBorderPalette();
            InitializeSnapAtCurrentCursor();
            UpdateInstructionStatus("Ready for next capture...");
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

            StopSnapBorderAnimations();
            Close();
        }

        private void SelectionOverlayWindow_Closed(object sender, WindowEventArgs args)
        {
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
            _snapBorderVisual = ElementCompositionPreview.GetElementVisual(SnapBorderRectangle);
            _snapBorderChaseVisual = ElementCompositionPreview.GetElementVisual(SnapBorderChaseRectangle);
            _snapBorderCornerGlowVisual = ElementCompositionPreview.GetElementVisual(SnapBorderCornerGlowRectangle);
            _snapBorderGlowVisual = ElementCompositionPreview.GetElementVisual(SnapBorderGlowRectangle);
            _verticalGuideVisual = ElementCompositionPreview.GetElementVisual(VerticalCursorGuide);
            _horizontalGuideVisual = ElementCompositionPreview.GetElementVisual(HorizontalCursorGuide);

            _borderOpacityAnimation = _compositor.CreateScalarKeyFrameAnimation();
            _borderOpacityAnimation.InsertKeyFrame(0.0f, (float)_borderFxProfile.BorderOpacityLow);
            _borderOpacityAnimation.InsertKeyFrame(0.5f, (float)_borderFxProfile.BorderOpacityHigh);
            _borderOpacityAnimation.InsertKeyFrame(1.0f, (float)_borderFxProfile.BorderOpacityLow);
            _borderOpacityAnimation.Duration = TimeSpan.FromMilliseconds(1500);
            _borderOpacityAnimation.IterationBehavior = AnimationIterationBehavior.Forever;

            _borderScaleAnimation = _compositor.CreateVector3KeyFrameAnimation();
            _borderScaleAnimation.InsertKeyFrame(0.0f, Vector3.One);
            _borderScaleAnimation.InsertKeyFrame(0.5f, new Vector3((float)_borderFxProfile.BorderPulseMaxScale, (float)_borderFxProfile.BorderPulseMaxScale, 1f));
            _borderScaleAnimation.InsertKeyFrame(1.0f, Vector3.One);
            _borderScaleAnimation.Duration = TimeSpan.FromMilliseconds(1550);
            _borderScaleAnimation.IterationBehavior = AnimationIterationBehavior.Forever;

            _borderGlowOpacityAnimation = _compositor.CreateScalarKeyFrameAnimation();
            _borderGlowOpacityAnimation.InsertKeyFrame(0.0f, (float)_borderFxProfile.GlowOpacityLow);
            _borderGlowOpacityAnimation.InsertKeyFrame(0.5f, (float)_borderFxProfile.GlowOpacityHigh);
            _borderGlowOpacityAnimation.InsertKeyFrame(1.0f, (float)_borderFxProfile.GlowOpacityLow);
            _borderGlowOpacityAnimation.Duration = TimeSpan.FromMilliseconds(2200);
            _borderGlowOpacityAnimation.IterationBehavior = AnimationIterationBehavior.Forever;

            _borderGlowScaleAnimation = _compositor.CreateVector3KeyFrameAnimation();
            _borderGlowScaleAnimation.InsertKeyFrame(0.0f, Vector3.One);
            _borderGlowScaleAnimation.InsertKeyFrame(0.5f, new Vector3((float)_borderFxProfile.GlowPulseMaxScale, (float)_borderFxProfile.GlowPulseMaxScale, 1f));
            _borderGlowScaleAnimation.InsertKeyFrame(1.0f, Vector3.One);
            _borderGlowScaleAnimation.Duration = TimeSpan.FromMilliseconds(2400);
            _borderGlowScaleAnimation.IterationBehavior = AnimationIterationBehavior.Forever;

            _borderChaseOpacityAnimation = _compositor.CreateScalarKeyFrameAnimation();
            _borderChaseOpacityAnimation.InsertKeyFrame(0.0f, (float)_borderFxProfile.ChaseOpacityLow);
            _borderChaseOpacityAnimation.InsertKeyFrame(0.5f, (float)_borderFxProfile.ChaseOpacityHigh);
            _borderChaseOpacityAnimation.InsertKeyFrame(1.0f, (float)_borderFxProfile.ChaseOpacityLow);
            _borderChaseOpacityAnimation.Duration = TimeSpan.FromMilliseconds(980);
            _borderChaseOpacityAnimation.IterationBehavior = AnimationIterationBehavior.Forever;

            _borderChaseScaleAnimation = _compositor.CreateVector3KeyFrameAnimation();
            _borderChaseScaleAnimation.InsertKeyFrame(0.0f, Vector3.One);
            _borderChaseScaleAnimation.InsertKeyFrame(0.5f, new Vector3((float)_borderFxProfile.ChasePulseMaxScale, (float)_borderFxProfile.ChasePulseMaxScale, 1f));
            _borderChaseScaleAnimation.InsertKeyFrame(1.0f, Vector3.One);
            _borderChaseScaleAnimation.Duration = TimeSpan.FromMilliseconds(1050);
            _borderChaseScaleAnimation.IterationBehavior = AnimationIterationBehavior.Forever;

            _crosshairOpacityAnimation = _compositor.CreateScalarKeyFrameAnimation();
            _crosshairOpacityAnimation.InsertKeyFrame(0.0f, 0.62f);
            _crosshairOpacityAnimation.InsertKeyFrame(0.5f, 0.88f);
            _crosshairOpacityAnimation.InsertKeyFrame(1.0f, 0.62f);
            _crosshairOpacityAnimation.Duration = TimeSpan.FromMilliseconds(1850);
            _crosshairOpacityAnimation.IterationBehavior = AnimationIterationBehavior.Forever;

            _verticalGuideVisual?.StartAnimation("Opacity", _crosshairOpacityAnimation);
            _horizontalGuideVisual?.StartAnimation("Opacity", _crosshairOpacityAnimation);
            UpdateAnimatedBorderBrushes();
            _colorDriftTimer.Start();
        }

        private void StartSnapBorderAnimations()
        {
            if (_isBorderAnimationRunning || _snapBorderVisual is null || _borderOpacityAnimation is null || _borderScaleAnimation is null)
            {
                return;
            }

            var center = new Vector3((float)(SnapBorderRectangle.Width / 2.0), (float)(SnapBorderRectangle.Height / 2.0), 0f);
            _snapBorderVisual.CenterPoint = center;
            if (_snapBorderGlowVisual is not null)
            {
                _snapBorderGlowVisual.CenterPoint = center;
            }
            if (_snapBorderChaseVisual is not null)
            {
                _snapBorderChaseVisual.CenterPoint = center;
            }
            if (_snapBorderCornerGlowVisual is not null)
            {
                _snapBorderCornerGlowVisual.CenterPoint = center;
            }

            _snapBorderVisual.StartAnimation("Opacity", _borderOpacityAnimation);
            _snapBorderVisual.StartAnimation("Scale", _borderScaleAnimation);
            if (_snapBorderGlowVisual is not null && _borderGlowOpacityAnimation is not null && _borderGlowScaleAnimation is not null)
            {
                _snapBorderGlowVisual.StartAnimation("Opacity", _borderGlowOpacityAnimation);
                _snapBorderGlowVisual.StartAnimation("Scale", _borderGlowScaleAnimation);
            }

            if (_snapBorderChaseVisual is not null && _borderChaseOpacityAnimation is not null && _borderChaseScaleAnimation is not null)
            {
                _snapBorderChaseVisual.StartAnimation("Opacity", _borderChaseOpacityAnimation);
                _snapBorderChaseVisual.StartAnimation("Scale", _borderChaseScaleAnimation);
            }

            if (_snapBorderCornerGlowVisual is not null && _borderChaseOpacityAnimation is not null)
            {
                _snapBorderCornerGlowVisual.StartAnimation("Opacity", _borderChaseOpacityAnimation);
            }

            _isBorderAnimationRunning = true;
        }

        private void StopSnapBorderAnimations()
        {
            if (_snapBorderVisual is null)
            {
                return;
            }

            _snapBorderVisual.StopAnimation("Opacity");
            _snapBorderVisual.StopAnimation("Scale");
            _snapBorderVisual.Opacity = 1f;
            _snapBorderVisual.Scale = Vector3.One;
            if (_snapBorderGlowVisual is not null)
            {
                _snapBorderGlowVisual.StopAnimation("Opacity");
                _snapBorderGlowVisual.StopAnimation("Scale");
                _snapBorderGlowVisual.Opacity = 1f;
                _snapBorderGlowVisual.Scale = Vector3.One;
            }

            if (_snapBorderChaseVisual is not null)
            {
                _snapBorderChaseVisual.StopAnimation("Opacity");
                _snapBorderChaseVisual.StopAnimation("Scale");
                _snapBorderChaseVisual.Opacity = 1f;
                _snapBorderChaseVisual.Scale = Vector3.One;
            }

            if (_snapBorderCornerGlowVisual is not null)
            {
                _snapBorderCornerGlowVisual.StopAnimation("Opacity");
                _snapBorderCornerGlowVisual.Opacity = 1f;
            }

            _isBorderAnimationRunning = false;
            SnapBorderRectangle.Stroke = _snapBorderGradientBrush;
            SnapBorderChaseRectangle.Stroke = _snapBorderChaseGradientBrush;
            SnapBorderGlowRectangle.Stroke = _snapBorderGlowGradientBrush;
            SnapBorderCornerGlowRectangle.Stroke = _snapBorderCornerGlowBrush;
        }

        private void ColorDriftTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            var deltaSeconds = sender.Interval.TotalSeconds;
            _borderEffectElapsedSeconds += deltaSeconds;
            _crosshairAccentElapsedSeconds += deltaSeconds;
            UpdateAnimatedBorderBrushes();
            UpdateTravelingHighlight(deltaSeconds);

            var crosshairBlend = (Math.Sin(_crosshairAccentElapsedSeconds * 1.75) + 1.0) / 2.0;
            var crosshairColor = LerpColor(_crosshairBaseBrush.Color, _crosshairAccentBrush.Color, crosshairBlend);
            _crosshairAnimatedBrush.Color = crosshairColor;
        }

        private void UpdateSnapBorderLayers(int localX, int localY, int width, int height)
        {
            ApplyRectangleGeometry(SnapBorderRectangle, localX, localY, width, height);
            ApplyRectangleGeometry(SnapBorderChaseRectangle, localX, localY, width, height);
            ApplyRectangleGeometry(SnapBorderCornerGlowRectangle, localX, localY, width, height);
            ApplyRectangleGeometry(
                SnapBorderGlowRectangle,
                localX - _borderFxProfile.GlowPadding,
                localY - _borderFxProfile.GlowPadding,
                width + (_borderFxProfile.GlowPadding * 2),
                height + (_borderFxProfile.GlowPadding * 2));
        }

        private static void ApplyRectangleGeometry(FrameworkElement rectangle, double x, double y, double width, double height)
        {
            Canvas.SetLeft(rectangle, x);
            Canvas.SetTop(rectangle, y);
            rectangle.Width = Math.Max(1, width);
            rectangle.Height = Math.Max(1, height);
        }

        private void SetSnapBorderLayersVisible(bool isVisible)
        {
            var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            SnapBorderRectangle.Visibility = visibility;
            SnapBorderChaseRectangle.Visibility = visibility;
            SnapBorderCornerGlowRectangle.Visibility = visibility;
            SnapBorderGlowRectangle.Visibility = visibility;
        }

        private void UpdateTravelingHighlight(double deltaSeconds)
        {
            if (!_isBorderAnimationRunning)
            {
                return;
            }

            var dashDelta = _currentDashSpeedUnitsPerSecond * deltaSeconds;
            SnapBorderChaseRectangle.StrokeDashOffset = SnapBorderChaseRectangle.StrokeDashOffset - dashDelta;
            SnapBorderCornerGlowRectangle.StrokeDashOffset = SnapBorderCornerGlowRectangle.StrokeDashOffset + (dashDelta * 0.6);
        }

        private void UpdateAnimatedBorderBrushes()
        {
            var hueBase = (_borderEffectElapsedSeconds * _borderFxProfile.HueShiftDegreesPerSecond) % 360.0;
            var drift = (_borderEffectElapsedSeconds * _borderFxProfile.DriftSpeed) % 1.0;

            _snapBorderGradientBrush.StartPoint = new Point(drift, 0);
            _snapBorderGradientBrush.EndPoint = new Point(1.0 - drift, 1.0);

            var chaseDrift = (_borderEffectElapsedSeconds * (_borderFxProfile.DriftSpeed * 1.4)) % 1.0;
            _snapBorderChaseGradientBrush.StartPoint = new Point(0, chaseDrift);
            _snapBorderChaseGradientBrush.EndPoint = new Point(1.0, 1.0 - chaseDrift);

            var glowDrift = (_borderEffectElapsedSeconds * (_borderFxProfile.DriftSpeed * 0.55)) % 1.0;
            _snapBorderGlowGradientBrush.StartPoint = new Point(glowDrift, 0);
            _snapBorderGlowGradientBrush.EndPoint = new Point(1.0, 1.0 - glowDrift);

            for (var i = 0; i < _snapBorderGradientStops.Length; i++)
            {
                var hue = hueBase + _activePaletteHueOffsets[i % _activePaletteHueOffsets.Length];
                _snapBorderGradientStops[i].Color = ColorFromHsv(hue, 0.74, 1.0, 255);
                _snapBorderGlowGradientStops[i].Color = ColorFromHsv(hue + 18.0, 0.62, 1.0, 120);
            }

            _snapBorderChaseGradientStops[0].Color = Color.FromArgb(0, 255, 255, 255);
            _snapBorderChaseGradientStops[1].Color = ColorFromHsv(hueBase + _activePaletteHueOffsets[1 % _activePaletteHueOffsets.Length], 0.25, 1.0, 96);
            _snapBorderChaseGradientStops[2].Color = Color.FromArgb(255, 255, 255, 255);
            _snapBorderChaseGradientStops[3].Color = ColorFromHsv(hueBase + _activePaletteHueOffsets[3 % _activePaletteHueOffsets.Length], 0.35, 1.0, 180);
            _snapBorderChaseGradientStops[4].Color = Color.FromArgb(0, 255, 255, 255);

            var cornerGlowAlpha = (byte)(120 + (Math.Sin(_borderEffectElapsedSeconds * 4.8) * 60.0));
            _snapBorderCornerGlowBrush.Color = ColorFromHsv(hueBase + _activePaletteHueOffsets[4 % _activePaletteHueOffsets.Length], 0.45, 1.0, cornerGlowAlpha);
        }

        private void PickNextBorderPalette()
        {
            if (BorderPaletteHueOffsets.Length == 0)
            {
                _activePaletteHueOffsets = new[] { 0.0, 45.0, 95.0, 165.0, 245.0 };
                return;
            }

            if (BorderPaletteHueOffsets.Length == 1)
            {
                _activePaletteIndex = 0;
                _activePaletteHueOffsets = BorderPaletteHueOffsets[0];
                return;
            }

            var nextIndex = _activePaletteIndex;
            while (nextIndex == _activePaletteIndex)
            {
                nextIndex = _random.Next(BorderPaletteHueOffsets.Length);
            }

            _activePaletteIndex = nextIndex;
            _activePaletteHueOffsets = BorderPaletteHueOffsets[nextIndex];
        }

        private void UpdateChaseSpeedForBounds(RectInt32 bounds)
        {
            var perimeter = (bounds.Width * 2.0) + (bounds.Height * 2.0);
            var normalizedSize = Math.Clamp((perimeter - 240.0) / 3200.0, 0.0, 1.0);
            var speedScale = 0.75 + (normalizedSize * 0.95);
            _currentDashSpeedUnitsPerSecond = _borderFxProfile.DashSpeedUnitsPerSecond * speedScale;
        }

        private static BorderFxProfile CreateBorderFxProfile(ScreenshotBorderIntensity intensity)
        {
            return intensity switch
            {
                ScreenshotBorderIntensity.Subtle => new BorderFxProfile(
                    BorderPulseMaxScale: 1.0025,
                    GlowPulseMaxScale: 1.003,
                    ChasePulseMaxScale: 1.0042,
                    GlowPadding: 1.0,
                    HueShiftDegreesPerSecond: BaseBorderHueShiftDegreesPerSecond * 0.78,
                    DashSpeedUnitsPerSecond: BaseBorderDashSpeedUnitsPerSecond * 0.82,
                    DriftSpeed: BaseBorderDriftSpeed * 0.7,
                    BorderStrokeThickness: BaseBorderStrokeThickness * 0.9,
                    ChaseStrokeThickness: BaseChaseStrokeThickness * 0.85,
                    CornerGlowStrokeThickness: BaseCornerGlowStrokeThickness * 0.8,
                    OuterGlowStrokeThickness: BaseOuterGlowStrokeThickness * 0.72,
                    BorderOpacityLow: 0.76,
                    BorderOpacityHigh: 0.93,
                    GlowOpacityLow: 0.06,
                    GlowOpacityHigh: 0.15,
                    ChaseOpacityLow: 0.34,
                    ChaseOpacityHigh: 0.74),
                ScreenshotBorderIntensity.Bold => new BorderFxProfile(
                    BorderPulseMaxScale: 1.006,
                    GlowPulseMaxScale: 1.0072,
                    ChasePulseMaxScale: 1.009,
                    GlowPadding: 3.2,
                    HueShiftDegreesPerSecond: BaseBorderHueShiftDegreesPerSecond * 1.2,
                    DashSpeedUnitsPerSecond: BaseBorderDashSpeedUnitsPerSecond * 1.24,
                    DriftSpeed: BaseBorderDriftSpeed * 1.2,
                    BorderStrokeThickness: BaseBorderStrokeThickness * 1.14,
                    ChaseStrokeThickness: BaseChaseStrokeThickness * 1.16,
                    CornerGlowStrokeThickness: BaseCornerGlowStrokeThickness * 1.1,
                    OuterGlowStrokeThickness: BaseOuterGlowStrokeThickness * 1.22,
                    BorderOpacityLow: 0.9,
                    BorderOpacityHigh: 1.0,
                    GlowOpacityLow: 0.13,
                    GlowOpacityHigh: 0.29,
                    ChaseOpacityLow: 0.58,
                    ChaseOpacityHigh: 1.0),
                _ => new BorderFxProfile(
                    BorderPulseMaxScale: BaseBorderPulseMaxScale,
                    GlowPulseMaxScale: BaseBorderGlowPulseMaxScale,
                    ChasePulseMaxScale: BaseBorderChasePulseMaxScale,
                    GlowPadding: BaseBorderGlowPadding,
                    HueShiftDegreesPerSecond: BaseBorderHueShiftDegreesPerSecond,
                    DashSpeedUnitsPerSecond: BaseBorderDashSpeedUnitsPerSecond,
                    DriftSpeed: BaseBorderDriftSpeed,
                    BorderStrokeThickness: BaseBorderStrokeThickness,
                    ChaseStrokeThickness: BaseChaseStrokeThickness,
                    CornerGlowStrokeThickness: BaseCornerGlowStrokeThickness,
                    OuterGlowStrokeThickness: BaseOuterGlowStrokeThickness,
                    BorderOpacityLow: 0.84,
                    BorderOpacityHigh: 1.0,
                    GlowOpacityLow: 0.1,
                    GlowOpacityHigh: 0.22,
                    ChaseOpacityLow: 0.48,
                    ChaseOpacityHigh: 0.95)
            };
        }

        private static (LinearGradientBrush Brush, GradientStop[] Stops) CreateAnimatedGradientBrush()
        {
            var stops = new[]
            {
                new GradientStop { Offset = 0.00, Color = Color.FromArgb(255, 255, 64, 129) },
                new GradientStop { Offset = 0.25, Color = Color.FromArgb(255, 255, 171, 64) },
                new GradientStop { Offset = 0.50, Color = Color.FromArgb(255, 255, 238, 88) },
                new GradientStop { Offset = 0.75, Color = Color.FromArgb(255, 102, 187, 106) },
                new GradientStop { Offset = 1.00, Color = Color.FromArgb(255, 66, 165, 245) }
            };

            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };

            foreach (var stop in stops)
            {
                brush.GradientStops.Add(stop);
            }

            return (brush, stops);
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

        private static Color ColorFromHsv(double hue, double saturation, double value, byte alpha)
        {
            var wrappedHue = ((hue % 360.0) + 360.0) % 360.0;
            var clampedSaturation = Math.Clamp(saturation, 0.0, 1.0);
            var clampedValue = Math.Clamp(value, 0.0, 1.0);

            var chroma = clampedValue * clampedSaturation;
            var hueSegment = wrappedHue / 60.0;
            var x = chroma * (1.0 - Math.Abs((hueSegment % 2.0) - 1.0));
            var m = clampedValue - chroma;

            (double rPrime, double gPrime, double bPrime) = hueSegment switch
            {
                < 1.0 => (chroma, x, 0.0),
                < 2.0 => (x, chroma, 0.0),
                < 3.0 => (0.0, chroma, x),
                < 4.0 => (0.0, x, chroma),
                < 5.0 => (x, 0.0, chroma),
                _ => (chroma, 0.0, x)
            };

            var r = (byte)Math.Round((rPrime + m) * 255.0);
            var g = (byte)Math.Round((gPrime + m) * 255.0);
            var b = (byte)Math.Round((bPrime + m) * 255.0);
            return Color.FromArgb(alpha, r, g, b);
        }

        private AppWindow GetAppWindowForCurrentWindow()
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_windowHandle);
            return AppWindow.GetFromWindowId(windowId);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            nint hWnd,
            nint hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(nint hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

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

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern nint LoadCursor(nint hInstance, nint lpCursorName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern nint SetCursor(nint hCursor);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out PointStruct lpPoint);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
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

    internal readonly record struct BorderFxProfile(
        double BorderPulseMaxScale,
        double GlowPulseMaxScale,
        double ChasePulseMaxScale,
        double GlowPadding,
        double HueShiftDegreesPerSecond,
        double DashSpeedUnitsPerSecond,
        double DriftSpeed,
        double BorderStrokeThickness,
        double ChaseStrokeThickness,
        double CornerGlowStrokeThickness,
        double OuterGlowStrokeThickness,
        double BorderOpacityLow,
        double BorderOpacityHigh,
        double GlowOpacityLow,
        double GlowOpacityHigh,
        double ChaseOpacityLow,
        double ChaseOpacityHigh);
}
