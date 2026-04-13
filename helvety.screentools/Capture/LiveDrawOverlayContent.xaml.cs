using System;
using System.Threading.Tasks;
using helvety.screentools.Editor;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace helvety.screentools.Capture
{
    /// <summary>
    /// Live Draw vector overlay: full virtual-screen <see cref="UserControl"/> with a transparent root hosted inside
    /// <see cref="LiveDrawNativeHost"/> (a <c>WS_EX_NOREDIRECTIONBITMAP</c> window); ink renders above the desktop.
    /// Left mouse: freehand when no shape modifier matches; rectangle, arrow, and straight line use the modifiers set in Settings.
    /// Right mouse: circle or ellipse with the chosen modifiers; plain right hold is the sparkle animation (no modifier).
    /// </summary>
    internal sealed partial class LiveDrawOverlayContent : UserControl
    {
        private const double LiveDrawArrowSizeScale = 2.0;
        private const double LiveDrawFreeDrawMinCommitPathLengthDip = 2.0;
        private const double LiveDrawShapeMinCommitSizeDip = 2.0;

        private readonly RectInt32 _virtualBounds;
        private readonly SnapBorderChromeController _snapBorderChrome;
        private EventHandler<object>? _activationRenderingHandler;
        private TaskCompletionSource<bool> _sessionCompletion = new();
        private LiveDrawTool _activeTool;
        private Point _pointerDownLocal;
        private Polyline? _currentPolyline;
        private PointCollection? _currentMainPoints;
        private PointCollection? _currentChasePoints;
        private PointCollection? _currentCornerPoints;
        private Canvas? _currentFreeDrawContainer;
        private Polyline? _currentFreeDrawChasePolyline;
        private Polyline? _currentFreeDrawCornerPolyline;
        private bool _isPointerDown;
        private ActivePointerKind _activePointerKind;
        private bool _snapBorderCompositionInitialized;
        private bool _activationPulsePlayed;
        private int _liveDrawMainStrokeThicknessDip = 4;
        private bool _liveDrawFreeDrawEnabled = true;
        private bool _liveDrawSparkleEnabled = true;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _driftTimer;
        private LiveDrawNativeHost? _host;

        internal LiveDrawOverlayContent(RectInt32 virtualBounds)
        {
            _virtualBounds = virtualBounds;
            InitializeComponent();

            RootGrid.Width = _virtualBounds.Width;
            RootGrid.Height = _virtualBounds.Height;

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
                BorderCanvas,
                SnapEllipseBorder,
                SnapEllipseChase,
                SnapEllipseCornerGlow,
                SnapEllipseGlow);

            RootGrid.Loaded += RootGrid_Loaded;
            Unloaded += LiveDrawOverlayContent_Unloaded;
        }

        internal event Action? CloseRequested;

        internal void AttachHost(LiveDrawNativeHost host)
        {
            _host = host;
        }

        internal void DetachHost()
        {
            _host = null;
        }

        /// <summary>Ends the session from Win32 <c>WM_KEYDOWN</c> (Esc) when XAML routing does not receive focus.</summary>
        internal void RequestExitFromNative()
        {
            CompleteSession();
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (_snapBorderCompositionInitialized)
            {
                return;
            }

            _snapBorderCompositionInitialized = true;
            _snapBorderChrome.InitializeCompositionAnimations();

            var liveDrawDrawingSettings = SettingsService.LoadLiveDrawDrawingSettings();
            _liveDrawMainStrokeThicknessDip = Math.Max(1, liveDrawDrawingSettings.MainStrokeThickness);
            _liveDrawFreeDrawEnabled = liveDrawDrawingSettings.FreeDrawEnabled;
            _liveDrawSparkleEnabled = liveDrawDrawingSettings.SparkleEnabled;
            var liveDrawMainStroke = _liveDrawMainStrokeThicknessDip * LiveDrawArrowSizeScale;
            _snapBorderChrome.ApplyLiveDrawStrokeThickness(liveDrawMainStroke);

            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            _driftTimer = dq.CreateTimer();
            _driftTimer.Interval = TimeSpan.FromMilliseconds(70);
            _driftTimer.Tick += (_, _) => _snapBorderChrome.OnColorDriftTick(0.07);
            _driftTimer.Start();
        }

        private void LiveDrawOverlayContent_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_activationRenderingHandler is not null)
            {
                CompositionTarget.Rendering -= _activationRenderingHandler;
                _activationRenderingHandler = null;
            }

            _driftTimer?.Stop();
            _driftTimer = null;
            _snapBorderChrome.StopClickSparkleHold();
        }

        internal async Task PrepareVisibleSessionAsync()
        {
            await WaitForRootGridLoadedAsync().ConfigureAwait(true);
            await WaitForOneLayoutPassAsync().ConfigureAwait(true);
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

        internal Task RunSessionAsync()
        {
            var activationCompleted = false;
            _activationPulsePlayed = false;

            void CompleteActivation()
            {
                if (activationCompleted)
                {
                    return;
                }

                activationCompleted = true;
                RootGrid.Focus(FocusState.Programmatic);
                PlayActivationPulseAtCursorOnce();
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
            if (point.Properties.IsRightButtonPressed)
            {
                HandleRightPointerPressed(e);
                return;
            }

            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            _isPointerDown = true;
            _activePointerKind = ActivePointerKind.Left;
            _pointerDownLocal = point.Position;
            _ = RootGrid.CapturePointer(e.Pointer);
            _host?.EnsureFocusedForKeyboard();

            var shapeMods = SettingsService.LoadLiveDrawShapeModifiers();
            if (shapeMods.Rectangle != LiveDrawRectangleModifier.None &&
                MatchesLiveDrawModifier(shapeMods.Rectangle, e))
            {
                _activeTool = LiveDrawTool.Rectangle;
            }
            else if (shapeMods.Arrow != LiveDrawRectangleModifier.None &&
                     MatchesLiveDrawModifier(shapeMods.Arrow, e))
            {
                _activeTool = LiveDrawTool.Arrow;
            }
            else if (shapeMods.StraightLine != LiveDrawRectangleModifier.None &&
                     MatchesLiveDrawModifier(shapeMods.StraightLine, e))
            {
                _activeTool = LiveDrawTool.StraightLine;
            }
            else
            {
                if (!_liveDrawFreeDrawEnabled)
                {
                    return;
                }

                _activeTool = LiveDrawTool.FreeDraw;
            }

            _snapBorderChrome.PickNextBorderPalette();
            _snapBorderChrome.ResetDashSpeedToDefault();

            switch (_activeTool)
            {
                case LiveDrawTool.FreeDraw:
                    _currentMainPoints = new PointCollection { _pointerDownLocal };
                    _currentChasePoints = new PointCollection { _pointerDownLocal };
                    _currentCornerPoints = new PointCollection { _pointerDownLocal };
                    var chase = new Polyline();
                    var corner = new Polyline();
                    _currentPolyline = new Polyline();
                    _snapBorderChrome.ConfigureLiveDrawPolylineLayers(
                        _currentPolyline,
                        chase,
                        corner,
                        _currentMainPoints,
                        _currentChasePoints,
                        _currentCornerPoints);
                    _currentFreeDrawChasePolyline = chase;
                    _currentFreeDrawCornerPolyline = corner;
                    _currentFreeDrawContainer = new Canvas
                    {
                        Width = DrawCanvas.Width,
                        Height = DrawCanvas.Height
                    };
                    _currentFreeDrawContainer.Children.Add(corner);
                    _currentFreeDrawContainer.Children.Add(chase);
                    _currentFreeDrawContainer.Children.Add(_currentPolyline);
                    DrawCanvas.Children.Add(_currentFreeDrawContainer);
                    break;

                case LiveDrawTool.Rectangle:
                case LiveDrawTool.Arrow:
                case LiveDrawTool.StraightLine:
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
                    if (_currentMainPoints is not null && _currentChasePoints is not null && _currentCornerPoints is not null)
                    {
                        _currentMainPoints.Add(local);
                        _currentChasePoints.Add(local);
                        _currentCornerPoints.Add(local);
                        if (_currentMainPoints.Count >= 2)
                        {
                            _snapBorderChrome.UpdateChaseSpeedForArrowLength(ComputePolylinePathLength(_currentMainPoints));
                        }
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

                case LiveDrawTool.Circle:
                case LiveDrawTool.Ellipse:
                    {
                        ComputeLiveDrawEllipseBounds(
                            _pointerDownLocal,
                            local,
                            _activeTool == LiveDrawTool.Circle,
                            out var el,
                            out var et,
                            out var ew,
                            out var eh);
                        _snapBorderChrome.UpdateChaseSpeedForPixelSize(ew, eh);
                        _snapBorderChrome.UpdateSnapBorderEllipseLayers(el, et, ew, eh);
                        _snapBorderChrome.SetSnapBorderEllipseLayersVisible(true);
                        _snapBorderChrome.StartSnapBorderEllipseAnimations();
                        break;
                    }

                case LiveDrawTool.Arrow:
                case LiveDrawTool.StraightLine:
                    PreviewCanvas.Children.Clear();
                    var editorSettings = SettingsService.LoadEditorUiSettings();
                    _snapBorderChrome.UpdateChaseSpeedForArrowLength(SegmentLengthDip(_pointerDownLocal, local));
                    if (_activeTool == LiveDrawTool.Arrow)
                    {
                        _snapBorderChrome.DrawSnapChromeArrow(
                            PreviewCanvas,
                            BuildLiveDrawVectorLayer(
                                local,
                                _pointerDownLocal,
                                editorSettings.PrimaryColorHex,
                                _liveDrawMainStrokeThicknessDip,
                                ArrowFormStyle.Straight));
                    }
                    else
                    {
                        _snapBorderChrome.DrawSnapChromeLine(
                            PreviewCanvas,
                            BuildLiveDrawVectorLayer(
                                local,
                                _pointerDownLocal,
                                editorSettings.PrimaryColorHex,
                                _liveDrawMainStrokeThicknessDip,
                                ArrowFormStyle.LineOnly));
                    }

                    break;

                case LiveDrawTool.ClickSparkle:
                    _snapBorderChrome.UpdateClickSparkleHoldPosition(DrawCanvas, local);
                    break;
            }
        }

        private void HandleRightPointerPressed(PointerRoutedEventArgs e)
        {
            var pos = e.GetCurrentPoint(RootGrid).Position;
            var shapeMods = SettingsService.LoadLiveDrawShapeModifiers();
            if (shapeMods.CircleRight != LiveDrawRectangleModifier.None &&
                MatchesLiveDrawModifier(shapeMods.CircleRight, e))
            {
                _activeTool = LiveDrawTool.Circle;
            }
            else if (shapeMods.EllipseRight != LiveDrawRectangleModifier.None &&
                     MatchesLiveDrawModifier(shapeMods.EllipseRight, e))
            {
                _activeTool = LiveDrawTool.Ellipse;
            }
            else
            {
                if (!_liveDrawSparkleEnabled)
                {
                    return;
                }

                _activeTool = LiveDrawTool.ClickSparkle;
                _isPointerDown = true;
                _activePointerKind = ActivePointerKind.Right;
                _pointerDownLocal = pos;
                _ = RootGrid.CapturePointer(e.Pointer);
                _host?.EnsureFocusedForKeyboard();
                _snapBorderChrome.StartClickSparkleHold(DrawCanvas, pos);
                return;
            }

            _isPointerDown = true;
            _activePointerKind = ActivePointerKind.Right;
            _pointerDownLocal = pos;
            _ = RootGrid.CapturePointer(e.Pointer);
            _host?.EnsureFocusedForKeyboard();

            _snapBorderChrome.PickNextBorderPalette();
            _snapBorderChrome.ResetDashSpeedToDefault();
        }

        private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPointerDown)
            {
                return;
            }

            var point = e.GetCurrentPoint(RootGrid);
            var kind = point.Properties.PointerUpdateKind;
            if (_activePointerKind == ActivePointerKind.Left &&
                kind != PointerUpdateKind.LeftButtonReleased)
            {
                return;
            }

            if (_activePointerKind == ActivePointerKind.Right &&
                kind != PointerUpdateKind.RightButtonReleased)
            {
                return;
            }

            _isPointerDown = false;
            _activePointerKind = ActivePointerKind.None;
            RootGrid.ReleasePointerCaptures();

            var local = point.Position;

            switch (_activeTool)
            {
                case LiveDrawTool.ClickSparkle:
                    _snapBorderChrome.StopClickSparkleHold();
                    break;

                case LiveDrawTool.Rectangle:
                    FinishRectangleDrag(local);
                    break;

                case LiveDrawTool.Circle:
                case LiveDrawTool.Ellipse:
                    FinishEllipseOrCircleDrag(local);
                    break;

                case LiveDrawTool.Arrow:
                case LiveDrawTool.StraightLine:
                    {
                        PreviewCanvas.Children.Clear();
                        var releasedSettings = SettingsService.LoadEditorUiSettings();
                        if (_activeTool == LiveDrawTool.Arrow)
                        {
                            _snapBorderChrome.CommitSnapChromeArrowToDrawCanvas(
                                DrawCanvas,
                                BuildLiveDrawVectorLayer(
                                    local,
                                    _pointerDownLocal,
                                    releasedSettings.PrimaryColorHex,
                                    _liveDrawMainStrokeThicknessDip,
                                    ArrowFormStyle.Straight));
                        }
                        else
                        {
                            _snapBorderChrome.CommitSnapChromeLineToDrawCanvas(
                                DrawCanvas,
                                BuildLiveDrawVectorLayer(
                                    local,
                                    _pointerDownLocal,
                                    releasedSettings.PrimaryColorHex,
                                    _liveDrawMainStrokeThicknessDip,
                                    ArrowFormStyle.LineOnly));
                        }

                        break;
                    }

                case LiveDrawTool.FreeDraw:
                    CommitFreeDrawStrokeOnPointerUp();
                    break;
            }

            ClearPendingStrokeFields();
        }

        private void FinishRectangleDrag(Point local)
        {
            var left = Math.Min(_pointerDownLocal.X, local.X);
            var top = Math.Min(_pointerDownLocal.Y, local.Y);
            var w = Math.Abs(local.X - _pointerDownLocal.X);
            var h = Math.Abs(local.Y - _pointerDownLocal.Y);
            if (IsCommitSizedShape(w, h))
            {
                _snapBorderChrome.CommitSnapBorderToDrawCanvas(DrawCanvas, left, top, w, h);
            }

            _snapBorderChrome.StopSnapBorderAnimations();
            _snapBorderChrome.SetSnapBorderLayersVisible(false);
        }

        private void FinishEllipseOrCircleDrag(Point local)
        {
            ComputeLiveDrawEllipseBounds(
                _pointerDownLocal,
                local,
                _activeTool == LiveDrawTool.Circle,
                out var el,
                out var et,
                out var ew,
                out var eh);
            if (IsCommitSizedShape(ew, eh))
            {
                _snapBorderChrome.CommitSnapBorderEllipseToDrawCanvas(DrawCanvas, el, et, ew, eh);
            }

            _snapBorderChrome.StopSnapBorderEllipseAnimations();
            _snapBorderChrome.SetSnapBorderEllipseLayersVisible(false);
        }

        private static bool IsCommitSizedShape(double w, double h) =>
            w > LiveDrawShapeMinCommitSizeDip && h > LiveDrawShapeMinCommitSizeDip;

        private void ClearPendingStrokeFields()
        {
            _currentPolyline = null;
            _currentMainPoints = null;
            _currentChasePoints = null;
            _currentCornerPoints = null;
            _currentFreeDrawContainer = null;
            _currentFreeDrawChasePolyline = null;
            _currentFreeDrawCornerPolyline = null;
        }

        private void RootGrid_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPointerDown)
            {
                return;
            }

            if (_activePointerKind == ActivePointerKind.Left)
            {
                switch (_activeTool)
                {
                    case LiveDrawTool.Rectangle:
                        _snapBorderChrome.StopSnapBorderAnimations();
                        _snapBorderChrome.SetSnapBorderLayersVisible(false);
                        break;
                    case LiveDrawTool.Arrow:
                    case LiveDrawTool.StraightLine:
                        PreviewCanvas.Children.Clear();
                        break;
                    case LiveDrawTool.FreeDraw:
                        if (_currentFreeDrawContainer is not null)
                        {
                            DrawCanvas.Children.Remove(_currentFreeDrawContainer);
                        }

                        break;
                }
            }
            else if (_activePointerKind == ActivePointerKind.Right)
            {
                if (_activeTool == LiveDrawTool.Circle || _activeTool == LiveDrawTool.Ellipse)
                {
                    _snapBorderChrome.StopSnapBorderEllipseAnimations();
                    _snapBorderChrome.SetSnapBorderEllipseLayersVisible(false);
                }
                else if (_activeTool == LiveDrawTool.ClickSparkle)
                {
                    _snapBorderChrome.StopClickSparkleHold();
                }
            }

            _isPointerDown = false;
            _activePointerKind = ActivePointerKind.None;
            RootGrid.ReleasePointerCaptures();

            ClearPendingStrokeFields();
        }

        private void CommitFreeDrawStrokeOnPointerUp()
        {
            if (_currentMainPoints is null ||
                _currentFreeDrawChasePolyline is null ||
                _currentFreeDrawCornerPolyline is null ||
                _currentFreeDrawContainer is null)
            {
                return;
            }

            var len = ComputePolylinePathLength(_currentMainPoints);
            if (_currentMainPoints.Count < 2 || len <= LiveDrawFreeDrawMinCommitPathLengthDip ||
                !TryGetPolylineBoundingCenter(_currentMainPoints, out var cx, out var cy))
            {
                return;
            }

            _snapBorderChrome.UpdateChaseSpeedForArrowLength(len);
            _snapBorderChrome.RegisterCommittedFreeDraw(_currentFreeDrawChasePolyline, _currentFreeDrawCornerPolyline);
            _snapBorderChrome.FinalizeLiveDrawStroke(_currentFreeDrawContainer, cx, cy);
        }

        private static double ComputePolylinePathLength(PointCollection points)
        {
            if (points.Count < 2)
            {
                return 0;
            }

            double sum = 0;
            for (var i = 1; i < points.Count; i++)
            {
                var dx = points[i].X - points[i - 1].X;
                var dy = points[i].Y - points[i - 1].Y;
                sum += Math.Sqrt((dx * dx) + (dy * dy));
            }

            return sum;
        }

        private static bool TryGetPolylineBoundingCenter(PointCollection points, out double cx, out double cy)
        {
            cx = 0;
            cy = 0;
            if (points.Count == 0)
            {
                return false;
            }

            var minX = points[0].X;
            var maxX = points[0].X;
            var minY = points[0].Y;
            var maxY = points[0].Y;
            for (var i = 1; i < points.Count; i++)
            {
                var p = points[i];
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
            }

            cx = (minX + maxX) / 2.0;
            cy = (minY + maxY) / 2.0;
            return true;
        }

        private void PlayActivationPulseAtCursorOnce()
        {
            if (_activationPulsePlayed)
            {
                return;
            }

            if (!TryGetCursorLocalPosition(out var localPoint))
            {
                return;
            }

            _activationPulsePlayed = true;
            _snapBorderChrome.PlayClickSparkle(DrawCanvas, localPoint);
        }

        private bool TryGetCursorLocalPosition(out Point localPoint)
        {
            localPoint = default;
            if (!GetCursorPos(out var cursorScreen))
            {
                return false;
            }

            var x = Math.Clamp(cursorScreen.X - _virtualBounds.X, 0, _virtualBounds.Width);
            var y = Math.Clamp(cursorScreen.Y - _virtualBounds.Y, 0, _virtualBounds.Height);
            localPoint = new Point(x, y);
            return true;
        }

        private static double SegmentLengthDip(Point a, Point b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static void ComputeLiveDrawEllipseBounds(
            Point start,
            Point current,
            bool circle,
            out double left,
            out double top,
            out double w,
            out double h)
        {
            var rawW = Math.Abs(current.X - start.X);
            var rawH = Math.Abs(current.Y - start.Y);
            if (circle)
            {
                var side = Math.Min(rawW, rawH);
                var minX = Math.Min(start.X, current.X);
                var minY = Math.Min(start.Y, current.Y);
                left = minX + (rawW - side) / 2.0;
                top = minY + (rawH - side) / 2.0;
                w = side;
                h = side;
            }
            else
            {
                left = Math.Min(start.X, current.X);
                top = Math.Min(start.Y, current.Y);
                w = rawW;
                h = rawH;
            }
        }

        private static ArrowLayer BuildLiveDrawVectorLayer(
            Point tail,
            Point tip,
            string colorHex,
            double mainStrokeThicknessDip,
            ArrowFormStyle formStyle)
        {
            var scaledThickness = Math.Max(1, mainStrokeThicknessDip) * LiveDrawArrowSizeScale;
            return new ArrowLayer(
                tail.X,
                tail.Y,
                tip.X,
                tip.Y,
                scaledThickness,
                colorHex,
                formStyle)
            {
                HasBorder = false,
                BorderThickness = 0,
                HasShadow = false,
                ShadowColorHex = "#66000000",
                ShadowOffset = Math.Max(1, (int)Math.Round(2 * LiveDrawArrowSizeScale))
            };
        }

        private void CompleteSession()
        {
            if (_sessionCompletion.Task.IsCompleted)
            {
                return;
            }

            _sessionCompletion.TrySetResult(true);
            CloseRequested?.Invoke();
        }

        private const int VkMenu = 0x12;
        private const int VkLMenu = 0xA4;
        private const int VkRMenu = 0xA5;
        private const int VkShift = 0x10;
        private const int VkLShift = 0xA0;
        private const int VkRShift = 0xA1;
        private const int VkControl = 0x11;
        private const int VkLwin = 0x5B;
        private const int VkRwin = 0x5C;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out PointStruct lpPoint);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct PointStruct
        {
            public int X;
            public int Y;
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

        private static bool MatchesLiveDrawModifier(LiveDrawRectangleModifier modifier, PointerRoutedEventArgs? e = null)
        {
            return modifier switch
            {
                LiveDrawRectangleModifier.None => false,
                LiveDrawRectangleModifier.Shift => IsShiftDown(e),
                LiveDrawRectangleModifier.Control => IsControlDown(e),
                LiveDrawRectangleModifier.Win => IsWinDown(e),
                LiveDrawRectangleModifier.Alt => IsAltMenuWithoutCtrl(e),
                _ => false
            };
        }

        private enum ActivePointerKind
        {
            None,
            Left,
            Right
        }

        private enum LiveDrawTool
        {
            FreeDraw,
            Rectangle,
            Arrow,
            StraightLine,
            Circle,
            Ellipse,
            ClickSparkle
        }
    }
}
