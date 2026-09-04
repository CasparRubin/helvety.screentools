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
            arrowLayer.GetControlPoint(out var controlX, out var controlY);
            controlX += offsetX;
            controlY += offsetY;
            var isStraight = arrowLayer.IsStraight;

            var chordDx = tipX - startX;
            var chordDy = tipY - startY;
            var chordLength = Math.Sqrt((chordDx * chordDx) + (chordDy * chordDy));
            if (chordLength < 0.001)
            {
                yield break;
            }

            if (arrowLayer.FormStyle == ArrowFormStyle.LineOnly)
            {
                if (isStraight)
                {
                    yield return CreateStraightShaftLine(startX, startY, tipX, tipY, color, thickness);
                }
                else
                {
                    yield return CreateQuadraticStrokePath(startX, startY, controlX, controlY, tipX, tipY, color, thickness);
                }

                yield break;
            }

            if (!TryGetTipTangent(startX, startY, controlX, controlY, tipX, tipY, isStraight, out var unitX, out var unitY))
            {
                yield break;
            }

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
            var shaftEndT = isStraight
                ? 1d
                : FindDistanceFromTipT(
                    startX,
                    startY,
                    controlX,
                    controlY,
                    tipX,
                    tipY,
                    Math.Max(0, headLength - shaftHeadOverlap));

            if (isTapered)
            {
                var shaftPath = isStraight
                    ? CreateTaperedShaftPath(
                        startX,
                        startY,
                        shaftEndX,
                        shaftEndY,
                        normalX,
                        normalY,
                        thickness,
                        widthExpansion,
                        color,
                        useUniformSegmentAlpha)
                    : CreateTaperedShaftPathAlongCurve(
                        startX,
                        startY,
                        controlX,
                        controlY,
                        tipX,
                        tipY,
                        shaftEndT,
                        thickness,
                        widthExpansion,
                        color,
                        useUniformSegmentAlpha);
                if (shaftPath is not null)
                {
                    yield return shaftPath;
                }
            }
            else if (isStraight)
            {
                yield return CreateStraightShaftLine(startX, startY, shaftEndX, shaftEndY, color, thickness);
            }
            else
            {
                SplitQuadratic(startX, startY, controlX, controlY, tipX, tipY, shaftEndT, out var splitControlX, out var splitControlY, out var splitEndX, out var splitEndY);
                yield return CreateQuadraticStrokePath(
                    startX,
                    startY,
                    splitControlX,
                    splitControlY,
                    splitEndX,
                    splitEndY,
                    color,
                    thickness);
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

        private static Line CreateStraightShaftLine(
            double startX,
            double startY,
            double endX,
            double endY,
            Color color,
            double thickness)
        {
            return new Line
            {
                X1 = startX,
                Y1 = startY,
                X2 = endX,
                Y2 = endY,
                Stroke = EditorXamlResourceCache.GetBrush(color),
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
        }

        private static Path CreateQuadraticStrokePath(
            double startX,
            double startY,
            double controlX,
            double controlY,
            double endX,
            double endY,
            Color color,
            double thickness)
        {
            var figure = new PathFigure
            {
                StartPoint = new Point(startX, startY),
                IsFilled = false,
                IsClosed = false
            };
            figure.Segments.Add(new QuadraticBezierSegment
            {
                Point1 = new Point(controlX, controlY),
                Point2 = new Point(endX, endY)
            });
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return new Path
            {
                Data = geometry,
                Stroke = EditorXamlResourceCache.GetBrush(color),
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
        }

        private static bool TryGetTipTangent(
            double startX,
            double startY,
            double controlX,
            double controlY,
            double tipX,
            double tipY,
            bool isStraight,
            out double unitX,
            out double unitY)
        {
            double dx;
            double dy;
            if (isStraight)
            {
                dx = tipX - startX;
                dy = tipY - startY;
            }
            else
            {
                dx = tipX - controlX;
                dy = tipY - controlY;
            }

            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length < 0.001)
            {
                dx = tipX - startX;
                dy = tipY - startY;
                length = Math.Sqrt((dx * dx) + (dy * dy));
            }

            if (length < 0.001)
            {
                unitX = 0;
                unitY = 0;
                return false;
            }

            unitX = dx / length;
            unitY = dy / length;
            return true;
        }

        private static double FindDistanceFromTipT(
            double p0x,
            double p0y,
            double p1x,
            double p1y,
            double p2x,
            double p2y,
            double distanceFromTip)
        {
            if (distanceFromTip <= 0)
            {
                return 1d;
            }

            const int samples = 24;
            var prevX = p2x;
            var prevY = p2y;
            var prevT = 1d;
            var remaining = distanceFromTip;
            for (var i = samples - 1; i >= 0; i--)
            {
                var t = i / (double)samples;
                ArrowLayer.EvaluateQuadratic(p0x, p0y, p1x, p1y, p2x, p2y, t, out var x, out var y);
                var dx = prevX - x;
                var dy = prevY - y;
                var segmentLength = Math.Sqrt((dx * dx) + (dy * dy));
                if (segmentLength >= remaining)
                {
                    var fraction = segmentLength < 0.0001 ? 0d : remaining / segmentLength;
                    return prevT - (fraction * (prevT - t));
                }

                remaining -= segmentLength;
                prevX = x;
                prevY = y;
                prevT = t;
            }

            return 0d;
        }

        private static void SplitQuadratic(
            double p0x,
            double p0y,
            double p1x,
            double p1y,
            double p2x,
            double p2y,
            double t,
            out double controlX,
            out double controlY,
            out double endX,
            out double endY)
        {
            controlX = p0x + ((p1x - p0x) * t);
            controlY = p0y + ((p1y - p0y) * t);
            ArrowLayer.EvaluateQuadratic(p0x, p0y, p1x, p1y, p2x, p2y, t, out endX, out endY);
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

        private static Path? CreateTaperedShaftPathAlongCurve(
            double p0x,
            double p0y,
            double p1x,
            double p1y,
            double p2x,
            double p2y,
            double shaftEndT,
            double thickness,
            double widthExpansion,
            Color color,
            bool useUniformSegmentAlpha)
        {
            if (shaftEndT < 0.001)
            {
                return null;
            }

            var shaftLength = EstimateQuadraticLength(p0x, p0y, p1x, p1y, p2x, p2y, shaftEndT);
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
                var t = shaftEndT * (i / (double)segmentCount);
                var easedT = Math.Pow(i / (double)segmentCount, 1.15);
                var halfWidth = tailHalfWidth + ((nearHeadHalfWidth - tailHalfWidth) * easedT);
                ArrowLayer.EvaluateQuadratic(p0x, p0y, p1x, p1y, p2x, p2y, t, out var x, out var y);
                ArrowLayer.EvaluateQuadraticTangent(p0x, p0y, p1x, p1y, p2x, p2y, t, out var dx, out var dy);
                var tangentLength = Math.Sqrt((dx * dx) + (dy * dy));
                double normalX;
                double normalY;
                if (tangentLength < 0.0001)
                {
                    normalX = 0;
                    normalY = 0;
                }
                else
                {
                    normalX = -dy / tangentLength;
                    normalY = dx / tangentLength;
                }

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

        private static double EstimateQuadraticLength(
            double p0x,
            double p0y,
            double p1x,
            double p1y,
            double p2x,
            double p2y,
            double endT)
        {
            const int samples = 8;
            ArrowLayer.EvaluateQuadratic(p0x, p0y, p1x, p1y, p2x, p2y, 0, out var prevX, out var prevY);
            var length = 0d;
            for (var i = 1; i <= samples; i++)
            {
                var t = endT * (i / (double)samples);
                ArrowLayer.EvaluateQuadratic(p0x, p0y, p1x, p1y, p2x, p2y, t, out var x, out var y);
                var dx = x - prevX;
                var dy = y - prevY;
                length += Math.Sqrt((dx * dx) + (dy * dy));
                prevX = x;
                prevY = y;
            }

            return length;
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
