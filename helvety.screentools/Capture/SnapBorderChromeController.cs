using System;
using System.Collections.Generic;
using System.Numerics;
using helvety.screentools.Editor;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace helvety.screentools.Capture
{
    internal sealed class SnapBorderChromeController
    {
        private const double BaseBorderPulseMaxScale = 1.004;
        private const double BaseBorderGlowPulseMaxScale = 1.0045;
        private const double BaseBorderChasePulseMaxScale = 1.006;
        private const double BaseBorderGlowPadding = 2.0;
        private const double BaseBorderHueShiftDegreesPerSecond = 22.0;
        private const double BaseBorderDashSpeedUnitsPerSecond = 92.0;
        private const double BaseBorderDriftSpeed = 0.28;
        private const double BaseBorderStrokeThickness = 4.0;
        private const double BaseChaseStrokeThickness = 3.0;
        private const double BaseCornerGlowStrokeThickness = 6.0;
        private const double BaseOuterGlowStrokeThickness = 8.0;

        private static readonly double[][] BorderPaletteHueOffsets =
        {
            new[] { 336.0, 344.0, 352.0, 2.0, 12.0 },
            new[] { 328.0, 336.0, 346.0, 356.0, 8.0 },
            new[] { 322.0, 332.0, 342.0, 354.0, 6.0 },
            new[] { 330.0, 340.0, 350.0, 0.0, 10.0 },
            new[] { 334.0, 344.0, 354.0, 4.0, 14.0 }
        };

        private readonly Grid _rootGrid;
        private readonly Rectangle _snapBorderRectangle;
        private readonly Rectangle _snapBorderChaseRectangle;
        private readonly Rectangle _snapBorderCornerGlowRectangle;
        private readonly Rectangle _snapBorderGlowRectangle;
        private readonly Canvas _overlayCanvas;
        private readonly BorderFxProfile _borderFxProfile;
        private readonly Random _random = new();
        private readonly LinearGradientBrush _snapBorderGradientBrush;
        private readonly LinearGradientBrush _snapBorderChaseGradientBrush;
        private readonly LinearGradientBrush _snapBorderGlowGradientBrush;
        private readonly SolidColorBrush _snapBorderCornerGlowBrush = new(Color.FromArgb(170, 255, 255, 255));
        private readonly GradientStop[] _snapBorderGradientStops;
        private readonly GradientStop[] _snapBorderChaseGradientStops;
        private readonly GradientStop[] _snapBorderGlowGradientStops;

        private Compositor? _compositor;
        private Visual? _snapBorderVisual;
        private Visual? _snapBorderChaseVisual;
        private Visual? _snapBorderCornerGlowVisual;
        private Visual? _snapBorderGlowVisual;
        private readonly Ellipse? _snapEllipseBorder;
        private readonly Ellipse? _snapEllipseChase;
        private readonly Ellipse? _snapEllipseCornerGlow;
        private readonly Ellipse? _snapEllipseGlow;
        private Visual? _snapEllipseBorderVisual;
        private Visual? _snapEllipseChaseVisual;
        private Visual? _snapEllipseCornerGlowVisual;
        private Visual? _snapEllipseGlowVisual;
        private bool _isEllipseBorderAnimationRunning;
        private ScalarKeyFrameAnimation? _borderOpacityAnimation;
        private Vector3KeyFrameAnimation? _borderScaleAnimation;
        private ScalarKeyFrameAnimation? _borderGlowOpacityAnimation;
        private Vector3KeyFrameAnimation? _borderGlowScaleAnimation;
        private ScalarKeyFrameAnimation? _borderChaseOpacityAnimation;
        private Vector3KeyFrameAnimation? _borderChaseScaleAnimation;
        private bool _isBorderAnimationRunning;
        private readonly List<CommittedSnapBorderGroup> _committedGroups = new();
        private readonly List<CommittedSnapEllipseGroup> _committedEllipseGroups = new();
        private readonly List<CommittedSnapArrowGroup> _committedArrowGroups = new();
        private readonly List<CommittedSnapLineGroup> _committedLineGroups = new();
        private readonly List<CommittedFreeDrawGroup> _committedFreeDrawGroups = new();
        private readonly LinearGradientBrush _arrowBorderGradientBrush;
        private readonly GradientStop[] _arrowBorderGradientStops;
        private readonly LinearGradientBrush _arrowChaseGradientBrush;
        private readonly GradientStop[] _arrowChaseGradientStops;
        private readonly LinearGradientBrush _arrowGlowGradientBrush;
        private readonly GradientStop[] _arrowGlowGradientStops;

        private static readonly DoubleCollection ChaseDashPattern = new() { 26, 180 };
        private static readonly DoubleCollection CornerDashPattern = new() { 18, 68, 18, 220 };
        private double _borderEffectElapsedSeconds;
        private double _currentDashSpeedUnitsPerSecond;
        private int _activePaletteIndex;
        private double[] _activePaletteHueOffsets = BorderPaletteHueOffsets[0];

        private const double ClickSparkleSizeDip = 56.0;
        private const int ClickSparkleHoldPulseMs = 640;
        private const int ClickSparkleHoldFallbackTickMs = 50;
        private static readonly float[] ClickSparkleRingSpinDegreesPerSecond = { 132f, -96f, 68f, -42f };
        private Canvas? _clickSparkleHoldDrawCanvas;
        private Canvas? _clickSparkleHoldContainer;
        private Visual? _clickSparkleHoldVisual;
        private readonly List<Ellipse> _clickSparkleHoldRings = new();
        private readonly List<Visual> _clickSparkleHoldRingVisuals = new();
        private readonly List<RotateTransform> _clickSparkleHoldRingFallbackRotates = new();
        private CompositeTransform? _clickSparkleHoldFallbackTransform;
        private DispatcherQueueTimer? _clickSparkleHoldFallbackTimer;
        private double _clickSparkleHoldFallbackPhase;

        private bool HasEllipseLayerTemplates =>
            _snapEllipseBorder is not null &&
            _snapEllipseChase is not null &&
            _snapEllipseCornerGlow is not null &&
            _snapEllipseGlow is not null;

        internal SnapBorderChromeController(
            Grid rootGrid,
            Rectangle snapBorderRectangle,
            Rectangle snapBorderChaseRectangle,
            Rectangle snapBorderCornerGlowRectangle,
            Rectangle snapBorderGlowRectangle,
            Canvas overlayCanvas,
            Ellipse? snapEllipseBorder = null,
            Ellipse? snapEllipseChase = null,
            Ellipse? snapEllipseCornerGlow = null,
            Ellipse? snapEllipseGlow = null)
        {
            _rootGrid = rootGrid;
            _snapBorderRectangle = snapBorderRectangle;
            _snapBorderChaseRectangle = snapBorderChaseRectangle;
            _snapBorderCornerGlowRectangle = snapBorderCornerGlowRectangle;
            _snapBorderGlowRectangle = snapBorderGlowRectangle;
            _overlayCanvas = overlayCanvas;
            _snapEllipseBorder = snapEllipseBorder;
            _snapEllipseChase = snapEllipseChase;
            _snapEllipseCornerGlow = snapEllipseCornerGlow;
            _snapEllipseGlow = snapEllipseGlow;

            var configuredIntensity = SettingsService.Load().ScreenshotBorderIntensity;
            _borderFxProfile = CreateBorderFxProfile(configuredIntensity);
            _currentDashSpeedUnitsPerSecond = _borderFxProfile.DashSpeedUnitsPerSecond;
            (_snapBorderGradientBrush, _snapBorderGradientStops) = CreateAnimatedGradientBrush();
            (_snapBorderChaseGradientBrush, _snapBorderChaseGradientStops) = CreateAnimatedGradientBrush();
            (_snapBorderGlowGradientBrush, _snapBorderGlowGradientStops) = CreateAnimatedGradientBrush();
            (_arrowBorderGradientBrush, _arrowBorderGradientStops) = CreateAnimatedGradientBrush();
            (_arrowChaseGradientBrush, _arrowChaseGradientStops) = CreateAnimatedGradientBrush();
            (_arrowGlowGradientBrush, _arrowGlowGradientStops) = CreateAnimatedGradientBrush();

            _snapBorderRectangle.Stroke = _snapBorderGradientBrush;
            _snapBorderChaseRectangle.Stroke = _snapBorderChaseGradientBrush;
            _snapBorderCornerGlowRectangle.Stroke = _snapBorderCornerGlowBrush;
            _snapBorderGlowRectangle.Stroke = _snapBorderGlowGradientBrush;
            _snapBorderRectangle.StrokeThickness = _borderFxProfile.BorderStrokeThickness;
            _snapBorderChaseRectangle.StrokeThickness = _borderFxProfile.ChaseStrokeThickness;
            _snapBorderCornerGlowRectangle.StrokeThickness = _borderFxProfile.CornerGlowStrokeThickness;
            _snapBorderGlowRectangle.StrokeThickness = _borderFxProfile.OuterGlowStrokeThickness;

            if (HasEllipseLayerTemplates)
            {
                var ellipseBorder = _snapEllipseBorder!;
                var ellipseChase = _snapEllipseChase!;
                var ellipseCornerGlow = _snapEllipseCornerGlow!;
                var ellipseGlow = _snapEllipseGlow!;
                ellipseBorder.Stroke = _snapBorderGradientBrush;
                ellipseChase.Stroke = _snapBorderChaseGradientBrush;
                ellipseCornerGlow.Stroke = _snapBorderCornerGlowBrush;
                ellipseGlow.Stroke = _snapBorderGlowGradientBrush;
                ellipseBorder.StrokeThickness = _borderFxProfile.BorderStrokeThickness;
                ellipseChase.StrokeThickness = _borderFxProfile.ChaseStrokeThickness;
                ellipseCornerGlow.StrokeThickness = _borderFxProfile.CornerGlowStrokeThickness;
                ellipseGlow.StrokeThickness = _borderFxProfile.OuterGlowStrokeThickness;
                ellipseChase.StrokeDashArray = CloneDashArray(ChaseDashPattern);
                ellipseCornerGlow.StrokeDashArray = CloneDashArray(CornerDashPattern);
            }

            PickNextBorderPalette();
        }

        internal void InitializeCompositionAnimations()
        {
            try
            {
                var rootVisual = ElementCompositionPreview.GetElementVisual(_rootGrid);
                if (rootVisual is null)
                {
                    return;
                }

                _compositor = rootVisual.Compositor;
                _snapBorderVisual = ElementCompositionPreview.GetElementVisual(_snapBorderRectangle);
                _snapBorderChaseVisual = ElementCompositionPreview.GetElementVisual(_snapBorderChaseRectangle);
                _snapBorderCornerGlowVisual = ElementCompositionPreview.GetElementVisual(_snapBorderCornerGlowRectangle);
                _snapBorderGlowVisual = ElementCompositionPreview.GetElementVisual(_snapBorderGlowRectangle);

                if (HasEllipseLayerTemplates)
                {
                    _snapEllipseBorderVisual = ElementCompositionPreview.GetElementVisual(_snapEllipseBorder);
                    _snapEllipseChaseVisual = ElementCompositionPreview.GetElementVisual(_snapEllipseChase);
                    _snapEllipseCornerGlowVisual = ElementCompositionPreview.GetElementVisual(_snapEllipseCornerGlow);
                    _snapEllipseGlowVisual = ElementCompositionPreview.GetElementVisual(_snapEllipseGlow);
                }

                _borderOpacityAnimation = CreateBorderOpacityAnimation();
                _borderScaleAnimation = CreateBorderScaleAnimation();
                _borderGlowOpacityAnimation = CreateGlowOpacityAnimation();
                _borderGlowScaleAnimation = CreateGlowScaleAnimation();
                _borderChaseOpacityAnimation = CreateChaseOpacityAnimation();
                _borderChaseScaleAnimation = CreateChaseScaleAnimation();

                UpdateAnimatedBorderBrushes();
            }
            catch
            {
                _compositor = null;
                _snapBorderVisual = null;
                _snapBorderChaseVisual = null;
                _snapBorderCornerGlowVisual = null;
                _snapBorderGlowVisual = null;
                _borderOpacityAnimation = null;
                _borderScaleAnimation = null;
                _borderGlowOpacityAnimation = null;
                _borderGlowScaleAnimation = null;
                _borderChaseOpacityAnimation = null;
                _borderChaseScaleAnimation = null;
                _snapEllipseBorderVisual = null;
                _snapEllipseChaseVisual = null;
                _snapEllipseCornerGlowVisual = null;
                _snapEllipseGlowVisual = null;
            }
        }

        /// <summary>
        /// Live Draw only: scale snap-border rectangle/ellipse stroke weights so the main outline matches straight lines and arrows
        /// (same <paramref name="mainStrokeThicknessDip"/> as the Live Draw line thickness setting).
        /// Selection overlay keeps the capture border intensity profile without calling this.
        /// </summary>
        internal void ApplyLiveDrawStrokeThickness(double mainStrokeThicknessDip)
        {
            var main = Math.Max(1.0, mainStrokeThicknessDip);
            var p = _borderFxProfile;
            var scale = main / p.BorderStrokeThickness;
            if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
            {
                return;
            }

            _snapBorderRectangle.StrokeThickness = main;
            _snapBorderChaseRectangle.StrokeThickness = Math.Max(1.0, p.ChaseStrokeThickness * scale);
            _snapBorderCornerGlowRectangle.StrokeThickness = Math.Max(1.0, p.CornerGlowStrokeThickness * scale);
            _snapBorderGlowRectangle.StrokeThickness = Math.Max(1.0, p.OuterGlowStrokeThickness * scale);

            if (HasEllipseLayerTemplates)
            {
                _snapEllipseBorder!.StrokeThickness = main;
                _snapEllipseChase!.StrokeThickness = Math.Max(1.0, p.ChaseStrokeThickness * scale);
                _snapEllipseCornerGlow!.StrokeThickness = Math.Max(1.0, p.CornerGlowStrokeThickness * scale);
                _snapEllipseGlow!.StrokeThickness = Math.Max(1.0, p.OuterGlowStrokeThickness * scale);
            }
        }

        /// <summary>Copies the four animated snap-border layers onto the canvas so they keep gradient drift and dash motion.</summary>
        internal void CommitSnapBorderToDrawCanvas(Canvas drawCanvas, double left, double top, double width, double height)
        {
            if (_compositor is null || width < 1 || height < 1)
            {
                return;
            }

            var border = CreateCommittedRectangle(_snapBorderRectangle);
            var chase = CreateCommittedRectangle(_snapBorderChaseRectangle);
            var corner = CreateCommittedRectangle(_snapBorderCornerGlowRectangle);
            var glow = CreateCommittedRectangle(_snapBorderGlowRectangle);

            ApplyRectangleGeometry(border, left, top, width, height);
            ApplyRectangleGeometry(chase, left, top, width, height);
            ApplyRectangleGeometry(corner, left, top, width, height);
            ApplyRectangleGeometry(
                glow,
                left - _borderFxProfile.GlowPadding,
                top - _borderFxProfile.GlowPadding,
                width + (_borderFxProfile.GlowPadding * 2),
                height + (_borderFxProfile.GlowPadding * 2));

            chase.StrokeDashOffset = _snapBorderChaseRectangle.StrokeDashOffset;
            corner.StrokeDashOffset = _snapBorderCornerGlowRectangle.StrokeDashOffset;

            drawCanvas.Children.Add(border);
            drawCanvas.Children.Add(chase);
            drawCanvas.Children.Add(corner);
            drawCanvas.Children.Add(glow);

            var vBorder = ElementCompositionPreview.GetElementVisual(border);
            var vChase = ElementCompositionPreview.GetElementVisual(chase);
            var vCorner = ElementCompositionPreview.GetElementVisual(corner);
            var vGlow = ElementCompositionPreview.GetElementVisual(glow);

            var cx = (float)(width / 2.0);
            var cy = (float)(height / 2.0);
            var center = new Vector3(cx, cy, 0f);
            vBorder.CenterPoint = center;
            vChase.CenterPoint = center;
            vCorner.CenterPoint = center;
            vGlow.CenterPoint = center;

            StartCommittedVisualAnimations(vBorder, vChase, vCorner, vGlow);

            _committedGroups.Add(new CommittedSnapBorderGroup(border, chase, corner, glow));
        }

        /// <summary>Live Draw ellipse/circle: same animated chrome as snap-border rectangles, drawn with stroked ellipses.</summary>
        internal void CommitSnapBorderEllipseToDrawCanvas(Canvas drawCanvas, double left, double top, double width, double height)
        {
            if (_compositor is null || width < 1 || height < 1 || !HasEllipseLayerTemplates)
            {
                return;
            }

            var border = CreateCommittedEllipse(_snapEllipseBorder!);
            var chase = CreateCommittedEllipse(_snapEllipseChase!);
            var corner = CreateCommittedEllipse(_snapEllipseCornerGlow!);
            var glow = CreateCommittedEllipse(_snapEllipseGlow!);

            ApplyEllipseGeometry(border, left, top, width, height);
            ApplyEllipseGeometry(chase, left, top, width, height);
            ApplyEllipseGeometry(corner, left, top, width, height);
            ApplyEllipseGeometry(
                glow,
                left - _borderFxProfile.GlowPadding,
                top - _borderFxProfile.GlowPadding,
                width + (_borderFxProfile.GlowPadding * 2),
                height + (_borderFxProfile.GlowPadding * 2));

            chase.StrokeDashOffset = _snapEllipseChase!.StrokeDashOffset;
            corner.StrokeDashOffset = _snapEllipseCornerGlow!.StrokeDashOffset;

            drawCanvas.Children.Add(border);
            drawCanvas.Children.Add(chase);
            drawCanvas.Children.Add(corner);
            drawCanvas.Children.Add(glow);

            var vBorder = ElementCompositionPreview.GetElementVisual(border);
            var vChase = ElementCompositionPreview.GetElementVisual(chase);
            var vCorner = ElementCompositionPreview.GetElementVisual(corner);
            var vGlow = ElementCompositionPreview.GetElementVisual(glow);

            var cx = (float)(width / 2.0);
            var cy = (float)(height / 2.0);
            var center = new Vector3(cx, cy, 0f);
            vBorder.CenterPoint = center;
            vChase.CenterPoint = center;
            vCorner.CenterPoint = center;
            vGlow.CenterPoint = center;

            StartCommittedVisualAnimations(vBorder, vChase, vCorner, vGlow);

            _committedEllipseGroups.Add(new CommittedSnapEllipseGroup(border, chase, corner, glow));
        }

        /// <summary>One-shot “click here” burst using the same gradient/dash palette as snap chrome. Live Draw’s default right-click UX is <see cref="StartClickSparkleHold"/> (pulse while held).</summary>
        internal void PlayClickSparkle(Canvas drawCanvas, Point center)
        {
            PickNextBorderPalette();
            ResetDashSpeedToDefault();

            const int sparkleAnimationMs = 400;
            var left = center.X - (ClickSparkleSizeDip / 2.0);
            var top = center.Y - (ClickSparkleSizeDip / 2.0);
            var container = CreateClickSparkleContainerPopulated(left, top);
            drawCanvas.Children.Add(container);

            if (_compositor is null)
            {
                ScheduleRemoveSparkleContainer(drawCanvas, container, sparkleAnimationMs + 20);
                return;
            }

            var v = ElementCompositionPreview.GetElementVisual(container);
            if (v is null)
            {
                ScheduleRemoveSparkleContainer(drawCanvas, container, sparkleAnimationMs + 20);
                return;
            }

            v.CenterPoint = new Vector3((float)(ClickSparkleSizeDip / 2.0), (float)(ClickSparkleSizeDip / 2.0), 0f);
            v.Opacity = 1f;
            v.Scale = new Vector3(0.52f, 0.52f, 1f);

            var opacityAnim = _compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.InsertKeyFrame(0.0f, 1.0f);
            opacityAnim.InsertKeyFrame(1.0f, 0.0f);
            opacityAnim.Duration = TimeSpan.FromMilliseconds(sparkleAnimationMs);
            opacityAnim.IterationBehavior = AnimationIterationBehavior.Count;
            opacityAnim.IterationCount = 1;

            var scaleAnim = _compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.InsertKeyFrame(0.0f, new Vector3(0.52f, 0.52f, 1f));
            scaleAnim.InsertKeyFrame(1.0f, new Vector3(1.32f, 1.32f, 1f));
            scaleAnim.Duration = TimeSpan.FromMilliseconds(sparkleAnimationMs);
            scaleAnim.IterationBehavior = AnimationIterationBehavior.Count;
            scaleAnim.IterationCount = 1;

            var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            v.StartAnimation("Opacity", opacityAnim);
            v.StartAnimation("Scale", scaleAnim);
            batch.End();
            batch.Completed += (_, _) => drawCanvas.Children.Remove(container);
        }

        /// <summary>Live Draw: right-click sparkle that loops a smooth opacity/scale pulse until <see cref="StopClickSparkleHold"/> (call <see cref="UpdateClickSparkleHoldPosition"/> while the pointer moves).</summary>
        internal void StartClickSparkleHold(Canvas drawCanvas, Point center)
        {
            StopClickSparkleHold();
            PickNextBorderPalette();
            ResetDashSpeedToDefault();

            var left = center.X - (ClickSparkleSizeDip / 2.0);
            var top = center.Y - (ClickSparkleSizeDip / 2.0);
            var container = CreateClickSparkleContainerPopulated(left, top);
            drawCanvas.Children.Add(container);

            _clickSparkleHoldDrawCanvas = drawCanvas;
            _clickSparkleHoldContainer = container;
            BindHoldSparkleRings(container);

            if (_compositor is null)
            {
                StartClickSparkleHoldFallback(container);
                return;
            }

            var v = ElementCompositionPreview.GetElementVisual(container);
            if (v is null)
            {
                StartClickSparkleHoldFallback(container);
                return;
            }

            _clickSparkleHoldVisual = v;
            var half = (float)(ClickSparkleSizeDip / 2.0);
            v.CenterPoint = new Vector3(half, half, 0f);
            v.Opacity = 0.62f;
            v.Scale = new Vector3(0.72f, 0.72f, 1f);

            v.StartAnimation("Opacity", CreateClickSparkleHoldOpacityAnimation());
            v.StartAnimation("Scale", CreateClickSparkleHoldScaleAnimation());
            StartClickSparkleHoldRingSpinAnimations();
        }

        internal void UpdateClickSparkleHoldPosition(Canvas drawCanvas, Point center)
        {
            if (_clickSparkleHoldContainer is null)
            {
                return;
            }

            if (_clickSparkleHoldDrawCanvas is null || !ReferenceEquals(_clickSparkleHoldDrawCanvas, drawCanvas))
            {
                return;
            }

            var left = center.X - (ClickSparkleSizeDip / 2.0);
            var top = center.Y - (ClickSparkleSizeDip / 2.0);
            Canvas.SetLeft(_clickSparkleHoldContainer, left);
            Canvas.SetTop(_clickSparkleHoldContainer, top);
        }

        internal void StopClickSparkleHold()
        {
            if (_clickSparkleHoldFallbackTimer is not null)
            {
                _clickSparkleHoldFallbackTimer.Stop();
                _clickSparkleHoldFallbackTimer = null;
            }

            _clickSparkleHoldFallbackTransform = null;
            _clickSparkleHoldFallbackPhase = 0;

            if (_clickSparkleHoldVisual is not null)
            {
                _clickSparkleHoldVisual.StopAnimation("Opacity");
                _clickSparkleHoldVisual.StopAnimation("Scale");
                _clickSparkleHoldVisual = null;
            }
            foreach (var ringVisual in _clickSparkleHoldRingVisuals)
            {
                ringVisual.StopAnimation("RotationAngleInDegrees");
            }

            _clickSparkleHoldRingVisuals.Clear();
            _clickSparkleHoldRingFallbackRotates.Clear();
            _clickSparkleHoldRings.Clear();

            if (_clickSparkleHoldContainer is not null && _clickSparkleHoldDrawCanvas is not null)
            {
                _clickSparkleHoldDrawCanvas.Children.Remove(_clickSparkleHoldContainer);
            }

            _clickSparkleHoldContainer = null;
            _clickSparkleHoldDrawCanvas = null;
        }

        private void StartClickSparkleHoldFallback(Canvas container)
        {
            var dq = DispatcherQueue.GetForCurrentThread();
            if (dq is null)
            {
                _clickSparkleHoldDrawCanvas?.Children.Remove(container);
                _clickSparkleHoldContainer = null;
                _clickSparkleHoldDrawCanvas = null;
                return;
            }

            var ct = new CompositeTransform
            {
                CenterX = ClickSparkleSizeDip / 2.0,
                CenterY = ClickSparkleSizeDip / 2.0,
                ScaleX = 0.72,
                ScaleY = 0.72
            };
            container.RenderTransform = ct;
            _clickSparkleHoldFallbackTransform = ct;

            var phaseStep = (Math.PI * 2.0 * ClickSparkleHoldFallbackTickMs) / ClickSparkleHoldPulseMs;
            _clickSparkleHoldFallbackPhase = 0;

            ApplyClickSparkleHoldFallbackFrame();

            var timer = dq.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(ClickSparkleHoldFallbackTickMs);
            timer.Tick += (_, _) =>
            {
                _clickSparkleHoldFallbackPhase += phaseStep;
                ApplyClickSparkleHoldFallbackFrame();
            };
            timer.Start();
            _clickSparkleHoldFallbackTimer = timer;
        }

        private void ApplyClickSparkleHoldFallbackFrame()
        {
            if (_clickSparkleHoldContainer is null || _clickSparkleHoldFallbackTransform is null)
            {
                return;
            }

            var s = Math.Sin(_clickSparkleHoldFallbackPhase);
            var t = 0.5 + 0.5 * s;
            _clickSparkleHoldContainer.Opacity = 0.62 + 0.38 * t;
            var scale = 0.72 + 0.50 * t;
            _clickSparkleHoldFallbackTransform.ScaleX = scale;
            _clickSparkleHoldFallbackTransform.ScaleY = scale;

            var elapsedSeconds = ClickSparkleHoldFallbackTickMs / 1000f;
            for (var i = 0; i < _clickSparkleHoldRingFallbackRotates.Count; i++)
            {
                var rotate = _clickSparkleHoldRingFallbackRotates[i];
                var speed = GetClickSparkleRingSpinSpeed(i);
                rotate.Angle += speed * elapsedSeconds;
            }
        }

        private void BindHoldSparkleRings(Canvas container)
        {
            _clickSparkleHoldRings.Clear();
            _clickSparkleHoldRingVisuals.Clear();
            _clickSparkleHoldRingFallbackRotates.Clear();
            for (var i = 0; i < container.Children.Count; i++)
            {
                if (container.Children[i] is Ellipse ring)
                {
                    _clickSparkleHoldRings.Add(ring);
                }
            }
        }

        private void StartClickSparkleHoldRingSpinAnimations()
        {
            if (_compositor is null)
            {
                return;
            }

            for (var i = 0; i < _clickSparkleHoldRings.Count; i++)
            {
                var ring = _clickSparkleHoldRings[i];
                var visual = ElementCompositionPreview.GetElementVisual(ring);
                if (visual is null)
                {
                    continue;
                }

                visual.CenterPoint = new Vector3(
                    (float)(ring.Width / 2.0),
                    (float)(ring.Height / 2.0),
                    0f);
                visual.StartAnimation(
                    "RotationAngleInDegrees",
                    CreateClickSparkleRingSpinAnimation(GetClickSparkleRingSpinSpeed(i)));
                _clickSparkleHoldRingVisuals.Add(visual);
            }

            EnsureHoldSparkleFallbackRingTransforms();
        }

        private ScalarKeyFrameAnimation CreateClickSparkleRingSpinAnimation(float degreesPerSecond)
        {
            var a = _compositor!.CreateScalarKeyFrameAnimation();
            a.InsertKeyFrame(0.0f, 0.0f);
            a.InsertKeyFrame(1.0f, degreesPerSecond);
            a.Duration = TimeSpan.FromSeconds(1);
            a.IterationBehavior = AnimationIterationBehavior.Forever;
            return a;
        }

        private static float GetClickSparkleRingSpinSpeed(int ringIndex)
        {
            if (ClickSparkleRingSpinDegreesPerSecond.Length == 0)
            {
                return 0f;
            }

            var index = ringIndex % ClickSparkleRingSpinDegreesPerSecond.Length;
            return ClickSparkleRingSpinDegreesPerSecond[index];
        }

        private void EnsureHoldSparkleFallbackRingTransforms()
        {
            _clickSparkleHoldRingFallbackRotates.Clear();
            foreach (var ring in _clickSparkleHoldRings)
            {
                var rotate = new RotateTransform
                {
                    CenterX = ring.Width / 2.0,
                    CenterY = ring.Height / 2.0
                };
                ring.RenderTransform = rotate;
                _clickSparkleHoldRingFallbackRotates.Add(rotate);
            }
        }

        private ScalarKeyFrameAnimation CreateClickSparkleHoldOpacityAnimation()
        {
            var a = _compositor!.CreateScalarKeyFrameAnimation();
            a.InsertKeyFrame(0.0f, 0.62f);
            a.InsertKeyFrame(0.5f, 1.0f);
            a.InsertKeyFrame(1.0f, 0.62f);
            a.Duration = TimeSpan.FromMilliseconds(ClickSparkleHoldPulseMs);
            a.IterationBehavior = AnimationIterationBehavior.Forever;
            return a;
        }

        private Vector3KeyFrameAnimation CreateClickSparkleHoldScaleAnimation()
        {
            var a = _compositor!.CreateVector3KeyFrameAnimation();
            a.InsertKeyFrame(0.0f, new Vector3(0.72f, 0.72f, 1f));
            a.InsertKeyFrame(0.5f, new Vector3(1.22f, 1.22f, 1f));
            a.InsertKeyFrame(1.0f, new Vector3(0.72f, 0.72f, 1f));
            a.Duration = TimeSpan.FromMilliseconds(ClickSparkleHoldPulseMs);
            a.IterationBehavior = AnimationIterationBehavior.Forever;
            return a;
        }

        private Canvas CreateClickSparkleContainerPopulated(double left, double top)
        {
            var container = new Canvas
            {
                Width = ClickSparkleSizeDip,
                Height = ClickSparkleSizeDip
            };
            Canvas.SetLeft(container, left);
            Canvas.SetTop(container, top);

            var p = _borderFxProfile;
            AddSparkleRing(container, ClickSparkleSizeDip, 34, p.BorderStrokeThickness, _snapBorderGradientBrush, null);
            AddSparkleRing(
                container,
                ClickSparkleSizeDip,
                40,
                Math.Max(1.1, p.ChaseStrokeThickness * 0.72),
                _snapBorderChaseGradientBrush,
                ChaseDashPattern);
            AddSparkleRing(
                container,
                ClickSparkleSizeDip,
                46,
                Math.Max(1.0, p.CornerGlowStrokeThickness * 0.68),
                _snapBorderCornerGlowBrush,
                CornerDashPattern);
            AddSparkleRing(container, ClickSparkleSizeDip, 56, p.OuterGlowStrokeThickness, _snapBorderGlowGradientBrush, null);

            return container;
        }

        private static void ScheduleRemoveSparkleContainer(Canvas drawCanvas, UIElement container, int delayMs)
        {
            var dq = DispatcherQueue.GetForCurrentThread();
            if (dq is null)
            {
                drawCanvas.Children.Remove(container);
                return;
            }

            var timer = dq.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(delayMs);
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                drawCanvas.Children.Remove(container);
            };
            timer.Start();
        }

        private void AddSparkleRing(
            Canvas container,
            double containerSize,
            double ringDiameter,
            double strokeThickness,
            Brush stroke,
            DoubleCollection? dash)
        {
            var e = new Ellipse
            {
                Width = Math.Max(1, ringDiameter),
                Height = Math.Max(1, ringDiameter),
                Stroke = stroke,
                StrokeThickness = strokeThickness,
                Fill = null
            };

            if (dash is not null)
            {
                e.StrokeDashArray = CloneDashArray(dash);
            }

            var offset = (containerSize - ringDiameter) / 2.0;
            Canvas.SetLeft(e, offset);
            Canvas.SetTop(e, offset);
            container.Children.Add(e);
        }

        private static Rectangle CreateCommittedRectangle(Rectangle template)
        {
            return new Rectangle
            {
                Stroke = template.Stroke,
                StrokeThickness = template.StrokeThickness,
                StrokeDashArray = CloneDashArray(template.StrokeDashArray),
                Fill = null,
                Visibility = Visibility.Visible
            };
        }

        private static Ellipse CreateCommittedEllipse(Ellipse template)
        {
            return new Ellipse
            {
                Stroke = template.Stroke,
                StrokeThickness = template.StrokeThickness,
                StrokeDashArray = CloneDashArray(template.StrokeDashArray),
                Fill = null,
                Visibility = Visibility.Visible
            };
        }

        private static DoubleCollection? CloneDashArray(DoubleCollection? source)
        {
            if (source is null || source.Count == 0)
            {
                return null;
            }

            var c = new DoubleCollection();
            foreach (var v in source)
            {
                c.Add(v);
            }

            return c;
        }

        private void StartCommittedVisualAnimations(Visual border, Visual chase, Visual corner, Visual glow)
        {
            if (_compositor is null)
            {
                return;
            }

            border.StartAnimation("Opacity", CreateBorderOpacityAnimation());
            border.StartAnimation("Scale", CreateBorderScaleAnimation());
            glow.StartAnimation("Opacity", CreateGlowOpacityAnimation());
            glow.StartAnimation("Scale", CreateGlowScaleAnimation());
            chase.StartAnimation("Opacity", CreateChaseOpacityAnimation());
            chase.StartAnimation("Scale", CreateChaseScaleAnimation());
            corner.StartAnimation("Opacity", CreateChaseOpacityAnimation());
        }

        private ScalarKeyFrameAnimation CreateBorderOpacityAnimation()
        {
            var a = _compositor!.CreateScalarKeyFrameAnimation();
            a.InsertKeyFrame(0.0f, (float)_borderFxProfile.BorderOpacityLow);
            a.InsertKeyFrame(0.5f, (float)_borderFxProfile.BorderOpacityHigh);
            a.InsertKeyFrame(1.0f, (float)_borderFxProfile.BorderOpacityLow);
            a.Duration = TimeSpan.FromMilliseconds(1500);
            a.IterationBehavior = AnimationIterationBehavior.Forever;
            return a;
        }

        private Vector3KeyFrameAnimation CreateBorderScaleAnimation()
        {
            var a = _compositor!.CreateVector3KeyFrameAnimation();
            a.InsertKeyFrame(0.0f, Vector3.One);
            a.InsertKeyFrame(0.5f, new Vector3((float)_borderFxProfile.BorderPulseMaxScale, (float)_borderFxProfile.BorderPulseMaxScale, 1f));
            a.InsertKeyFrame(1.0f, Vector3.One);
            a.Duration = TimeSpan.FromMilliseconds(1550);
            a.IterationBehavior = AnimationIterationBehavior.Forever;
            return a;
        }

        private ScalarKeyFrameAnimation CreateGlowOpacityAnimation()
        {
            var a = _compositor!.CreateScalarKeyFrameAnimation();
            a.InsertKeyFrame(0.0f, (float)_borderFxProfile.GlowOpacityLow);
            a.InsertKeyFrame(0.5f, (float)_borderFxProfile.GlowOpacityHigh);
            a.InsertKeyFrame(1.0f, (float)_borderFxProfile.GlowOpacityLow);
            a.Duration = TimeSpan.FromMilliseconds(2200);
            a.IterationBehavior = AnimationIterationBehavior.Forever;
            return a;
        }

        private Vector3KeyFrameAnimation CreateGlowScaleAnimation()
        {
            var a = _compositor!.CreateVector3KeyFrameAnimation();
            a.InsertKeyFrame(0.0f, Vector3.One);
            a.InsertKeyFrame(0.5f, new Vector3((float)_borderFxProfile.GlowPulseMaxScale, (float)_borderFxProfile.GlowPulseMaxScale, 1f));
            a.InsertKeyFrame(1.0f, Vector3.One);
            a.Duration = TimeSpan.FromMilliseconds(2400);
            a.IterationBehavior = AnimationIterationBehavior.Forever;
            return a;
        }

        private ScalarKeyFrameAnimation CreateChaseOpacityAnimation()
        {
            var a = _compositor!.CreateScalarKeyFrameAnimation();
            a.InsertKeyFrame(0.0f, (float)_borderFxProfile.ChaseOpacityLow);
            a.InsertKeyFrame(0.5f, (float)_borderFxProfile.ChaseOpacityHigh);
            a.InsertKeyFrame(1.0f, (float)_borderFxProfile.ChaseOpacityLow);
            a.Duration = TimeSpan.FromMilliseconds(980);
            a.IterationBehavior = AnimationIterationBehavior.Forever;
            return a;
        }

        private Vector3KeyFrameAnimation CreateChaseScaleAnimation()
        {
            var a = _compositor!.CreateVector3KeyFrameAnimation();
            a.InsertKeyFrame(0.0f, Vector3.One);
            a.InsertKeyFrame(0.5f, new Vector3((float)_borderFxProfile.ChasePulseMaxScale, (float)_borderFxProfile.ChasePulseMaxScale, 1f));
            a.InsertKeyFrame(1.0f, Vector3.One);
            a.Duration = TimeSpan.FromMilliseconds(1050);
            a.IterationBehavior = AnimationIterationBehavior.Forever;
            return a;
        }

        private sealed class CommittedSnapBorderGroup
        {
            internal CommittedSnapBorderGroup(Rectangle border, Rectangle chase, Rectangle cornerGlow, Rectangle glow)
            {
                Border = border;
                Chase = chase;
                CornerGlow = cornerGlow;
                Glow = glow;
            }

            internal Rectangle Border { get; }
            internal Rectangle Chase { get; }
            internal Rectangle CornerGlow { get; }
            internal Rectangle Glow { get; }
        }

        private sealed class CommittedSnapEllipseGroup
        {
            internal CommittedSnapEllipseGroup(Ellipse border, Ellipse chase, Ellipse cornerGlow, Ellipse glow)
            {
                Border = border;
                Chase = chase;
                CornerGlow = cornerGlow;
                Glow = glow;
            }

            internal Ellipse Border { get; }
            internal Ellipse Chase { get; }
            internal Ellipse CornerGlow { get; }
            internal Ellipse Glow { get; }
        }

        private sealed class CommittedSnapArrowGroup
        {
            internal CommittedSnapArrowGroup(Line chaseShaft, Polygon chaseHead, Line cornerShaft, Polygon cornerHead)
            {
                ChaseShaft = chaseShaft;
                ChaseHead = chaseHead;
                CornerShaft = cornerShaft;
                CornerHead = cornerHead;
            }

            internal Line ChaseShaft { get; }
            internal Polygon ChaseHead { get; }
            internal Line CornerShaft { get; }
            internal Polygon CornerHead { get; }
        }

        private sealed class CommittedSnapLineGroup
        {
            internal CommittedSnapLineGroup(Line chaseLine, Line cornerLine)
            {
                ChaseLine = chaseLine;
                CornerLine = cornerLine;
            }

            internal Line ChaseLine { get; }
            internal Line CornerLine { get; }
        }

        private sealed record CommittedFreeDrawGroup(Polyline Chase, Polyline Corner);

        internal void OnColorDriftTick(double deltaSeconds)
        {
            _borderEffectElapsedSeconds += deltaSeconds;
            UpdateAnimatedBorderBrushes();
            UpdateTravelingHighlight(deltaSeconds);
        }

        internal void UpdateSnapBorderLayers(double localX, double localY, int width, int height)
        {
            ApplyRectangleGeometry(_snapBorderRectangle, localX, localY, width, height);
            ApplyRectangleGeometry(_snapBorderChaseRectangle, localX, localY, width, height);
            ApplyRectangleGeometry(_snapBorderCornerGlowRectangle, localX, localY, width, height);
            ApplyRectangleGeometry(
                _snapBorderGlowRectangle,
                localX - _borderFxProfile.GlowPadding,
                localY - _borderFxProfile.GlowPadding,
                width + (_borderFxProfile.GlowPadding * 2),
                height + (_borderFxProfile.GlowPadding * 2));
        }

        internal void UpdateSnapBorderEllipseLayers(double localX, double localY, double width, double height)
        {
            if (!HasEllipseLayerTemplates)
            {
                return;
            }

            ApplyEllipseGeometry(_snapEllipseBorder!, localX, localY, width, height);
            ApplyEllipseGeometry(_snapEllipseChase!, localX, localY, width, height);
            ApplyEllipseGeometry(_snapEllipseCornerGlow!, localX, localY, width, height);
            ApplyEllipseGeometry(
                _snapEllipseGlow!,
                localX - _borderFxProfile.GlowPadding,
                localY - _borderFxProfile.GlowPadding,
                width + (_borderFxProfile.GlowPadding * 2),
                height + (_borderFxProfile.GlowPadding * 2));
        }

        internal void SetSnapBorderLayersVisible(bool isVisible)
        {
            var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            _snapBorderRectangle.Visibility = visibility;
            _snapBorderChaseRectangle.Visibility = visibility;
            _snapBorderCornerGlowRectangle.Visibility = visibility;
            _snapBorderGlowRectangle.Visibility = visibility;
        }

        internal void SetSnapBorderEllipseLayersVisible(bool isVisible)
        {
            if (!HasEllipseLayerTemplates)
            {
                return;
            }

            var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            _snapEllipseBorder!.Visibility = visibility;
            _snapEllipseChase!.Visibility = visibility;
            _snapEllipseCornerGlow!.Visibility = visibility;
            _snapEllipseGlow!.Visibility = visibility;
        }

        internal void StartSnapBorderAnimations()
        {
            if (_snapBorderVisual is null || _borderOpacityAnimation is null || _borderScaleAnimation is null)
            {
                return;
            }

            var center = new Vector3((float)(_snapBorderRectangle.Width / 2.0), (float)(_snapBorderRectangle.Height / 2.0), 0f);
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

            if (_isBorderAnimationRunning)
            {
                return;
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

        internal void StopSnapBorderAnimations()
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
            _snapBorderRectangle.Stroke = _snapBorderGradientBrush;
            _snapBorderChaseRectangle.Stroke = _snapBorderChaseGradientBrush;
            _snapBorderGlowRectangle.Stroke = _snapBorderGlowGradientBrush;
            _snapBorderCornerGlowRectangle.Stroke = _snapBorderCornerGlowBrush;
        }

        internal void StartSnapBorderEllipseAnimations()
        {
            if (_snapEllipseBorderVisual is null ||
                _borderOpacityAnimation is null ||
                _borderScaleAnimation is null ||
                _snapEllipseBorder is null)
            {
                return;
            }

            var center = new Vector3(
                (float)(_snapEllipseBorder.Width / 2.0),
                (float)(_snapEllipseBorder.Height / 2.0),
                0f);
            _snapEllipseBorderVisual.CenterPoint = center;
            if (_snapEllipseGlowVisual is not null)
            {
                _snapEllipseGlowVisual.CenterPoint = center;
            }

            if (_snapEllipseChaseVisual is not null)
            {
                _snapEllipseChaseVisual.CenterPoint = center;
            }

            if (_snapEllipseCornerGlowVisual is not null)
            {
                _snapEllipseCornerGlowVisual.CenterPoint = center;
            }

            if (_isEllipseBorderAnimationRunning)
            {
                return;
            }

            _snapEllipseBorderVisual.StartAnimation("Opacity", _borderOpacityAnimation);
            _snapEllipseBorderVisual.StartAnimation("Scale", _borderScaleAnimation);
            if (_snapEllipseGlowVisual is not null && _borderGlowOpacityAnimation is not null && _borderGlowScaleAnimation is not null)
            {
                _snapEllipseGlowVisual.StartAnimation("Opacity", _borderGlowOpacityAnimation);
                _snapEllipseGlowVisual.StartAnimation("Scale", _borderGlowScaleAnimation);
            }

            if (_snapEllipseChaseVisual is not null && _borderChaseOpacityAnimation is not null && _borderChaseScaleAnimation is not null)
            {
                _snapEllipseChaseVisual.StartAnimation("Opacity", _borderChaseOpacityAnimation);
                _snapEllipseChaseVisual.StartAnimation("Scale", _borderChaseScaleAnimation);
            }

            if (_snapEllipseCornerGlowVisual is not null && _borderChaseOpacityAnimation is not null)
            {
                _snapEllipseCornerGlowVisual.StartAnimation("Opacity", _borderChaseOpacityAnimation);
            }

            _isEllipseBorderAnimationRunning = true;
        }

        internal void StopSnapBorderEllipseAnimations()
        {
            if (_snapEllipseBorderVisual is null)
            {
                return;
            }

            _snapEllipseBorderVisual.StopAnimation("Opacity");
            _snapEllipseBorderVisual.StopAnimation("Scale");
            _snapEllipseBorderVisual.Opacity = 1f;
            _snapEllipseBorderVisual.Scale = Vector3.One;
            if (_snapEllipseGlowVisual is not null)
            {
                _snapEllipseGlowVisual.StopAnimation("Opacity");
                _snapEllipseGlowVisual.StopAnimation("Scale");
                _snapEllipseGlowVisual.Opacity = 1f;
                _snapEllipseGlowVisual.Scale = Vector3.One;
            }

            if (_snapEllipseChaseVisual is not null)
            {
                _snapEllipseChaseVisual.StopAnimation("Opacity");
                _snapEllipseChaseVisual.StopAnimation("Scale");
                _snapEllipseChaseVisual.Opacity = 1f;
                _snapEllipseChaseVisual.Scale = Vector3.One;
            }

            if (_snapEllipseCornerGlowVisual is not null)
            {
                _snapEllipseCornerGlowVisual.StopAnimation("Opacity");
                _snapEllipseCornerGlowVisual.Opacity = 1f;
            }

            _isEllipseBorderAnimationRunning = false;
            if (_snapEllipseBorder is not null)
            {
                _snapEllipseBorder.Stroke = _snapBorderGradientBrush;
            }

            if (_snapEllipseChase is not null)
            {
                _snapEllipseChase.Stroke = _snapBorderChaseGradientBrush;
            }

            if (_snapEllipseGlow is not null)
            {
                _snapEllipseGlow.Stroke = _snapBorderGlowGradientBrush;
            }

            if (_snapEllipseCornerGlow is not null)
            {
                _snapEllipseCornerGlow.Stroke = _snapBorderCornerGlowBrush;
            }
        }

        internal void PickNextBorderPalette()
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

        internal void UpdateChaseSpeedForBounds(RectInt32 bounds)
        {
            var perimeter = (bounds.Width * 2.0) + (bounds.Height * 2.0);
            var normalizedSize = Math.Clamp((perimeter - 240.0) / 3200.0, 0.0, 1.0);
            var speedScale = 0.75 + (normalizedSize * 0.95);
            _currentDashSpeedUnitsPerSecond = _borderFxProfile.DashSpeedUnitsPerSecond * speedScale;
        }

        internal void UpdateChaseSpeedForPixelSize(double width, double height)
        {
            var perimeter = (width * 2.0) + (height * 2.0);
            var normalizedSize = Math.Clamp((perimeter - 240.0) / 3200.0, 0.0, 1.0);
            var speedScale = 0.75 + (normalizedSize * 0.95);
            _currentDashSpeedUnitsPerSecond = _borderFxProfile.DashSpeedUnitsPerSecond * speedScale;
        }

        internal void ResetDashSpeedToDefault()
        {
            _currentDashSpeedUnitsPerSecond = _borderFxProfile.DashSpeedUnitsPerSecond;
        }

        /// <summary>Scales dash speed from stroke length (pixels). Used for Live Draw arrows, straight lines, and freehand paths.</summary>
        internal void UpdateChaseSpeedForArrowLength(double length)
        {
            var normalizedSize = Math.Clamp((length - 120.0) / 1600.0, 0.0, 1.0);
            var speedScale = 0.75 + (normalizedSize * 0.95);
            _currentDashSpeedUnitsPerSecond = _borderFxProfile.DashSpeedUnitsPerSecond * speedScale;
        }

        /// <summary>Live Draw freehand: three polylines with identical geometry (separate <see cref="PointCollection"/> per shape — WinUI does not safely share one collection across multiple polylines).</summary>
        internal void ConfigureLiveDrawPolylineLayers(
            Polyline main,
            Polyline chase,
            Polyline corner,
            PointCollection mainPoints,
            PointCollection chasePoints,
            PointCollection cornerPoints)
        {
            main.Points = mainPoints;
            main.Stroke = _snapBorderGradientBrush;
            // Live Draw only: keep freehand ink stroke weights aligned with the scaled snap-border chrome.
            main.StrokeThickness = Math.Max(1.0, _snapBorderRectangle.StrokeThickness);
            ApplyRoundPolylineOutline(main);

            chase.Points = chasePoints;
            chase.Stroke = _snapBorderChaseGradientBrush;
            chase.StrokeThickness = Math.Max(1.0, _snapBorderChaseRectangle.StrokeThickness);
            chase.StrokeDashArray = CloneDashArray(ChaseDashPattern);
            ApplyRoundPolylineOutline(chase);

            corner.Points = cornerPoints;
            corner.Stroke = _snapBorderCornerGlowBrush;
            corner.StrokeThickness = Math.Max(1.0, _snapBorderCornerGlowRectangle.StrokeThickness);
            corner.StrokeDashArray = CloneDashArray(CornerDashPattern);
            ApplyRoundPolylineOutline(corner);
        }

        /// <summary>Registers dashed freehand layers so <see cref="UpdateTravelingHighlight"/> animates dash offsets after the stroke ends.</summary>
        internal void RegisterCommittedFreeDraw(Polyline chase, Polyline corner)
        {
            _committedFreeDrawGroups.Add(new CommittedFreeDrawGroup(chase, corner));
        }

        /// <summary>Composition pulse on the stroke container (same pattern as <see cref="CommitSnapChromeArrowToDrawCanvas"/>).</summary>
        internal void FinalizeLiveDrawStroke(Canvas container, double centerX, double centerY)
        {
            StartCommittedContainerPulse(container, centerX, centerY);
        }

        private void StartCommittedContainerPulse(Canvas container, double centerX, double centerY)
        {
            if (_compositor is null)
            {
                return;
            }

            var v = ElementCompositionPreview.GetElementVisual(container);
            if (v is null)
            {
                return;
            }

            v.CenterPoint = new Vector3((float)centerX, (float)centerY, 0f);
            v.StartAnimation("Opacity", CreateBorderOpacityAnimation());
            v.StartAnimation("Scale", CreateBorderScaleAnimation());
        }

        private static void ApplyRoundPolylineOutline(Polyline polyline)
        {
            polyline.StrokeLineJoin = PenLineJoin.Round;
            polyline.StrokeStartLineCap = PenLineCap.Round;
            polyline.StrokeEndLineCap = PenLineCap.Round;
        }

        /// <summary>Live Draw arrow preview: same gradient / dash palette as snap-border chrome.</summary>
        internal void DrawSnapChromeArrow(Canvas canvas, ArrowLayer layer)
        {
            var dx = layer.EndX - layer.StartX;
            var dy = layer.EndY - layer.StartY;
            if (Math.Sqrt((dx * dx) + (dy * dy)) < 2.0)
            {
                return;
            }

            GetStraightArrowGeometry(layer, out var startX, out var startY, out var tipX, out var tipY, out var baseX, out var baseY, out var leftX, out var leftY, out var rightX, out var rightY);
            ComputeShaftTailPoints(layer, startX, startY, baseX, baseY, leftX, leftY, rightX, rightY, out var tlX, out var tlY, out var trX, out var trY);
            AddArrowChromeLayers(
                canvas,
                startX,
                startY,
                tipX,
                tipY,
                baseX,
                baseY,
                leftX,
                leftY,
                rightX,
                rightY,
                tlX,
                tlY,
                trX,
                trY,
                out _,
                out _,
                out _,
                out _);
        }

        /// <summary>Commits a Live Draw arrow with the same animated chrome as other snap-border shapes (gradient drift, dashing, pulse).</summary>
        internal void CommitSnapChromeArrowToDrawCanvas(Canvas drawCanvas, ArrowLayer layer)
        {
            var adx = layer.EndX - layer.StartX;
            var ady = layer.EndY - layer.StartY;
            if (Math.Sqrt((adx * adx) + (ady * ady)) < 2.0)
            {
                return;
            }

            if (_compositor is null)
            {
                ArrowRendering.DrawArrowLayer(layer, suppressExpensiveEffects: false, drawCanvas);
                return;
            }

            GetStraightArrowGeometry(layer, out var startX, out var startY, out var tipX, out var tipY, out var baseX, out var baseY, out var leftX, out var leftY, out var rightX, out var rightY);
            ComputeShaftTailPoints(layer, startX, startY, baseX, baseY, leftX, leftY, rightX, rightY, out var tailLeftX, out var tailLeftY, out var tailRightX, out var tailRightY);
            var minX = Math.Min(Math.Min(Math.Min(startX, tipX), Math.Min(leftX, rightX)), Math.Min(tailLeftX, tailRightX));
            var minY = Math.Min(Math.Min(Math.Min(startY, tipY), Math.Min(leftY, rightY)), Math.Min(tailLeftY, tailRightY));
            var maxX = Math.Max(Math.Max(Math.Max(startX, tipX), Math.Max(leftX, rightX)), Math.Max(tailLeftX, tailRightX));
            var maxY = Math.Max(Math.Max(Math.Max(startY, tipY), Math.Max(leftY, rightY)), Math.Max(tailLeftY, tailRightY));
            var bw = Math.Max(1.0, maxX - minX);
            var bh = Math.Max(1.0, maxY - minY);

            var container = new Canvas();
            Canvas.SetLeft(container, minX);
            Canvas.SetTop(container, minY);
            container.Width = bw;
            container.Height = bh;

            AddArrowChromeLayers(
                container,
                startX - minX,
                startY - minY,
                tipX - minX,
                tipY - minY,
                baseX - minX,
                baseY - minY,
                leftX - minX,
                leftY - minY,
                rightX - minX,
                rightY - minY,
                tailLeftX - minX,
                tailLeftY - minY,
                tailRightX - minX,
                tailRightY - minY,
                out var chaseShaft,
                out var chaseHead,
                out var cornerShaft,
                out var cornerHead);

            drawCanvas.Children.Add(container);

            StartCommittedContainerPulse(container, bw / 2.0, bh / 2.0);

            _committedArrowGroups.Add(new CommittedSnapArrowGroup(chaseShaft, chaseHead, cornerShaft, cornerHead));
        }

        /// <summary>Live Draw straight-line preview: uniform stroke (not arrow-shaft taper); same gradients and dash layers as other snap chrome.</summary>
        internal void DrawSnapChromeLine(Canvas canvas, ArrowLayer layer)
        {
            var dx = layer.EndX - layer.StartX;
            var dy = layer.EndY - layer.StartY;
            if (Math.Sqrt((dx * dx) + (dy * dy)) < 2.0)
            {
                return;
            }

            AddLineChromeLayers(
                canvas,
                layer,
                layer.StartX,
                layer.StartY,
                layer.EndX,
                layer.EndY,
                out _,
                out _);
        }

        /// <summary>Commits a Live Draw straight line with the same animated chrome as arrows (no arrowhead).</summary>
        internal void CommitSnapChromeLineToDrawCanvas(Canvas drawCanvas, ArrowLayer layer)
        {
            var adx = layer.EndX - layer.StartX;
            var ady = layer.EndY - layer.StartY;
            if (Math.Sqrt((adx * adx) + (ady * ady)) < 2.0)
            {
                return;
            }

            if (_compositor is null)
            {
                ArrowRendering.DrawArrowLayer(layer, suppressExpensiveEffects: false, drawCanvas);
                return;
            }

            var startX = layer.StartX;
            var startY = layer.StartY;
            var tipX = layer.EndX;
            var tipY = layer.EndY;
            var pad = Math.Max(layer.Thickness * 2.5, _borderFxProfile.OuterGlowStrokeThickness);
            var minX = Math.Min(startX, tipX) - pad;
            var minY = Math.Min(startY, tipY) - pad;
            var maxX = Math.Max(startX, tipX) + pad;
            var maxY = Math.Max(startY, tipY) + pad;
            var bw = Math.Max(1.0, maxX - minX);
            var bh = Math.Max(1.0, maxY - minY);

            var container = new Canvas();
            Canvas.SetLeft(container, minX);
            Canvas.SetTop(container, minY);
            container.Width = bw;
            container.Height = bh;

            AddLineChromeLayers(
                container,
                layer,
                startX - minX,
                startY - minY,
                tipX - minX,
                tipY - minY,
                out var chaseLine,
                out var cornerLine);

            drawCanvas.Children.Add(container);

            StartCommittedContainerPulse(container, bw / 2.0, bh / 2.0);

            _committedLineGroups.Add(new CommittedSnapLineGroup(chaseLine, cornerLine));
        }

        private void UpdateTravelingHighlight(double deltaSeconds)
        {
            var dashDelta = _currentDashSpeedUnitsPerSecond * deltaSeconds;
            foreach (var g in _committedGroups)
            {
                g.Chase.StrokeDashOffset = g.Chase.StrokeDashOffset - dashDelta;
                g.CornerGlow.StrokeDashOffset = g.CornerGlow.StrokeDashOffset + (dashDelta * 0.6);
            }

            foreach (var eg in _committedEllipseGroups)
            {
                eg.Chase.StrokeDashOffset = eg.Chase.StrokeDashOffset - dashDelta;
                eg.CornerGlow.StrokeDashOffset = eg.CornerGlow.StrokeDashOffset + (dashDelta * 0.6);
            }

            foreach (var a in _committedArrowGroups)
            {
                a.ChaseShaft.StrokeDashOffset -= dashDelta;
                a.ChaseHead.StrokeDashOffset -= dashDelta;
                a.CornerShaft.StrokeDashOffset += dashDelta * 0.6;
                a.CornerHead.StrokeDashOffset += dashDelta * 0.6;
            }

            foreach (var ln in _committedLineGroups)
            {
                ln.ChaseLine.StrokeDashOffset -= dashDelta;
                ln.CornerLine.StrokeDashOffset += dashDelta * 0.6;
            }

            foreach (var f in _committedFreeDrawGroups)
            {
                f.Chase.StrokeDashOffset -= dashDelta;
                f.Corner.StrokeDashOffset += dashDelta * 0.6;
            }

            if (_isBorderAnimationRunning)
            {
                _snapBorderChaseRectangle.StrokeDashOffset = _snapBorderChaseRectangle.StrokeDashOffset - dashDelta;
                _snapBorderCornerGlowRectangle.StrokeDashOffset = _snapBorderCornerGlowRectangle.StrokeDashOffset + (dashDelta * 0.6);
            }

            if (_isEllipseBorderAnimationRunning &&
                _snapEllipseChase is not null &&
                _snapEllipseCornerGlow is not null)
            {
                _snapEllipseChase.StrokeDashOffset = _snapEllipseChase.StrokeDashOffset - dashDelta;
                _snapEllipseCornerGlow.StrokeDashOffset = _snapEllipseCornerGlow.StrokeDashOffset + (dashDelta * 0.6);
            }
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

            _arrowBorderGradientBrush.StartPoint = _snapBorderGradientBrush.StartPoint;
            _arrowBorderGradientBrush.EndPoint = _snapBorderGradientBrush.EndPoint;
            _arrowChaseGradientBrush.StartPoint = _snapBorderChaseGradientBrush.StartPoint;
            _arrowChaseGradientBrush.EndPoint = _snapBorderChaseGradientBrush.EndPoint;
            _arrowGlowGradientBrush.StartPoint = _snapBorderGlowGradientBrush.StartPoint;
            _arrowGlowGradientBrush.EndPoint = _snapBorderGlowGradientBrush.EndPoint;

            for (var i = 0; i < _snapBorderGradientStops.Length; i++)
            {
                var hue = hueBase + _activePaletteHueOffsets[i % _activePaletteHueOffsets.Length];
                _snapBorderGradientStops[i].Color = ColorFromHsv(hue, 0.74, 1.0, 255);
                _snapBorderGlowGradientStops[i].Color = ColorFromHsv(hue + 18.0, 0.62, 1.0, 120);
                _arrowBorderGradientStops[i].Color = _snapBorderGradientStops[i].Color;
                _arrowGlowGradientStops[i].Color = _snapBorderGlowGradientStops[i].Color;
            }

            _snapBorderChaseGradientStops[0].Color = Color.FromArgb(0, 255, 255, 255);
            _snapBorderChaseGradientStops[1].Color = ColorFromHsv(hueBase + _activePaletteHueOffsets[1 % _activePaletteHueOffsets.Length], 0.25, 1.0, 96);
            _snapBorderChaseGradientStops[2].Color = Color.FromArgb(255, 255, 255, 255);
            _snapBorderChaseGradientStops[3].Color = ColorFromHsv(hueBase + _activePaletteHueOffsets[3 % _activePaletteHueOffsets.Length], 0.35, 1.0, 180);
            _snapBorderChaseGradientStops[4].Color = Color.FromArgb(0, 255, 255, 255);

            for (var i = 0; i < _arrowChaseGradientStops.Length; i++)
            {
                _arrowChaseGradientStops[i].Color = _snapBorderChaseGradientStops[i].Color;
            }

            var cornerGlowAlpha = (byte)(120 + (Math.Sin(_borderEffectElapsedSeconds * 4.8) * 60.0));
            _snapBorderCornerGlowBrush.Color = ColorFromHsv(hueBase + _activePaletteHueOffsets[4 % _activePaletteHueOffsets.Length], 0.45, 1.0, cornerGlowAlpha);
        }

        /// <summary>Computes straight-arrow head geometry for snap chrome (arrow path only; Live Draw straight line uses uniform <see cref="Line"/> strokes).</summary>
        private static void GetStraightArrowGeometry(
            ArrowLayer layer,
            out double startX,
            out double startY,
            out double tipX,
            out double tipY,
            out double baseX,
            out double baseY,
            out double leftX,
            out double leftY,
            out double rightX,
            out double rightY)
        {
            var thickness = Math.Max(1, layer.Thickness);
            startX = layer.StartX;
            startY = layer.StartY;
            tipX = layer.EndX;
            tipY = layer.EndY;

            var dx = tipX - startX;
            var dy = tipY - startY;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length < 0.001)
            {
                baseX = tipX;
                baseY = tipY;
                leftX = tipX;
                leftY = tipY;
                rightX = tipX;
                rightY = tipY;
                return;
            }

            var unitX = dx / length;
            var unitY = dy / length;
            var normalX = -unitY;
            var normalY = unitX;
            var headLength = Math.Max(10.0, thickness * 3.5);
            var headWidth = Math.Max(8.0, thickness * 3.0);

            baseX = tipX - (unitX * headLength);
            baseY = tipY - (unitY * headLength);
            leftX = baseX + (normalX * (headWidth / 2d));
            leftY = baseY + (normalY * (headWidth / 2d));
            rightX = baseX - (normalX * (headWidth / 2d));
            rightY = baseY - (normalY * (headWidth / 2d));
        }

        private static void ComputeShaftTailPoints(
            ArrowLayer layer,
            double startX,
            double startY,
            double baseX,
            double baseY,
            double leftX,
            double leftY,
            double rightX,
            double rightY,
            out double tailLeftX,
            out double tailLeftY,
            out double tailRightX,
            out double tailRightY)
        {
            var t = Math.Max(1, layer.Thickness);
            var dx = baseX - startX;
            var dy = baseY - startY;
            var len = Math.Sqrt((dx * dx) + (dy * dy));
            if (len < 0.001)
            {
                tailLeftX = startX;
                tailLeftY = startY;
                tailRightX = startX;
                tailRightY = startY;
                return;
            }

            var unitX = dx / len;
            var unitY = dy / len;
            var normalX = -unitY;
            var normalY = unitX;
            var headBaseHalf = 0.5 * Math.Sqrt(((leftX - rightX) * (leftX - rightX)) + ((leftY - rightY) * (leftY - rightY)));
            if (headBaseHalf < 0.001)
            {
                headBaseHalf = t * 0.5;
            }

            var tailHalf = Math.Clamp(t * 0.22, 0.55, headBaseHalf * 0.48);
            tailLeftX = startX + (normalX * tailHalf);
            tailLeftY = startY + (normalY * tailHalf);
            tailRightX = startX - (normalX * tailHalf);
            tailRightY = startY - (normalY * tailHalf);
        }

        private void AddArrowChromeLayers(
            Canvas canvas,
            double startX,
            double startY,
            double tipX,
            double tipY,
            double baseX,
            double baseY,
            double leftX,
            double leftY,
            double rightX,
            double rightY,
            double tailLeftX,
            double tailLeftY,
            double tailRightX,
            double tailRightY,
            out Line chaseShaft,
            out Polygon chaseHead,
            out Line cornerShaft,
            out Polygon cornerHead)
        {
            var p = _borderFxProfile;

            var mainShaft = CreateArrowShaftTaperPolygon(tailLeftX, tailLeftY, tailRightX, tailRightY, rightX, rightY, leftX, leftY, _arrowBorderGradientBrush);
            var glowShaft = CreateArrowShaftTaperPolygon(tailLeftX, tailLeftY, tailRightX, tailRightY, rightX, rightY, leftX, leftY, _arrowGlowGradientBrush);

            var chaseLineThickness = Math.Max(1.1, p.ChaseStrokeThickness * 0.72);
            chaseShaft = CreateArrowShaftLine(startX, startY, baseX, baseY, _arrowChaseGradientBrush, chaseLineThickness, ChaseDashPattern);
            chaseHead = CreateArrowHeadOutlinePolygon(tipX, tipY, leftX, leftY, rightX, rightY, _arrowChaseGradientBrush, p.ChaseStrokeThickness, ChaseDashPattern);
            var cornerLineThickness = Math.Max(1.0, p.CornerGlowStrokeThickness * 0.68);
            cornerShaft = CreateArrowShaftLine(startX, startY, baseX, baseY, _snapBorderCornerGlowBrush, cornerLineThickness, CornerDashPattern);
            cornerHead = CreateArrowHeadOutlinePolygon(tipX, tipY, leftX, leftY, rightX, rightY, _snapBorderCornerGlowBrush, p.CornerGlowStrokeThickness, CornerDashPattern);

            var mainHead = new Polygon
            {
                Fill = _arrowBorderGradientBrush,
                Points = new PointCollection
                {
                    new Point(tipX, tipY),
                    new Point(leftX, leftY),
                    new Point(rightX, rightY)
                }
            };

            var glowHead = CreateArrowHeadOutlinePolygon(tipX, tipY, leftX, leftY, rightX, rightY, _arrowGlowGradientBrush, p.OuterGlowStrokeThickness, null);

            canvas.Children.Add(mainShaft);
            canvas.Children.Add(mainHead);
            canvas.Children.Add(chaseShaft);
            canvas.Children.Add(chaseHead);
            canvas.Children.Add(cornerShaft);
            canvas.Children.Add(cornerHead);
            canvas.Children.Add(glowShaft);
            canvas.Children.Add(glowHead);
        }

        /// <summary>
        /// Straight segment with uniform stroke thickness (no arrow-shaft taper polygons). Layering: glow, main, dashed chase, dashed corner.
        /// </summary>
        private void AddLineChromeLayers(
            Canvas canvas,
            ArrowLayer layer,
            double startX,
            double startY,
            double tipX,
            double tipY,
            out Line chaseLine,
            out Line cornerLine)
        {
            var p = _borderFxProfile;
            var mainThickness = Math.Max(1, layer.Thickness);
            var glowThickness = Math.Max(mainThickness + 3, p.OuterGlowStrokeThickness);

            var glowLine = CreateArrowShaftLine(startX, startY, tipX, tipY, _arrowGlowGradientBrush, glowThickness, null);
            var mainLine = CreateArrowShaftLine(startX, startY, tipX, tipY, _arrowBorderGradientBrush, mainThickness, null);

            var chaseLineThickness = Math.Max(1.1, p.ChaseStrokeThickness * 0.72);
            chaseLine = CreateArrowShaftLine(startX, startY, tipX, tipY, _arrowChaseGradientBrush, chaseLineThickness, ChaseDashPattern);
            var cornerLineThickness = Math.Max(1.0, p.CornerGlowStrokeThickness * 0.68);
            cornerLine = CreateArrowShaftLine(startX, startY, tipX, tipY, _snapBorderCornerGlowBrush, cornerLineThickness, CornerDashPattern);

            canvas.Children.Add(glowLine);
            canvas.Children.Add(mainLine);
            canvas.Children.Add(chaseLine);
            canvas.Children.Add(cornerLine);
        }

        private static Polygon CreateArrowShaftTaperPolygon(
            double tailLeftX,
            double tailLeftY,
            double tailRightX,
            double tailRightY,
            double headRightX,
            double headRightY,
            double headLeftX,
            double headLeftY,
            Brush fill)
        {
            return new Polygon
            {
                Fill = fill,
                Stroke = null,
                Points = new PointCollection
                {
                    new Point(tailLeftX, tailLeftY),
                    new Point(tailRightX, tailRightY),
                    new Point(headRightX, headRightY),
                    new Point(headLeftX, headLeftY)
                }
            };
        }

        private static Line CreateArrowShaftLine(
            double x1,
            double y1,
            double x2,
            double y2,
            Brush stroke,
            double thickness,
            DoubleCollection? dash)
        {
            var line = new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };

            if (dash is not null)
            {
                line.StrokeDashArray = CloneDashArray(dash);
            }

            return line;
        }

        private static Polygon CreateArrowHeadOutlinePolygon(
            double tipX,
            double tipY,
            double leftX,
            double leftY,
            double rightX,
            double rightY,
            Brush stroke,
            double thickness,
            DoubleCollection? dash)
        {
            var poly = new Polygon
            {
                Fill = null,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Points = new PointCollection
                {
                    new Point(tipX, tipY),
                    new Point(leftX, leftY),
                    new Point(rightX, rightY)
                }
            };

            if (dash is not null)
            {
                poly.StrokeDashArray = CloneDashArray(dash);
            }

            return poly;
        }

        private static void ApplyRectangleGeometry(FrameworkElement rectangle, double x, double y, double width, double height)
        {
            Canvas.SetLeft(rectangle, x);
            Canvas.SetTop(rectangle, y);
            rectangle.Width = Math.Max(1, width);
            rectangle.Height = Math.Max(1, height);
        }

        private static void ApplyEllipseGeometry(FrameworkElement ellipse, double x, double y, double width, double height)
        {
            Canvas.SetLeft(ellipse, x);
            Canvas.SetTop(ellipse, y);
            ellipse.Width = Math.Max(1, width);
            ellipse.Height = Math.Max(1, height);
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
    }

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
