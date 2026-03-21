using System;
using System.Collections.Generic;
using System.Numerics;
using helvety.screentools.Editor;
using Microsoft.UI.Composition;
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
        private ScalarKeyFrameAnimation? _borderOpacityAnimation;
        private Vector3KeyFrameAnimation? _borderScaleAnimation;
        private ScalarKeyFrameAnimation? _borderGlowOpacityAnimation;
        private Vector3KeyFrameAnimation? _borderGlowScaleAnimation;
        private ScalarKeyFrameAnimation? _borderChaseOpacityAnimation;
        private Vector3KeyFrameAnimation? _borderChaseScaleAnimation;
        private bool _isBorderAnimationRunning;
        private readonly List<CommittedSnapBorderGroup> _committedGroups = new();
        private readonly List<CommittedSnapArrowGroup> _committedArrowGroups = new();
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

        internal SnapBorderChromeController(
            Grid rootGrid,
            Rectangle snapBorderRectangle,
            Rectangle snapBorderChaseRectangle,
            Rectangle snapBorderCornerGlowRectangle,
            Rectangle snapBorderGlowRectangle,
            Canvas overlayCanvas)
        {
            _rootGrid = rootGrid;
            _snapBorderRectangle = snapBorderRectangle;
            _snapBorderChaseRectangle = snapBorderChaseRectangle;
            _snapBorderCornerGlowRectangle = snapBorderCornerGlowRectangle;
            _snapBorderGlowRectangle = snapBorderGlowRectangle;
            _overlayCanvas = overlayCanvas;

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

            PickNextBorderPalette();
        }

        internal void InitializeCompositionAnimations()
        {
            _compositor = ElementCompositionPreview.GetElementVisual(_rootGrid).Compositor;
            _snapBorderVisual = ElementCompositionPreview.GetElementVisual(_snapBorderRectangle);
            _snapBorderChaseVisual = ElementCompositionPreview.GetElementVisual(_snapBorderChaseRectangle);
            _snapBorderCornerGlowVisual = ElementCompositionPreview.GetElementVisual(_snapBorderCornerGlowRectangle);
            _snapBorderGlowVisual = ElementCompositionPreview.GetElementVisual(_snapBorderGlowRectangle);

            _borderOpacityAnimation = CreateBorderOpacityAnimation();
            _borderScaleAnimation = CreateBorderScaleAnimation();
            _borderGlowOpacityAnimation = CreateGlowOpacityAnimation();
            _borderGlowScaleAnimation = CreateGlowScaleAnimation();
            _borderChaseOpacityAnimation = CreateChaseOpacityAnimation();
            _borderChaseScaleAnimation = CreateChaseScaleAnimation();

            UpdateAnimatedBorderBrushes();
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

        internal void SetSnapBorderLayersVisible(bool isVisible)
        {
            var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            _snapBorderRectangle.Visibility = visibility;
            _snapBorderChaseRectangle.Visibility = visibility;
            _snapBorderCornerGlowRectangle.Visibility = visibility;
            _snapBorderGlowRectangle.Visibility = visibility;
        }

        internal void StartSnapBorderAnimations()
        {
            if (_isBorderAnimationRunning || _snapBorderVisual is null || _borderOpacityAnimation is null || _borderScaleAnimation is null)
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

        internal void UpdateChaseSpeedForArrowLength(double length)
        {
            var normalizedSize = Math.Clamp((length - 120.0) / 1600.0, 0.0, 1.0);
            var speedScale = 0.75 + (normalizedSize * 0.95);
            _currentDashSpeedUnitsPerSecond = _borderFxProfile.DashSpeedUnitsPerSecond * speedScale;
        }

        /// <summary>Live Draw arrow preview: same gradient / dash palette as snap rectangles.</summary>
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

        /// <summary>Commits a Live Draw arrow with the same animated chrome as rectangles (gradient drift, dashing, pulse).</summary>
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

            var v = ElementCompositionPreview.GetElementVisual(container);
            if (v is not null)
            {
                v.CenterPoint = new Vector3((float)(bw / 2.0), (float)(bh / 2.0), 0f);
                v.StartAnimation("Opacity", CreateBorderOpacityAnimation());
                v.StartAnimation("Scale", CreateBorderScaleAnimation());
            }

            _committedArrowGroups.Add(new CommittedSnapArrowGroup(chaseShaft, chaseHead, cornerShaft, cornerHead));
        }

        private void UpdateTravelingHighlight(double deltaSeconds)
        {
            var dashDelta = _currentDashSpeedUnitsPerSecond * deltaSeconds;
            foreach (var g in _committedGroups)
            {
                g.Chase.StrokeDashOffset = g.Chase.StrokeDashOffset - dashDelta;
                g.CornerGlow.StrokeDashOffset = g.CornerGlow.StrokeDashOffset + (dashDelta * 0.6);
            }

            foreach (var a in _committedArrowGroups)
            {
                a.ChaseShaft.StrokeDashOffset -= dashDelta;
                a.ChaseHead.StrokeDashOffset -= dashDelta;
                a.CornerShaft.StrokeDashOffset += dashDelta * 0.6;
                a.CornerHead.StrokeDashOffset += dashDelta * 0.6;
            }

            if (!_isBorderAnimationRunning)
            {
                return;
            }

            _snapBorderChaseRectangle.StrokeDashOffset = _snapBorderChaseRectangle.StrokeDashOffset - dashDelta;
            _snapBorderCornerGlowRectangle.StrokeDashOffset = _snapBorderCornerGlowRectangle.StrokeDashOffset + (dashDelta * 0.6);
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
