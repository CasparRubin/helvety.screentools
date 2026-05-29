using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using UiFontStyle = Windows.UI.Text.FontStyle;

namespace helvety.screentools.Editor
{
    /// <summary>
    /// Builds XAML elements for text, border, and arrow layers. Used by <see cref="VectorCanvasHost"/> and overlay preview.
    /// </summary>
    internal static class EditorVectorOverlayRenderer
    {
        private const int MaxPrimaryThickness = 24;

        internal static IReadOnlyList<UIElement> CreateElementsForLayer(EditorLayer layer, bool suppressExpensiveEffects)
        {
            return layer switch
            {
                TextLayer textLayer => CreateTextElements(textLayer, suppressExpensiveEffects),
                BorderLayer borderLayer => CreateBorderElements(borderLayer, suppressExpensiveEffects),
                ArrowLayer arrowLayer => ArrowRendering.CreateArrowElements(arrowLayer, suppressExpensiveEffects),
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
