using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using Windows.UI;
using UiFontStyle = Windows.UI.Text.FontStyle;

namespace helvety.screentools.Editor
{
    /// <summary>
    /// Draws text, border, and arrow layers to a XAML canvas.
    /// </summary>
    internal static class EditorVectorOverlayRenderer
    {
        private const int MaxPrimaryThickness = 24;

        internal static void DrawVisibleVectorLayers(
            IEnumerable<EditorLayer> layers,
            Canvas targetCanvas,
            bool suppressExpensiveEffects)
        {
            targetCanvas.Children.Clear();
            foreach (var layer in layers.Where(item => item.IsVisible).Reverse())
            {
                DrawVectorLayerIfSupported(layer, suppressExpensiveEffects, targetCanvas);
            }
        }

        /// <summary>
        /// Draws layers in list order (back-to-front) when caller already provides bottom-to-top sequence.
        /// </summary>
        internal static void DrawVectorLayersBottomToTop(
            IEnumerable<EditorLayer> layersBottomToTop,
            Canvas targetCanvas,
            bool suppressExpensiveEffects)
        {
            targetCanvas.Children.Clear();
            foreach (var layer in layersBottomToTop)
            {
                DrawVectorLayerIfSupported(layer, suppressExpensiveEffects, targetCanvas);
            }
        }

        private static void DrawVectorLayerIfSupported(
            EditorLayer layer,
            bool suppressExpensiveEffects,
            Canvas targetCanvas)
        {
            switch (layer)
            {
                case TextLayer textLayer:
                    DrawTextLayer(textLayer, suppressExpensiveEffects, targetCanvas);
                    break;
                case BorderLayer borderLayer:
                    DrawBorderLayer(borderLayer, suppressExpensiveEffects, targetCanvas);
                    break;
                case ArrowLayer arrowLayer:
                    ArrowRendering.DrawArrowLayer(arrowLayer, suppressExpensiveEffects, targetCanvas);
                    break;
            }
        }

        private static void DrawTextLayer(TextLayer textLayer, bool suppressExpensiveEffects, Canvas targetCanvas)
        {
            if (!suppressExpensiveEffects && textLayer.HasShadow)
            {
                DrawFeatheredTextShadow(textLayer, targetCanvas);
            }

            if (!suppressExpensiveEffects && textLayer.HasBorder)
            {
                var thickness = Math.Clamp(textLayer.BorderThickness, 1, MaxPrimaryThickness);
                for (var offsetY = -thickness; offsetY <= thickness; offsetY++)
                {
                    for (var offsetX = -thickness; offsetX <= thickness; offsetX++)
                    {
                        if ((offsetX * offsetX) + (offsetY * offsetY) > (thickness * thickness))
                        {
                            continue;
                        }

                        if (offsetX == 0 && offsetY == 0)
                        {
                            continue;
                        }

                        var outline = new TextBlock
                        {
                            Text = textLayer.Text,
                            FontSize = textLayer.FontSize,
                            FontFamily = new FontFamily(GetFontName(textLayer.FontFamily)),
                            Foreground = new SolidColorBrush(ParseColor(textLayer.BorderColorHex)),
                            Width = Math.Max(1, textLayer.WrapWidth),
                            TextWrapping = TextWrapping.Wrap
                        };
                        ApplyTextStyleToBlock(outline, textLayer);
                        Canvas.SetLeft(outline, textLayer.X + offsetX);
                        Canvas.SetTop(outline, textLayer.Y + offsetY);
                        targetCanvas.Children.Add(outline);
                    }
                }
            }

            var mainText = new TextBlock
            {
                Text = textLayer.Text,
                FontSize = textLayer.FontSize,
                FontFamily = new FontFamily(GetFontName(textLayer.FontFamily)),
                Foreground = new SolidColorBrush(ParseColor(textLayer.ColorHex)),
                Width = Math.Max(1, textLayer.WrapWidth),
                TextWrapping = TextWrapping.Wrap
            };
            ApplyTextStyleToBlock(mainText, textLayer);
            Canvas.SetLeft(mainText, textLayer.X);
            Canvas.SetTop(mainText, textLayer.Y);
            targetCanvas.Children.Add(mainText);
        }

        private static void DrawBorderLayer(BorderLayer borderLayer, bool suppressExpensiveEffects, Canvas targetCanvas)
        {
            var cornerRadius = GetEffectiveCornerRadius(borderLayer.Region, borderLayer.CornerRadius);
            if (!suppressExpensiveEffects && borderLayer.HasShadow)
            {
                DrawFeatheredBorderShadow(borderLayer, cornerRadius, targetCanvas);
            }

            var borderRect = new Rectangle
            {
                Width = borderLayer.Region.Width,
                Height = borderLayer.Region.Height,
                Stroke = new SolidColorBrush(ParseColor(borderLayer.ColorHex)),
                StrokeThickness = borderLayer.Thickness,
                RadiusX = cornerRadius,
                RadiusY = cornerRadius
            };
            Canvas.SetLeft(borderRect, borderLayer.Region.X);
            Canvas.SetTop(borderRect, borderLayer.Region.Y);
            targetCanvas.Children.Add(borderRect);
        }

        private static void DrawFeatheredTextShadow(TextLayer textLayer, Canvas targetCanvas)
        {
            var shadowColor = ParseColor(textLayer.ShadowColorHex);
            var shadowOffset = Math.Max(1, textLayer.ShadowOffset);
            var shadowSteps = new (int delta, double alphaScale)[] { (0, 1.0), (1, 0.58), (2, 0.32) };
            foreach (var step in shadowSteps)
            {
                var shadow = new TextBlock
                {
                    Text = textLayer.Text,
                    FontSize = textLayer.FontSize,
                    FontFamily = new FontFamily(GetFontName(textLayer.FontFamily)),
                    Foreground = new SolidColorBrush(ScaleColorAlpha(shadowColor, step.alphaScale)),
                    Width = Math.Max(1, textLayer.WrapWidth),
                    TextWrapping = TextWrapping.Wrap
                };
                ApplyTextStyleToBlock(shadow, textLayer);

                var offset = shadowOffset + step.delta;
                Canvas.SetLeft(shadow, textLayer.X + offset);
                Canvas.SetTop(shadow, textLayer.Y + offset);
                targetCanvas.Children.Add(shadow);
            }
        }

        private static void DrawFeatheredBorderShadow(BorderLayer borderLayer, double cornerRadius, Canvas targetCanvas)
        {
            var shadowColor = ParseColor(borderLayer.ShadowColorHex);
            var shadowOffset = Math.Max(1, borderLayer.ShadowOffset);
            var shadowSteps = new (int delta, double alphaScale, double thicknessAdd)[] { (0, 1.0, 0.0), (1, 0.58, 1.0), (2, 0.34, 2.0) };
            foreach (var step in shadowSteps)
            {
                var shadowRect = new Rectangle
                {
                    Width = borderLayer.Region.Width,
                    Height = borderLayer.Region.Height,
                    Stroke = new SolidColorBrush(ScaleColorAlpha(shadowColor, step.alphaScale)),
                    StrokeThickness = Math.Max(1, borderLayer.Thickness + step.thicknessAdd),
                    RadiusX = cornerRadius,
                    RadiusY = cornerRadius
                };

                var offset = shadowOffset + step.delta;
                Canvas.SetLeft(shadowRect, borderLayer.Region.X + offset);
                Canvas.SetTop(shadowRect, borderLayer.Region.Y + offset);
                targetCanvas.Children.Add(shadowRect);
            }
        }

        private static int GetEffectiveCornerRadius(EditorRect region, int requestedRadius)
        {
            var maxAllowed = Math.Max(0, Math.Min(region.Width, region.Height) / 2);
            return Math.Clamp(requestedRadius, 0, maxAllowed);
        }

        private static string GetFontName(string? fontFamily)
        {
            return string.IsNullOrWhiteSpace(fontFamily)
                ? "Segoe UI"
                : fontFamily;
        }

        private static void ApplyTextStyleToBlock(TextBlock textBlock, TextLayer textLayer)
        {
            textBlock.FontWeight = textLayer.IsBold ? FontWeights.Bold : FontWeights.Normal;
            textBlock.FontStyle = textLayer.IsItalic ? UiFontStyle.Italic : UiFontStyle.Normal;
            textBlock.LineStackingStrategy = LineStackingStrategy.MaxHeight;
            textBlock.TextLineBounds = TextLineBounds.Full;
        }

        private static Color ScaleColorAlpha(Color color, double factor)
        {
            var alpha = (byte)Math.Clamp((int)Math.Round(color.A * factor), 0, 255);
            return ColorHelper.FromArgb(alpha, color.R, color.G, color.B);
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
