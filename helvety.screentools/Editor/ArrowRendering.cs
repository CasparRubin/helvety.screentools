using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace helvety.screentools.Editor
{
    /// <summary>Draws image-editor arrow layers (tapered, straight, or line-only) on a XAML <see cref="Canvas"/>.</summary>
    internal static class ArrowRendering
    {
        internal static IReadOnlyList<UIElement> CreateArrowElements(ArrowLayer arrowLayer, bool suppressExpensiveEffects)
        {
            var elements = new List<UIElement>();
            var baseThickness = Math.Max(1, arrowLayer.Thickness);
            if (!suppressExpensiveEffects &&
                arrowLayer.FormStyle != ArrowFormStyle.Tapered &&
                arrowLayer.HasShadow)
            {
                elements.AddRange(CreateFeatheredArrowShadowElements(arrowLayer, baseThickness));
            }

            if (!suppressExpensiveEffects && arrowLayer.HasBorder)
            {
                var borderThickness = Clamp(arrowLayer.BorderThickness, 1, 8);
                var isTapered = arrowLayer.FormStyle == ArrowFormStyle.Tapered;
                elements.AddRange(CreateArrowPrimitiveElements(
                    arrowLayer,
                    EditorXamlResourceCache.ParseColor(arrowLayer.BorderColorHex),
                    isTapered ? baseThickness : baseThickness + borderThickness,
                    isTapered ? borderThickness : 0,
                    0,
                    0,
                    useUniformSegmentAlpha: isTapered));
            }

            elements.AddRange(CreateArrowPrimitiveElements(
                arrowLayer,
                EditorXamlResourceCache.ParseColor(arrowLayer.ColorHex),
                baseThickness,
                0,
                0,
                0,
                useUniformSegmentAlpha: false));

            return elements;
        }

        internal static void DrawArrowLayer(ArrowLayer arrowLayer, bool suppressExpensiveEffects, Canvas targetCanvas)
        {
            foreach (var element in CreateArrowElements(arrowLayer, suppressExpensiveEffects))
            {
                targetCanvas.Children.Add(element);
            }
        }

        private static IEnumerable<UIElement> CreateFeatheredArrowShadowElements(ArrowLayer arrowLayer, double baseThickness)
        {
            var shadowColor = EditorXamlResourceCache.ParseColor(arrowLayer.ShadowColorHex);
            var shadowOffset = Math.Max(1, arrowLayer.ShadowOffset);
            var shadowSteps = new (int delta, double alphaScale, double thicknessScale)[] { (0, 1.0, 1.0), (1, 0.58, 1.22), (2, 0.34, 1.48) };
            foreach (var step in shadowSteps)
            {
                var offset = shadowOffset + step.delta;
                foreach (var element in CreateArrowPrimitiveElements(
                    arrowLayer,
                    ScaleColorAlpha(shadowColor, step.alphaScale),
                    Math.Max(1, baseThickness * step.thicknessScale),
                    0,
                    offset,
                    offset,
                    useUniformSegmentAlpha: false))
                {
                    yield return element;
                }
            }
        }

        private static IEnumerable<UIElement> CreateArrowPrimitiveElements(
            ArrowLayer arrowLayer,
            Color color,
            double thickness,
            double widthExpansion,
            double offsetX,
            double offsetY,
            bool useUniformSegmentAlpha)
        {
            var startX = arrowLayer.StartX + offsetX;
            var startY = arrowLayer.StartY + offsetY;
            var tipX = arrowLayer.EndX + offsetX;
            var tipY = arrowLayer.EndY + offsetY;

            var dx = tipX - startX;
            var dy = tipY - startY;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length < 0.001)
            {
                yield break;
            }

            if (arrowLayer.FormStyle == ArrowFormStyle.LineOnly)
            {
                yield return new Line
                {
                    X1 = startX,
                    Y1 = startY,
                    X2 = tipX,
                    Y2 = tipY,
                    Stroke = EditorXamlResourceCache.GetBrush(color),
                    StrokeThickness = thickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                yield break;
            }

            var unitX = dx / length;
            var unitY = dy / length;
            var normalX = -unitY;
            var normalY = unitX;
            var isTapered = arrowLayer.FormStyle == ArrowFormStyle.Tapered;
            var headLength = isTapered
                ? Math.Max(12.0, thickness * 4.0)
                : Math.Max(10.0, thickness * 3.5);
            var headWidth = isTapered
                ? Math.Max(14.0, thickness * 5.2)
                : Math.Max(8.0, thickness * 3.0);

            var baseX = tipX - (unitX * headLength);
            var baseY = tipY - (unitY * headLength);
            const double shaftHeadOverlap = 1.5;
            var shaftEndX = baseX + (unitX * shaftHeadOverlap);
            var shaftEndY = baseY + (unitY * shaftHeadOverlap);

            if (isTapered)
            {
                var shaftPath = CreateTaperedShaftPath(
                    startX,
                    startY,
                    shaftEndX,
                    shaftEndY,
                    normalX,
                    normalY,
                    thickness,
                    widthExpansion,
                    color,
                    useUniformSegmentAlpha);
                if (shaftPath is not null)
                {
                    yield return shaftPath;
                }
            }
            else
            {
                yield return new Line
                {
                    X1 = startX,
                    Y1 = startY,
                    X2 = shaftEndX,
                    Y2 = shaftEndY,
                    Stroke = EditorXamlResourceCache.GetBrush(color),
                    StrokeThickness = thickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
            }

            var headHalfWidth = (headWidth / 2d) + widthExpansion;
            var headLeftX = baseX + (normalX * headHalfWidth);
            var headLeftY = baseY + (normalY * headHalfWidth);
            var headRightX = baseX - (normalX * headHalfWidth);
            var headRightY = baseY - (normalY * headHalfWidth);

            yield return new Polygon
            {
                Fill = EditorXamlResourceCache.GetBrush(color),
                Points = new PointCollection
                {
                    new Point(tipX, tipY),
                    new Point(headLeftX, headLeftY),
                    new Point(headRightX, headRightY)
                }
            };
        }

        private static Path? CreateTaperedShaftPath(
            double startX,
            double startY,
            double baseX,
            double baseY,
            double normalX,
            double normalY,
            double thickness,
            double widthExpansion,
            Color color,
            bool useUniformSegmentAlpha)
        {
            var shaftDx = baseX - startX;
            var shaftDy = baseY - startY;
            var shaftLength = Math.Sqrt((shaftDx * shaftDx) + (shaftDy * shaftDy));
            if (shaftLength < 0.001)
            {
                return null;
            }

            var tailHalfWidth = Math.Max(0.2, thickness * 0.12) + widthExpansion;
            var headWidth = Math.Max(14.0, thickness * 5.2);
            var nearHeadHalfWidth = Math.Max(tailHalfWidth + 1.4, Math.Min((headWidth / 2d) - 0.9, thickness * 1.95)) + widthExpansion;
            var segmentCount = Math.Max(8, Math.Min(16, (int)Math.Round(shaftLength / 12d)));

            var geometry = new PathGeometry();
            var figure = new PathFigure { IsClosed = true };
            var topPoints = new List<Point>(segmentCount + 1);
            var bottomPoints = new List<Point>(segmentCount + 1);

            for (var i = 0; i <= segmentCount; i++)
            {
                var t = i / (double)segmentCount;
                var easedT = Math.Pow(t, 1.15);
                var halfWidth = tailHalfWidth + ((nearHeadHalfWidth - tailHalfWidth) * easedT);
                var x = startX + (shaftDx * t);
                var y = startY + (shaftDy * t);
                topPoints.Add(new Point(x + (normalX * halfWidth), y + (normalY * halfWidth)));
                bottomPoints.Add(new Point(x - (normalX * halfWidth), y - (normalY * halfWidth)));
            }

            figure.StartPoint = topPoints[0];
            for (var i = 1; i < topPoints.Count; i++)
            {
                figure.Segments.Add(new LineSegment { Point = topPoints[i] });
            }

            for (var i = bottomPoints.Count - 1; i >= 0; i--)
            {
                figure.Segments.Add(new LineSegment { Point = bottomPoints[i] });
            }

            geometry.Figures.Add(figure);

            var fillColor = useUniformSegmentAlpha
                ? color
                : ScaleColorAlpha(color, Math.Clamp(color.A / 255d, 0.35, 1.0));

            return new Path
            {
                Data = geometry,
                Fill = EditorXamlResourceCache.GetBrush(fillColor)
            };
        }

        private static Color ScaleColorAlpha(Color color, double factor)
        {
            var alpha = (byte)Math.Clamp((int)Math.Round(color.A * factor), 0, 255);
            return ColorHelper.FromArgb(alpha, color.R, color.G, color.B);
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
