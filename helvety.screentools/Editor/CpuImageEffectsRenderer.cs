using System;
using System.Collections.Generic;
using System.Linq;

namespace helvety.screentools.Editor
{
    /// <summary>CPU fallback for blur and highlight when GPU rendering is unavailable.</summary>
    internal static class CpuImageEffectsRenderer
    {
        internal static void Apply(
            byte[] sourcePixels,
            int imageWidth,
            int imageHeight,
            IReadOnlyList<EditorLayer> layers,
            bool blurInvertMode,
            int highlightDimPercent,
            bool highlightInvertMode,
            byte[] destinationPixels)
        {
            Buffer.BlockCopy(sourcePixels, 0, destinationPixels, 0, sourcePixels.Length);

            foreach (var blurLayer in layers.OfType<BlurLayer>().Where(layer => layer.IsVisible))
            {
                ApplyBlurRegion(
                    destinationPixels,
                    imageWidth,
                    imageHeight,
                    blurLayer,
                    blurInvertMode);
            }

            var dimFactor = 1f - (Math.Clamp(highlightDimPercent, 0, 80) / 100f);
            if (dimFactor >= 1f)
            {
                return;
            }

            foreach (var highlightLayer in layers.OfType<HighlightLayer>().Where(layer => layer.IsVisible))
            {
                ApplyHighlightDim(
                    destinationPixels,
                    imageWidth,
                    imageHeight,
                    highlightLayer,
                    dimFactor,
                    highlightInvertMode);
            }
        }

        private static void ApplyBlurRegion(
            byte[] pixels,
            int imageWidth,
            int imageHeight,
            BlurLayer blurLayer,
            bool invertMode)
        {
            var region = ClampRegion(blurLayer.Region, imageWidth, imageHeight);
            if (region.IsEmpty)
            {
                return;
            }

            var radius = Math.Clamp(blurLayer.Radius, 1, 25);
            var pad = radius * 2;
            var x0 = Math.Max(0, region.X - pad);
            var y0 = Math.Max(0, region.Y - pad);
            var x1 = Math.Min(imageWidth, region.X + region.Width + pad);
            var y1 = Math.Min(imageHeight, region.Y + region.Height + pad);
            var roiWidth = x1 - x0;
            var roiHeight = y1 - y0;
            if (roiWidth <= 0 || roiHeight <= 0)
            {
                return;
            }

            var roiStride = roiWidth * 4;
            var roi = new byte[roiStride * roiHeight];
            CopyRegion(pixels, imageWidth, imageHeight, x0, y0, roi, roiWidth, roiHeight);

            var passes = Math.Min(3, 1 + (radius / 8));
            for (var pass = 0; pass < passes; pass++)
            {
                BoxBlurHorizontal(roi, roiWidth, roiHeight, radius);
                BoxBlurVertical(roi, roiWidth, roiHeight, radius);
            }

            BlendRegion(pixels, imageWidth, imageHeight, roi, roiWidth, roiHeight, x0, y0, region, invertMode);
        }

        private static void ApplyHighlightDim(
            byte[] pixels,
            int imageWidth,
            int imageHeight,
            HighlightLayer highlightLayer,
            float dimFactor,
            bool invertMode)
        {
            var region = ClampRegion(highlightLayer.Region, imageWidth, imageHeight);
            if (region.IsEmpty)
            {
                return;
            }

            var dimInside = !invertMode;
            for (var y = 0; y < imageHeight; y++)
            {
                for (var x = 0; x < imageWidth; x++)
                {
                    var inside = x >= region.X && x < region.X + region.Width &&
                                 y >= region.Y && y < region.Y + region.Height;
                    if (inside != dimInside)
                    {
                        continue;
                    }

                    var index = ((y * imageWidth) + x) * 4;
                    pixels[index] = (byte)(pixels[index] * dimFactor);
                    pixels[index + 1] = (byte)(pixels[index + 1] * dimFactor);
                    pixels[index + 2] = (byte)(pixels[index + 2] * dimFactor);
                }
            }
        }

        private static void BlendRegion(
            byte[] dest,
            int imageWidth,
            int imageHeight,
            byte[] blurredRoi,
            int roiWidth,
            int roiHeight,
            int roiX,
            int roiY,
            EditorRect maskRegion,
            bool invertMode)
        {
            for (var y = 0; y < roiHeight; y++)
            {
                var imageY = roiY + y;
                if (imageY < 0 || imageY >= imageHeight)
                {
                    continue;
                }

                for (var x = 0; x < roiWidth; x++)
                {
                    var imageX = roiX + x;
                    if (imageX < 0 || imageX >= imageWidth)
                    {
                        continue;
                    }

                    var insideMask = imageX >= maskRegion.X && imageX < maskRegion.X + maskRegion.Width &&
                                     imageY >= maskRegion.Y && imageY < maskRegion.Y + maskRegion.Height;
                    var applyBlur = invertMode ? !insideMask : insideMask;
                    if (!applyBlur)
                    {
                        continue;
                    }

                    var destIndex = ((imageY * imageWidth) + imageX) * 4;
                    var srcIndex = ((y * roiWidth) + x) * 4;
                    dest[destIndex] = blurredRoi[srcIndex];
                    dest[destIndex + 1] = blurredRoi[srcIndex + 1];
                    dest[destIndex + 2] = blurredRoi[srcIndex + 2];
                    dest[destIndex + 3] = blurredRoi[srcIndex + 3];
                }
            }
        }

        private static void CopyRegion(
            byte[] source,
            int imageWidth,
            int imageHeight,
            int x0,
            int y0,
            byte[] dest,
            int destWidth,
            int destHeight)
        {
            for (var y = 0; y < destHeight; y++)
            {
                var imageY = y0 + y;
                if (imageY < 0 || imageY >= imageHeight)
                {
                    continue;
                }

                var srcRow = imageY * imageWidth * 4;
                var destRow = y * destWidth * 4;
                Buffer.BlockCopy(source, srcRow + (x0 * 4), dest, destRow, destWidth * 4);
            }
        }

        private static void BoxBlurHorizontal(byte[] pixels, int width, int height, int radius)
        {
            var window = (radius * 2) + 1;
            var temp = new byte[pixels.Length];
            Buffer.BlockCopy(pixels, 0, temp, 0, pixels.Length);

            for (var y = 0; y < height; y++)
            {
                var row = y * width * 4;
                for (var channel = 0; channel < 3; channel++)
                {
                    var sum = 0;
                    for (var x = -radius; x <= radius; x++)
                    {
                        var clampedX = Math.Clamp(x, 0, width - 1);
                        sum += temp[row + (clampedX * 4) + channel];
                    }

                    for (var x = 0; x < width; x++)
                    {
                        pixels[row + (x * 4) + channel] = (byte)(sum / window);
                        var removeX = Math.Clamp(x - radius, 0, width - 1);
                        var addX = Math.Clamp(x + radius + 1, 0, width - 1);
                        sum -= temp[row + (removeX * 4) + channel];
                        sum += temp[row + (addX * 4) + channel];
                    }
                }
            }
        }

        private static void BoxBlurVertical(byte[] pixels, int width, int height, int radius)
        {
            var window = (radius * 2) + 1;
            var temp = new byte[pixels.Length];
            Buffer.BlockCopy(pixels, 0, temp, 0, pixels.Length);

            for (var x = 0; x < width; x++)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    var sum = 0;
                    for (var y = -radius; y <= radius; y++)
                    {
                        var clampedY = Math.Clamp(y, 0, height - 1);
                        sum += temp[((clampedY * width) + x) * 4 + channel];
                    }

                    for (var y = 0; y < height; y++)
                    {
                        pixels[((y * width) + x) * 4 + channel] = (byte)(sum / window);
                        var removeY = Math.Clamp(y - radius, 0, height - 1);
                        var addY = Math.Clamp(y + radius + 1, 0, height - 1);
                        sum -= temp[((removeY * width) + x) * 4 + channel];
                        sum += temp[((addY * width) + x) * 4 + channel];
                    }
                }
            }
        }

        private static EditorRect ClampRegion(EditorRect region, int imageWidth, int imageHeight)
        {
            var x = Math.Clamp(region.X, 0, Math.Max(0, imageWidth));
            var y = Math.Clamp(region.Y, 0, Math.Max(0, imageHeight));
            var maxWidth = Math.Max(0, imageWidth - x);
            var maxHeight = Math.Max(0, imageHeight - y);
            var width = Math.Clamp(region.Width, 0, maxWidth);
            var height = Math.Clamp(region.Height, 0, maxHeight);
            return new EditorRect(x, y, width, height);
        }
    }
}
