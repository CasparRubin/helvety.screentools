using System;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace helvety.screentools.Editor
{
    internal static class ArrowRendering
    {
        internal static void DrawArrowLayer(ArrowLayer arrowLayer, bool suppressExpensiveEffects, Canvas targetCanvas)
        {
            var baseThickness = Math.Max(1, arrowLayer.Thickness);
            if (!suppressExpensiveEffects &&
                arrowLayer.FormStyle != ArrowFormStyle.Tapered &&
                arrowLayer.HasShadow)
            {
                DrawFeatheredArrowShadow(arrowLayer, baseThickness, targetCanvas);
            }

            if (!suppressExpensiveEffects && arrowLayer.HasBorder)
            {
                var borderThickness = Clamp(arrowLayer.BorderThickness, 1, 8);
                DrawArrowPrimitive(
                    arrowLayer,
                    ParseColor(arrowLayer.BorderColorHex),
                    baseThickness + (borderThickness * 2),
                    0,
                    0,
                    targetCanvas);
            }

            DrawArrowPrimitive(arrowLayer, ParseColor(arrowLayer.ColorHex), baseThickness, 0, 0, targetCanvas);
        }

        private static void DrawFeatheredArrowShadow(ArrowLayer arrowLayer, double baseThickness, Canvas targetCanvas)
        {
            var shadowColor = ParseColor(arrowLayer.ShadowColorHex);
            var shadowOffset = Math.Max(1, arrowLayer.ShadowOffset);
            var shadowSteps = new (int delta, double alphaScale, double thicknessScale)[] { (0, 1.0, 1.0), (1, 0.58, 1.22), (2, 0.34, 1.48) };
            foreach (var step in shadowSteps)
            {
                var offset = shadowOffset + step.delta;
                DrawArrowPrimitive(
                    arrowLayer,
                    ScaleColorAlpha(shadowColor, step.alphaScale),
                    Math.Max(1, baseThickness * step.thicknessScale),
                    offset,
                    offset,
                    targetCanvas);
            }
        }

        private static Color ScaleColorAlpha(Color color, double factor)
        {
            var alpha = (byte)Math.Clamp((int)Math.Round(color.A * factor), 0, 255);
            return ColorHelper.FromArgb(alpha, color.R, color.G, color.B);
        }

        private static void DrawArrowPrimitive(ArrowLayer arrowLayer, Color color, double thickness, double offsetX, double offsetY, Canvas targetCanvas)
        {
            var strokeBrush = new SolidColorBrush(color);
            var startX = arrowLayer.StartX + offsetX;
            var startY = arrowLayer.StartY + offsetY;
            var tipX = arrowLayer.EndX + offsetX;
            var tipY = arrowLayer.EndY + offsetY;

            var dx = tipX - startX;
            var dy = tipY - startY;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length < 0.001)
            {
                return;
            }

            if (arrowLayer.FormStyle == ArrowFormStyle.LineOnly)
            {
                targetCanvas.Children.Add(new Line
                {
                    X1 = startX,
                    Y1 = startY,
                    X2 = tipX,
                    Y2 = tipY,
                    Stroke = strokeBrush,
                    StrokeThickness = thickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
                return;
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
            var leftX = baseX + (normalX * (headWidth / 2d));
            var leftY = baseY + (normalY * (headWidth / 2d));
            var rightX = baseX - (normalX * (headWidth / 2d));
            var rightY = baseY - (normalY * (headWidth / 2d));

            if (isTapered)
            {
                var tailHalfWidth = Math.Max(0.2, thickness * 0.12);
                var nearHeadHalfWidth = Math.Max(tailHalfWidth + 1.4, Math.Min((headWidth / 2d) - 0.9, thickness * 1.95));
                var shaftDx = baseX - startX;
                var shaftDy = baseY - startY;
                var shaftLength = Math.Sqrt((shaftDx * shaftDx) + (shaftDy * shaftDy));
                var segmentCount = Math.Max(12, Math.Min(24, (int)Math.Round(shaftLength / 10d)));
                for (var i = 0; i < segmentCount; i++)
                {
                    var t0 = i / (double)segmentCount;
                    var t1 = (i + 1) / (double)segmentCount;
                    var easedT0 = Math.Pow(t0, 1.15);
                    var easedT1 = Math.Pow(t1, 1.15);
                    var width0 = tailHalfWidth + ((nearHeadHalfWidth - tailHalfWidth) * easedT0);
                    var width1 = tailHalfWidth + ((nearHeadHalfWidth - tailHalfWidth) * easedT1);

                    var x0 = startX + (shaftDx * t0);
                    var y0 = startY + (shaftDy * t0);
                    var x1 = startX + (shaftDx * t1);
                    var y1 = startY + (shaftDy * t1);

                    var alphaT = Math.Pow((t0 + t1) / 2d, 1.45);
                    var segmentAlpha = (byte)Math.Clamp((int)Math.Round(color.A * alphaT), 0, color.A);
                    if (segmentAlpha == 0)
                    {
                        continue;
                    }

                    targetCanvas.Children.Add(new Polygon
                    {
                        Fill = new SolidColorBrush(WithAlpha(color, segmentAlpha)),
                        Points = new PointCollection
                        {
                            new Point(x0 + (normalX * width0), y0 + (normalY * width0)),
                            new Point(x1 + (normalX * width1), y1 + (normalY * width1)),
                            new Point(x1 - (normalX * width1), y1 - (normalY * width1)),
                            new Point(x0 - (normalX * width0), y0 - (normalY * width0))
                        }
                    });
                }
            }
            else
            {
                targetCanvas.Children.Add(new Line
                {
                    X1 = startX,
                    Y1 = startY,
                    X2 = baseX,
                    Y2 = baseY,
                    Stroke = strokeBrush,
                    StrokeThickness = thickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
            }

            targetCanvas.Children.Add(new Polygon
            {
                Fill = strokeBrush,
                Stroke = strokeBrush,
                StrokeThickness = 1,
                Points = new PointCollection
                {
                    new Point(tipX, tipY),
                    new Point(leftX, leftY),
                    new Point(rightX, rightY)
                }
            });
        }

        private static Color WithAlpha(Color color, byte alpha)
        {
            return ColorHelper.FromArgb(alpha, color.R, color.G, color.B);
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static Color ParseColor(string colorHex)
        {
            if (string.IsNullOrWhiteSpace(colorHex))
            {
                return Colors.White;
            }

            var value = colorHex.Trim().TrimStart('#');
            if (value.Length == 6)
            {
                var rgb = Convert.ToUInt32(value, 16);
                var r = (byte)((rgb & 0xFF0000) >> 16);
                var g = (byte)((rgb & 0x00FF00) >> 8);
                var b = (byte)(rgb & 0x0000FF);
                return ColorHelper.FromArgb(255, r, g, b);
            }

            if (value.Length == 8)
            {
                var argb = Convert.ToUInt32(value, 16);
                var a = (byte)((argb & 0xFF000000) >> 24);
                var r = (byte)((argb & 0x00FF0000) >> 16);
                var g = (byte)((argb & 0x0000FF00) >> 8);
                var b = (byte)(argb & 0x000000FF);
                return ColorHelper.FromArgb(a, r, g, b);
            }

            return Colors.White;
        }
    }
}
