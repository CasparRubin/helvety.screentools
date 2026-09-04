using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;
using UiFontStyle = Windows.UI.Text.FontStyle;

namespace helvety.screentools.Editor
{
    /// <summary>
    /// Builds XAML elements for text, border, arrow, and magnifier layers. Used by <see cref="VectorCanvasHost"/> and overlay preview.
    /// </summary>
    internal static class EditorVectorOverlayRenderer
    {
        private const int MaxPrimaryThickness = 24;
        private const double MagnifierConnectorMinLength = 2.0;

        internal static IReadOnlyList<UIElement> CreateElementsForLayer(EditorLayer layer, bool suppressExpensiveEffects)
        {
            return layer switch
            {
                TextLayer textLayer => CreateTextElements(textLayer, suppressExpensiveEffects),
                BorderLayer borderLayer => CreateBorderElements(borderLayer, suppressExpensiveEffects),
                ArrowLayer arrowLayer => ArrowRendering.CreateArrowElements(arrowLayer, suppressExpensiveEffects),
                MagnifierLayer magnifierLayer => CreateMagnifierElements(magnifierLayer, suppressExpensiveEffects),
                _ => Array.Empty<UIElement>()
            };
        }

        private static List<UIElement> CreateTextElements(TextLayer textLayer, bool suppressExpensiveEffects)
        {
            var elements = new List<UIElement>();
            if (!suppressExpensiveEffects && textLayer.HasShadow)
            {
                elements.AddRange(CreateFeatheredTextShadowElements(textLayer));
            }

            if (!suppressExpensiveEffects && textLayer.HasBorder)
            {
                elements.AddRange(CreateTextOutlineElements(textLayer));
            }

            elements.Add(CreateMainTextBlock(textLayer));
            return elements;
        }

        private static IEnumerable<UIElement> CreateTextOutlineElements(TextLayer textLayer)
        {
            var thickness = Math.Clamp(textLayer.BorderThickness, 1, MaxPrimaryThickness);
            var outlineBrush = EditorXamlResourceCache.GetBrush(textLayer.BorderColorHex);
            var offsets = GetOutlineOffsets(thickness);
            foreach (var (offsetX, offsetY) in offsets)
            {
                var outline = CreateTextBlock(textLayer, outlineBrush);
                Canvas.SetLeft(outline, textLayer.X + offsetX);
                Canvas.SetTop(outline, textLayer.Y + offsetY);
                yield return outline;
            }
        }

        private static IEnumerable<(int offsetX, int offsetY)> GetOutlineOffsets(int thickness)
        {
            var step = thickness <= 8 ? 1 : 2;
            for (var offsetY = -thickness; offsetY <= thickness; offsetY += step)
            {
                for (var offsetX = -thickness; offsetX <= thickness; offsetX += step)
                {
                    if (offsetX == 0 && offsetY == 0)
                    {
                        continue;
                    }

                    if ((offsetX * offsetX) + (offsetY * offsetY) > (thickness * thickness))
                    {
                        continue;
                    }

                    yield return (offsetX, offsetY);
                }
            }
        }

        private static List<UIElement> CreateFeatheredTextShadowElements(TextLayer textLayer)
        {
            var elements = new List<UIElement>();
            var shadowColor = EditorXamlResourceCache.ParseColor(textLayer.ShadowColorHex);
            var shadowOffset = Math.Max(1, textLayer.ShadowOffset);
            var shadowSteps = new (int delta, double alphaScale)[] { (0, 1.0), (1, 0.58), (2, 0.32) };
            foreach (var step in shadowSteps)
            {
                var shadow = CreateTextBlock(
                    textLayer,
                    EditorXamlResourceCache.GetBrush(ScaleColorAlpha(shadowColor, step.alphaScale)));
                var offset = shadowOffset + step.delta;
                Canvas.SetLeft(shadow, textLayer.X + offset);
                Canvas.SetTop(shadow, textLayer.Y + offset);
                elements.Add(shadow);
            }

            return elements;
        }

        private static TextBlock CreateMainTextBlock(TextLayer textLayer)
        {
            var mainText = CreateTextBlock(textLayer, EditorXamlResourceCache.GetBrush(textLayer.ColorHex));
            Canvas.SetLeft(mainText, textLayer.X);
            Canvas.SetTop(mainText, textLayer.Y);
            return mainText;
        }

        private static TextBlock CreateTextBlock(TextLayer textLayer, SolidColorBrush foreground)
        {
            var textBlock = new TextBlock
            {
                Text = textLayer.Text,
                FontSize = textLayer.FontSize,
                FontFamily = EditorXamlResourceCache.GetFontFamily(textLayer.FontFamily),
                Foreground = foreground,
                Width = Math.Max(1, textLayer.WrapWidth),
                TextWrapping = TextWrapping.Wrap
            };
            ApplyTextStyleToBlock(textBlock, textLayer);
            return textBlock;
        }

        private static List<UIElement> CreateBorderElements(BorderLayer borderLayer, bool suppressExpensiveEffects)
        {
            var elements = new List<UIElement>();
            var cornerRadius = GetEffectiveCornerRadius(borderLayer.Region, borderLayer.CornerRadius);
            if (!suppressExpensiveEffects && borderLayer.HasShadow)
            {
                elements.AddRange(CreateFeatheredBorderShadowElements(borderLayer, cornerRadius));
            }

            var borderRect = new Rectangle
            {
                Width = borderLayer.Region.Width,
                Height = borderLayer.Region.Height,
                Stroke = EditorXamlResourceCache.GetBrush(borderLayer.ColorHex),
                StrokeThickness = borderLayer.Thickness,
                RadiusX = cornerRadius,
                RadiusY = cornerRadius
            };
            Canvas.SetLeft(borderRect, borderLayer.Region.X);
            Canvas.SetTop(borderRect, borderLayer.Region.Y);
            elements.Add(borderRect);
            return elements;
        }

        private static List<UIElement> CreateMagnifierElements(MagnifierLayer magnifier, bool suppressExpensiveEffects)
        {
            var elements = new List<UIElement>();
            var isCircle = magnifier.Shape == MagnifierShape.Circle;

            if (!suppressExpensiveEffects && magnifier.HasShadow)
            {
                elements.AddRange(CreateMagnifierShadowElements(magnifier.SourceRegion, magnifier, isCircle));
                elements.AddRange(CreateMagnifierShadowElements(magnifier.LoupeRegion, magnifier, isCircle));
                elements.AddRange(CreateMagnifierConnectorShadowElements(magnifier, isCircle));
            }

            var connector = CreateMagnifierConnectorPath(magnifier, isCircle, offsetX: 0, offsetY: 0, colorOverride: null);
            if (connector is not null)
            {
                elements.Add(connector);
            }

            elements.Add(CreateMagnifierStroke(magnifier.SourceRegion, magnifier, isCircle));

            var loupeImage = CreateMagnifierLoupeImage(magnifier);
            if (loupeImage is not null)
            {
                elements.Add(loupeImage);
            }

            elements.Add(CreateMagnifierStroke(magnifier.LoupeRegion, magnifier, isCircle));
            return elements;
        }

        private static IEnumerable<UIElement> CreateMagnifierConnectorShadowElements(MagnifierLayer magnifier, bool isCircle)
        {
            var shadowColor = EditorXamlResourceCache.ParseColor(magnifier.ShadowColorHex);
            var shadowOffset = Math.Max(1, magnifier.ShadowOffset);
            var shadowSteps = new (int delta, double alphaScale)[] { (0, 1.0), (1, 0.58), (2, 0.34) };
            foreach (var step in shadowSteps)
            {
                var offset = shadowOffset + step.delta;
                var path = CreateMagnifierConnectorPath(
                    magnifier,
                    isCircle,
                    offset,
                    offset,
                    ScaleColorAlpha(shadowColor, step.alphaScale));
                if (path is not null)
                {
                    yield return path;
                }
            }
        }

        private static ShapePath? CreateMagnifierConnectorPath(
            MagnifierLayer magnifier,
            bool isCircle,
            double offsetX,
            double offsetY,
            Color? colorOverride)
        {
            if (!TryGetMagnifierConnectorEndpoints(magnifier, isCircle, out var sourcePoint, out var loupePoint))
            {
                return null;
            }

            sourcePoint = new Point(sourcePoint.X + offsetX, sourcePoint.Y + offsetY);
            loupePoint = new Point(loupePoint.X + offsetX, loupePoint.Y + offsetY);

            var dx = loupePoint.X - sourcePoint.X;
            var dy = loupePoint.Y - sourcePoint.Y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length < MagnifierConnectorMinLength)
            {
                return null;
            }

            var normalX = -dy / length;
            var normalY = dx / length;

            var thickness = Math.Clamp(magnifier.Thickness, 1, MaxPrimaryThickness);
            var sourceHalfWidth = 0.75;
            var loupeHalfWidth = Math.Clamp(Math.Max(sourceHalfWidth + 1.25, thickness * 0.55), 2.0, 6.0);
            var segmentCount = Math.Max(6, Math.Min(14, (int)Math.Round(length / 14d)));

            var topPoints = new List<Point>(segmentCount + 1);
            var bottomPoints = new List<Point>(segmentCount + 1);
            for (var i = 0; i <= segmentCount; i++)
            {
                var t = i / (double)segmentCount;
                var easedT = Math.Pow(t, 1.2);
                var halfWidth = sourceHalfWidth + ((loupeHalfWidth - sourceHalfWidth) * easedT);
                var x = sourcePoint.X + (dx * t);
                var y = sourcePoint.Y + (dy * t);
                topPoints.Add(new Point(x + (normalX * halfWidth), y + (normalY * halfWidth)));
                bottomPoints.Add(new Point(x - (normalX * halfWidth), y - (normalY * halfWidth)));
            }

            var geometry = new PathGeometry();
            var figure = new PathFigure
            {
                IsClosed = true,
                StartPoint = topPoints[0]
            };
            for (var i = 1; i < topPoints.Count; i++)
            {
                figure.Segments.Add(new LineSegment { Point = topPoints[i] });
            }

            for (var i = bottomPoints.Count - 1; i >= 0; i--)
            {
                figure.Segments.Add(new LineSegment { Point = bottomPoints[i] });
            }

            geometry.Figures.Add(figure);

            var color = colorOverride ?? EditorXamlResourceCache.ParseColor(magnifier.ColorHex);
            return new ShapePath
            {
                Data = geometry,
                Fill = EditorXamlResourceCache.GetBrush(color)
            };
        }

        private static bool TryGetMagnifierConnectorEndpoints(
            MagnifierLayer magnifier,
            bool isCircle,
            out Point sourcePoint,
            out Point loupePoint)
        {
            sourcePoint = default;
            loupePoint = default;

            var sourceCenter = GetRegionCenter(magnifier.SourceRegion);
            var loupeCenter = GetRegionCenter(magnifier.LoupeRegion);
            var dx = loupeCenter.X - sourceCenter.X;
            var dy = loupeCenter.Y - sourceCenter.Y;
            if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
            {
                return false;
            }

            sourcePoint = GetBoundaryPointToward(magnifier.SourceRegion, isCircle, sourceCenter, loupeCenter);
            loupePoint = GetBoundaryPointToward(magnifier.LoupeRegion, isCircle, loupeCenter, sourceCenter);

            var segmentDx = loupePoint.X - sourcePoint.X;
            var segmentDy = loupePoint.Y - sourcePoint.Y;
            var segmentLength = Math.Sqrt((segmentDx * segmentDx) + (segmentDy * segmentDy));
            if (segmentLength < MagnifierConnectorMinLength)
            {
                return false;
            }

            // Ensure the connector still points source → loupe (not inverted by overlapping geometry).
            var centerDot = (segmentDx * dx) + (segmentDy * dy);
            return centerDot > 0;
        }

        private static Point GetRegionCenter(EditorRect region)
        {
            return new Point(region.X + (region.Width / 2.0), region.Y + (region.Height / 2.0));
        }

        private static Point GetBoundaryPointToward(
            EditorRect region,
            bool isCircle,
            Point fromCenter,
            Point toward)
        {
            var dx = toward.X - fromCenter.X;
            var dy = toward.Y - fromCenter.Y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length < 0.001)
            {
                return fromCenter;
            }

            var dirX = dx / length;
            var dirY = dy / length;
            return isCircle
                ? IntersectRayWithEllipse(fromCenter, dirX, dirY, region)
                : IntersectRayWithRect(fromCenter, dirX, dirY, region);
        }

        private static Point IntersectRayWithEllipse(Point center, double dirX, double dirY, EditorRect region)
        {
            var radiusX = Math.Max(0.5, region.Width / 2.0);
            var radiusY = Math.Max(0.5, region.Height / 2.0);
            var a = ((dirX * dirX) / (radiusX * radiusX)) + ((dirY * dirY) / (radiusY * radiusY));
            if (a < 0.0000001)
            {
                return center;
            }

            var t = Math.Sqrt(1.0 / a);
            return new Point(center.X + (dirX * t), center.Y + (dirY * t));
        }

        private static Point IntersectRayWithRect(Point center, double dirX, double dirY, EditorRect region)
        {
            var left = region.X;
            var top = region.Y;
            var right = region.X + region.Width;
            var bottom = region.Y + region.Height;
            var bestT = double.PositiveInfinity;

            if (Math.Abs(dirX) > 0.0000001)
            {
                var tLeft = (left - center.X) / dirX;
                if (tLeft > 0)
                {
                    var y = center.Y + (dirY * tLeft);
                    if (y >= top && y <= bottom)
                    {
                        bestT = Math.Min(bestT, tLeft);
                    }
                }

                var tRight = (right - center.X) / dirX;
                if (tRight > 0)
                {
                    var y = center.Y + (dirY * tRight);
                    if (y >= top && y <= bottom)
                    {
                        bestT = Math.Min(bestT, tRight);
                    }
                }
            }

            if (Math.Abs(dirY) > 0.0000001)
            {
                var tTop = (top - center.Y) / dirY;
                if (tTop > 0)
                {
                    var x = center.X + (dirX * tTop);
                    if (x >= left && x <= right)
                    {
                        bestT = Math.Min(bestT, tTop);
                    }
                }

                var tBottom = (bottom - center.Y) / dirY;
                if (tBottom > 0)
                {
                    var x = center.X + (dirX * tBottom);
                    if (x >= left && x <= right)
                    {
                        bestT = Math.Min(bestT, tBottom);
                    }
                }
            }

            if (double.IsPositiveInfinity(bestT))
            {
                return center;
            }

            return new Point(center.X + (dirX * bestT), center.Y + (dirY * bestT));
        }

        private static UIElement? CreateMagnifierLoupeImage(MagnifierLayer magnifier)
        {
            if (magnifier.LoupePixels is null ||
                magnifier.LoupePixelWidth <= 0 ||
                magnifier.LoupePixelHeight <= 0)
            {
                return null;
            }

            var bitmap = new WriteableBitmap(magnifier.LoupePixelWidth, magnifier.LoupePixelHeight);
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Write(magnifier.LoupePixels, 0, magnifier.LoupePixels.Length);
            }

            bitmap.Invalidate();

            var image = new Image
            {
                Width = magnifier.LoupeRegion.Width,
                Height = magnifier.LoupeRegion.Height,
                Stretch = Stretch.None,
                Source = bitmap
            };

            Canvas.SetLeft(image, magnifier.LoupeRegion.X);
            Canvas.SetTop(image, magnifier.LoupeRegion.Y);
            return image;
        }

        private static UIElement CreateMagnifierStroke(EditorRect region, MagnifierLayer magnifier, bool isCircle)
        {
            if (isCircle)
            {
                var ellipse = new Ellipse
                {
                    Width = region.Width,
                    Height = region.Height,
                    Stroke = EditorXamlResourceCache.GetBrush(magnifier.ColorHex),
                    StrokeThickness = Math.Clamp(magnifier.Thickness, 1, MaxPrimaryThickness),
                    Fill = null
                };
                Canvas.SetLeft(ellipse, region.X);
                Canvas.SetTop(ellipse, region.Y);
                return ellipse;
            }

            var rect = new Rectangle
            {
                Width = region.Width,
                Height = region.Height,
                Stroke = EditorXamlResourceCache.GetBrush(magnifier.ColorHex),
                StrokeThickness = Math.Clamp(magnifier.Thickness, 1, MaxPrimaryThickness),
                Fill = null
            };
            Canvas.SetLeft(rect, region.X);
            Canvas.SetTop(rect, region.Y);
            return rect;
        }

        private static IEnumerable<UIElement> CreateMagnifierShadowElements(
            EditorRect region,
            MagnifierLayer magnifier,
            bool isCircle)
        {
            var shadowColor = EditorXamlResourceCache.ParseColor(magnifier.ShadowColorHex);
            var shadowOffset = Math.Max(1, magnifier.ShadowOffset);
            var shadowSteps = new (int delta, double alphaScale, double thicknessAdd)[] { (0, 1.0, 0.0), (1, 0.58, 1.0), (2, 0.34, 2.0) };
            foreach (var step in shadowSteps)
            {
                var thickness = Math.Max(1, magnifier.Thickness + step.thicknessAdd);
                var brush = EditorXamlResourceCache.GetBrush(ScaleColorAlpha(shadowColor, step.alphaScale));
                var offset = shadowOffset + step.delta;
                if (isCircle)
                {
                    var ellipse = new Ellipse
                    {
                        Width = region.Width,
                        Height = region.Height,
                        Stroke = brush,
                        StrokeThickness = thickness,
                        Fill = null
                    };
                    Canvas.SetLeft(ellipse, region.X + offset);
                    Canvas.SetTop(ellipse, region.Y + offset);
                    yield return ellipse;
                }
                else
                {
                    var rect = new Rectangle
                    {
                        Width = region.Width,
                        Height = region.Height,
                        Stroke = brush,
                        StrokeThickness = thickness,
                        Fill = null
                    };
                    Canvas.SetLeft(rect, region.X + offset);
                    Canvas.SetTop(rect, region.Y + offset);
                    yield return rect;
                }
            }
        }

        private static IEnumerable<UIElement> CreateFeatheredBorderShadowElements(BorderLayer borderLayer, double cornerRadius)
        {
            var shadowColor = EditorXamlResourceCache.ParseColor(borderLayer.ShadowColorHex);
            var shadowOffset = Math.Max(1, borderLayer.ShadowOffset);
            var shadowSteps = new (int delta, double alphaScale, double thicknessAdd)[] { (0, 1.0, 0.0), (1, 0.58, 1.0), (2, 0.34, 2.0) };
            foreach (var step in shadowSteps)
            {
                var shadowRect = new Rectangle
                {
                    Width = borderLayer.Region.Width,
                    Height = borderLayer.Region.Height,
                    Stroke = EditorXamlResourceCache.GetBrush(ScaleColorAlpha(shadowColor, step.alphaScale)),
                    StrokeThickness = Math.Max(1, borderLayer.Thickness + step.thicknessAdd),
                    RadiusX = cornerRadius,
                    RadiusY = cornerRadius
                };

                var offset = shadowOffset + step.delta;
                Canvas.SetLeft(shadowRect, borderLayer.Region.X + offset);
                Canvas.SetTop(shadowRect, borderLayer.Region.Y + offset);
                yield return shadowRect;
            }
        }

        private static int GetEffectiveCornerRadius(EditorRect region, int requestedRadius)
        {
            var maxAllowed = Math.Max(0, Math.Min(region.Width, region.Height) / 2);
            return Math.Clamp(requestedRadius, 0, maxAllowed);
        }

        private static void ApplyTextStyleToBlock(TextBlock textBlock, TextLayer textLayer)
        {
            textBlock.FontWeight = textLayer.IsBold ? FontWeights.Bold : FontWeights.Normal;
            textBlock.FontStyle = textLayer.IsItalic ? UiFontStyle.Italic : UiFontStyle.Normal;
            textBlock.LineStackingStrategy = LineStackingStrategy.MaxHeight;
            textBlock.TextLineBounds = TextLineBounds.Full;
        }

        private static Windows.UI.Color ScaleColorAlpha(Windows.UI.Color color, double factor)
        {
            var alpha = (byte)Math.Clamp((int)Math.Round(color.A * factor), 0, 255);
            return ColorHelper.FromArgb(alpha, color.R, color.G, color.B);
        }
    }
}
