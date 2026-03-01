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
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace helvety.screenshots.Capture
{
    internal sealed partial class SelectionOverlayWindow : Window
    {
        private const int IdcArrow = 32512;
        private const int HwndTopmost = -1;
        private const uint SwpNomove = 0x0002;
        private const uint SwpNosize = 0x0001;
        private const uint SwpShowwindow = 0x0040;
        private const int DragThresholdPixels = 3;
        private const double InstructionPanelMargin = 24;
        private const double InstructionPanelProximityPadding = 72;
        private const int InstructionPanelMoveCooldownMilliseconds = 220;
        private const double BorderPulseMaxScale = 1.006;

        private readonly FreezeFrame _freezeFrame;
        private readonly WindowSnapHitTester _hitTester;
        private readonly SolidColorBrush _snapBorderBaseBrush = new(Color.FromArgb(255, 255, 59, 48));
        private readonly SolidColorBrush _snapBorderAccentBrush = new(Color.FromArgb(255, 255, 110, 72));
        private readonly SolidColorBrush _crosshairBaseBrush = new(Color.FromArgb(255, 138, 138, 138));
        private readonly SolidColorBrush _crosshairAccentBrush = new(Color.FromArgb(255, 190, 190, 190));
        private TaskCompletionSource<SelectionAction> _selectionCompletionSource = new();
        private readonly nint _windowHandle;
        private readonly DispatcherQueueTimer _colorDriftTimer;
        private Compositor? _compositor;
        private Visual? _snapBorderVisual;
        private Visual? _verticalGuideVisual;
        private Visual? _horizontalGuideVisual;
        private Visual? _instructionStatusCardVisual;
        private ScalarKeyFrameAnimation? _borderOpacityAnimation;
        private Vector3KeyFrameAnimation? _borderScaleAnimation;
        private ScalarKeyFrameAnimation? _crosshairOpacityAnimation;
        private Vector3KeyFrameAnimation? _statusToastUpAnimation;
        private Vector3KeyFrameAnimation? _statusToastDownAnimation;
        private ScalarKeyFrameAnimation? _statusToastOpacityAnimation;
        private bool _isSessionStarted;
        private bool _isPointerDown;
        private bool _isDragging;
        private bool _isBorderAnimationRunning;
        private bool _useAccentColorPhase;
        private bool _statusToastDirectionDown;
        private SelectionCommitMode _activeCommitMode = SelectionCommitMode.LeftCommitExit;
        private OverlayCorner _instructionPanelCorner = OverlayCorner.TopRight;
        private long _lastPanelCornerMoveAt;
        private Point _dragStartLocal;
        private Point _currentLocal;
        private RectInt32? _activeSnapBounds;

        public SelectionOverlayWindow(FreezeFrame freezeFrame, WindowSnapHitTester hitTester)
        {
            _freezeFrame = freezeFrame;
            _hitTester = hitTester;
            InitializeComponent();
            SnapBorderRectangle.Stroke = _snapBorderBaseBrush;
            VerticalCursorGuide.Stroke = _crosshairBaseBrush;
            HorizontalCursorGuide.Stroke = _crosshairBaseBrush;
            EnsureArrowCursor();
            _colorDriftTimer = DispatcherQueue.CreateTimer();
            _colorDriftTimer.Interval = TimeSpan.FromMilliseconds(900);
            _colorDriftTimer.Tick += ColorDriftTimer_Tick;

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
                RootGrid.Focus(FocusState.Programmatic);
                _isSessionStarted = true;
            }

            if (_selectionCompletionSource.Task.IsCompleted)
            {
                _selectionCompletionSource = new TaskCompletionSource<SelectionAction>();
            }

            InitializeSnapAtCurrentCursor();
            RelocateInstructionPanelIfNeeded(_currentLocal);
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

            OverlayCanvas.Width = _freezeFrame.VirtualBounds.Width;
            OverlayCanvas.Height = _freezeFrame.VirtualBounds.Height;
            UpdateCursorGuides(new Point(OverlayCanvas.Width / 2, OverlayCanvas.Height / 2));
            HideWindowDimming();
            PlaceInstructionPanel(_instructionPanelCorner);
            UpdateInstructionStatus("Waiting for selection...");

            SetWindowPos(_windowHandle, (nint)HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpShowwindow);
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
            RelocateInstructionPanelIfNeeded(point.Position);

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
                    SnapBorderRectangle.Visibility = Visibility.Collapsed;
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
            UpdateSnapBounds(clampedPoint);
            RelocateInstructionPanelIfNeeded(clampedPoint);
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
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            InstructionStatusText.Text = $"{message} ({timestamp})";
            PlayStatusToastAnimation();
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

        private void UpdateSnapBounds(Point localPoint)
        {
            var screenX = _freezeFrame.VirtualBounds.X + (int)Math.Round(localPoint.X);
            var screenY = _freezeFrame.VirtualBounds.Y + (int)Math.Round(localPoint.Y);
            if (!_hitTester.TryGetSnapBoundsAt(screenX, screenY, _windowHandle, out var bounds))
            {
                _activeSnapBounds = null;
                SnapBorderRectangle.Visibility = Visibility.Collapsed;
                HideWindowDimming();
                StopSnapBorderAnimations();
                return;
            }

            _activeSnapBounds = bounds;
            var localX = bounds.X - _freezeFrame.VirtualBounds.X;
            var localY = bounds.Y - _freezeFrame.VirtualBounds.Y;

            Canvas.SetLeft(SnapBorderRectangle, localX);
            Canvas.SetTop(SnapBorderRectangle, localY);
            SnapBorderRectangle.Width = bounds.Width;
            SnapBorderRectangle.Height = bounds.Height;
            SnapBorderRectangle.Visibility = Visibility.Visible;
            UpdateWindowDimming(localX, localY, bounds.Width, bounds.Height);
            if (_snapBorderVisual is not null)
            {
                _snapBorderVisual.CenterPoint = new Vector3((float)(SnapBorderRectangle.Width / 2.0), (float)(SnapBorderRectangle.Height / 2.0), 0f);
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
            var localLeft = (int)Math.Round(Math.Min(a.X, b.X));
            var localTop = (int)Math.Round(Math.Min(a.Y, b.Y));
            var localRight = (int)Math.Round(Math.Max(a.X, b.X));
            var localBottom = (int)Math.Round(Math.Max(a.Y, b.Y));
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
            InitializeSnapAtCurrentCursor();
            UpdateInstructionStatus("Ready for next capture...");
        }

        private void PlaceInstructionPanel(OverlayCorner corner)
        {
            EnsureInstructionPanelMeasured();
            var bounds = GetPanelBoundsForCorner(corner);
            Canvas.SetLeft(InstructionPanel, bounds.X);
            Canvas.SetTop(InstructionPanel, bounds.Y);
            _instructionPanelCorner = corner;
        }

        private void EnsureInstructionPanelMeasured()
        {
            if (InstructionPanel.ActualWidth > 0 && InstructionPanel.ActualHeight > 0)
            {
                return;
            }

            InstructionPanel.Measure(new Size(OverlayCanvas.Width, OverlayCanvas.Height));
        }

        private Rect GetPanelBoundsForCorner(OverlayCorner corner)
        {
            var width = InstructionPanel.ActualWidth > 0 ? InstructionPanel.ActualWidth : InstructionPanel.DesiredSize.Width;
            var height = InstructionPanel.ActualHeight > 0 ? InstructionPanel.ActualHeight : InstructionPanel.DesiredSize.Height;
            var left = InstructionPanelMargin;
            var top = InstructionPanelMargin;
            var right = Math.Max(InstructionPanelMargin, OverlayCanvas.Width - width - InstructionPanelMargin);
            var bottom = Math.Max(InstructionPanelMargin, OverlayCanvas.Height - height - InstructionPanelMargin);

            return corner switch
            {
                OverlayCorner.TopLeft => new Rect(left, top, width, height),
                OverlayCorner.TopRight => new Rect(right, top, width, height),
                OverlayCorner.BottomLeft => new Rect(left, bottom, width, height),
                _ => new Rect(right, bottom, width, height)
            };
        }

        private void RelocateInstructionPanelIfNeeded(Point localPointer)
        {
            var now = Environment.TickCount64;
            if (now - _lastPanelCornerMoveAt < InstructionPanelMoveCooldownMilliseconds)
            {
                return;
            }

            var panelBounds = GetPanelBoundsForCorner(_instructionPanelCorner);
            var expandedBounds = new Rect(
                panelBounds.X - InstructionPanelProximityPadding,
                panelBounds.Y - InstructionPanelProximityPadding,
                panelBounds.Width + (InstructionPanelProximityPadding * 2),
                panelBounds.Height + (InstructionPanelProximityPadding * 2));

            if (!expandedBounds.Contains(localPointer))
            {
                return;
            }

            var bestCorner = _instructionPanelCorner;
            var bestDistance = double.MinValue;
            foreach (var candidateCorner in new[] { OverlayCorner.TopLeft, OverlayCorner.TopRight, OverlayCorner.BottomLeft, OverlayCorner.BottomRight })
            {
                if (candidateCorner == _instructionPanelCorner)
                {
                    continue;
                }

                var candidateBounds = GetPanelBoundsForCorner(candidateCorner);
                var centerX = candidateBounds.X + (candidateBounds.Width / 2);
                var centerY = candidateBounds.Y + (candidateBounds.Height / 2);
                var deltaX = localPointer.X - centerX;
                var deltaY = localPointer.Y - centerY;
                var distance = (deltaX * deltaX) + (deltaY * deltaY);
                if (distance <= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestCorner = candidateCorner;
            }

            if (bestCorner == _instructionPanelCorner)
            {
                return;
            }

            PlaceInstructionPanel(bestCorner);
            _lastPanelCornerMoveAt = now;
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
            if (!_selectionCompletionSource.Task.IsCompleted)
            {
                _selectionCompletionSource.TrySetResult(new SelectionAction(SelectionCommitMode.Cancel, null));
            }
        }

        private void InitializeCompositionAnimations()
        {
            _compositor = ElementCompositionPreview.GetElementVisual(RootGrid).Compositor;
            _snapBorderVisual = ElementCompositionPreview.GetElementVisual(SnapBorderRectangle);
            _verticalGuideVisual = ElementCompositionPreview.GetElementVisual(VerticalCursorGuide);
            _horizontalGuideVisual = ElementCompositionPreview.GetElementVisual(HorizontalCursorGuide);
            _instructionStatusCardVisual = ElementCompositionPreview.GetElementVisual(InstructionStatusCard);

            _borderOpacityAnimation = _compositor.CreateScalarKeyFrameAnimation();
            _borderOpacityAnimation.InsertKeyFrame(0.0f, 0.82f);
            _borderOpacityAnimation.InsertKeyFrame(0.5f, 0.96f);
            _borderOpacityAnimation.InsertKeyFrame(1.0f, 0.82f);
            _borderOpacityAnimation.Duration = TimeSpan.FromMilliseconds(1800);
            _borderOpacityAnimation.IterationBehavior = AnimationIterationBehavior.Forever;

            _borderScaleAnimation = _compositor.CreateVector3KeyFrameAnimation();
            _borderScaleAnimation.InsertKeyFrame(0.0f, Vector3.One);
            _borderScaleAnimation.InsertKeyFrame(0.5f, new Vector3((float)BorderPulseMaxScale, (float)BorderPulseMaxScale, 1f));
            _borderScaleAnimation.InsertKeyFrame(1.0f, Vector3.One);
            _borderScaleAnimation.Duration = TimeSpan.FromMilliseconds(1800);
            _borderScaleAnimation.IterationBehavior = AnimationIterationBehavior.Forever;

            _crosshairOpacityAnimation = _compositor.CreateScalarKeyFrameAnimation();
            _crosshairOpacityAnimation.InsertKeyFrame(0.0f, 0.62f);
            _crosshairOpacityAnimation.InsertKeyFrame(0.5f, 0.88f);
            _crosshairOpacityAnimation.InsertKeyFrame(1.0f, 0.62f);
            _crosshairOpacityAnimation.Duration = TimeSpan.FromMilliseconds(1850);
            _crosshairOpacityAnimation.IterationBehavior = AnimationIterationBehavior.Forever;

            _statusToastUpAnimation = _compositor.CreateVector3KeyFrameAnimation();
            _statusToastUpAnimation.InsertKeyFrame(0.0f, new Vector3(0f, 12f, 0f));
            _statusToastUpAnimation.InsertKeyFrame(1.0f, Vector3.Zero);
            _statusToastUpAnimation.Duration = TimeSpan.FromMilliseconds(260);

            _statusToastDownAnimation = _compositor.CreateVector3KeyFrameAnimation();
            _statusToastDownAnimation.InsertKeyFrame(0.0f, new Vector3(0f, -12f, 0f));
            _statusToastDownAnimation.InsertKeyFrame(1.0f, Vector3.Zero);
            _statusToastDownAnimation.Duration = TimeSpan.FromMilliseconds(260);

            _statusToastOpacityAnimation = _compositor.CreateScalarKeyFrameAnimation();
            _statusToastOpacityAnimation.InsertKeyFrame(0.0f, 0.78f);
            _statusToastOpacityAnimation.InsertKeyFrame(1.0f, 1.0f);
            _statusToastOpacityAnimation.Duration = TimeSpan.FromMilliseconds(260);

            _verticalGuideVisual?.StartAnimation("Opacity", _crosshairOpacityAnimation);
            _horizontalGuideVisual?.StartAnimation("Opacity", _crosshairOpacityAnimation);
            _colorDriftTimer.Start();
        }

        private void PlayStatusToastAnimation()
        {
            if (_instructionStatusCardVisual is null || _statusToastOpacityAnimation is null)
            {
                return;
            }

            _statusToastDirectionDown = !_statusToastDirectionDown;
            var offsetAnimation = _statusToastDirectionDown ? _statusToastDownAnimation : _statusToastUpAnimation;
            if (offsetAnimation is not null)
            {
                _instructionStatusCardVisual.StartAnimation("Offset", offsetAnimation);
            }

            _instructionStatusCardVisual.StartAnimation("Opacity", _statusToastOpacityAnimation);
        }

        private void StartSnapBorderAnimations()
        {
            if (_isBorderAnimationRunning || _snapBorderVisual is null || _borderOpacityAnimation is null || _borderScaleAnimation is null)
            {
                return;
            }

            _snapBorderVisual.CenterPoint = new Vector3((float)(SnapBorderRectangle.Width / 2.0), (float)(SnapBorderRectangle.Height / 2.0), 0f);
            _snapBorderVisual.StartAnimation("Opacity", _borderOpacityAnimation);
            _snapBorderVisual.StartAnimation("Scale", _borderScaleAnimation);
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
            _isBorderAnimationRunning = false;
            SnapBorderRectangle.Stroke = _snapBorderBaseBrush;
        }

        private void ColorDriftTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            _useAccentColorPhase = !_useAccentColorPhase;
            var crosshairBrush = _useAccentColorPhase ? _crosshairAccentBrush : _crosshairBaseBrush;
            VerticalCursorGuide.Stroke = crosshairBrush;
            HorizontalCursorGuide.Stroke = crosshairBrush;
            SnapBorderRectangle.Stroke = _isBorderAnimationRunning && _useAccentColorPhase
                ? _snapBorderAccentBrush
                : _snapBorderBaseBrush;
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

    internal enum OverlayCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }
}
