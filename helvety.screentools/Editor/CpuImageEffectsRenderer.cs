using System;
using System.Linq;

namespace helvety.screentools.Editor
{
    /// <summary>
    /// CPU fallback for blur and highlight when GPU rendering is unavailable. Follows the same semantics as
    /// <see cref="GpuImageEffectsRenderer"/> (see <see cref="PixelEffects"/>), approximating Gaussian blur with box blurs.
    /// </summary>
    internal static class CpuImageEffectsRenderer
    {
        private const int MaxRadius = 25;

        internal static void Apply(byte[] sourcePixels, int imageWidth, int imageHeight, PixelEffects effects, byte[] destinationPixels)
        {
            Buffer.BlockCopy(sourcePixels, 0, destinationPixels, 0, sourcePixels.Length);

            if (effects.Blurs.Count > 0)
            {
                if (effects.BlurInvertMode)
                {
                    ApplyInvertedBlur(destinationPixels, imageWidth, imageHeight, effects);
                }
                else
                {
                    foreach (var blur in effects.Blurs)
                    {
                        ApplyRegionBlur(destinationPixels, imageWidth, imageHeight, blur);
                    }
                }
            }

            if (effects.HasHighlightDim)
            {
                ApplyHighlightDim(destinationPixels, imageWidth, imageHeight, effects);
            }
        }

        private static void ApplyRegionBlur(byte[] pixels, int imageWidth, int imageHeight, BlurEffect blur)
        {
            var radius = Math.Clamp(blur.Radius, 1, MaxRadius);
            var feather = Math.Clamp(blur.Feather, 0f, PixelEffects.MaxFeather);
            var pad = (radius * 2) + (int)MathF.Ceiling(feather * 2f);
            var x0 = Math.Max(0, blur.Region.X - pad);
            var y0 = Math.Max(0, blur.Region.Y - pad);
            var x1 = Math.Min(imageWidth, blur.Region.X + blur.Region.Width + pad);
            var y1 = Math.Min(imageHeight, blur.Region.Y + blur.Region.Height + pad);
            var roiWidth = x1 - x0;
            var roiHeight = y1 - y0;
            if (roiWidth <= 0 || roiHeight <= 0)
            {
                return;
            }

            var roi = new byte[roiWidth * roiHeight * 4];
            for (var y = 0; y < roiHeight; y++)
            {
                Buffer.BlockCopy(pixels, (((y0 + y) * imageWidth) + x0) * 4, roi, y * roiWidth * 4, roiWidth * 4);
            }

            BlurInPlace(roi, roiWidth, roiHeight, radius);

            for (var y = 0; y < roiHeight; y++)
            {
                for (var x = 0; x < roiWidth; x++)
                {
                    var coverage = PixelEffects.Coverage(x0 + x, y0 + y, blur.Region, blur.CornerRadius, feather);
                    if (coverage > 0f)
                    {
                        Blend(pixels, (((y0 + y) * imageWidth) + x0 + x) * 4, roi, ((y * roiWidth) + x) * 4, coverage);
                    }
                }
            }
        }

        private static void ApplyInvertedBlur(byte[] pixels, int imageWidth, int imageHeight, PixelEffects effects)
        {
            var radius = Math.Clamp(effects.Blurs.Max(blur => blur.Radius), 1, MaxRadius);
            var feather = Math.Clamp(effects.Blurs.Max(blur => blur.Feather), 0f, PixelEffects.MaxFeather);
            var blurred = (byte[])pixels.Clone();
            BlurInPlace(blurred, imageWidth, imageHeight, radius);

            for (var y = 0; y < imageHeight; y++)
            {
                for (var x = 0; x < imageWidth; x++)
                {
                    var keepSharp = 0f;
                    foreach (var blur in effects.Blurs)
                    {
                        keepSharp = MathF.Max(keepSharp, PixelEffects.Coverage(x, y, blur.Region, blur.CornerRadius, feather));
                    }

                    if (keepSharp < 1f)
                    {
                        var index = ((y * imageWidth) + x) * 4;
                        Blend(pixels, index, blurred, index, 1f - keepSharp);
                    }
                }
            }
        }

        private static void ApplyHighlightDim(byte[] pixels, int imageWidth, int imageHeight, PixelEffects effects)
        {
            var dimStrength = 1f - effects.HighlightDimFactor;
            for (var y = 0; y < imageHeight; y++)
            {
                for (var x = 0; x < imageWidth; x++)
                {
                    var inside = 0f;
                    foreach (var highlight in effects.Highlights)
                    {
                        inside = MathF.Max(inside, PixelEffects.Coverage(x, y, highlight.Region, highlight.CornerRadius, 0f));
                    }

                    var dimAmount = effects.HighlightInvertMode ? inside : 1f - inside;
                    if (dimAmount <= 0f)
                    {
                        continue;
                    }

                    // Premultiplied BGRA: scaling color channels darkens without changing coverage.
                    var factor = 1f - (dimStrength * dimAmount);
                    var index = ((y * imageWidth) + x) * 4;
                    pixels[index] = (byte)(pixels[index] * factor);
                    pixels[index + 1] = (byte)(pixels[index + 1] * factor);
                    pixels[index + 2] = (byte)(pixels[index + 2] * factor);
                }
            }
        }

        private static void Blend(byte[] destination, int destinationIndex, byte[] source, int sourceIndex, float amount)
        {
            for (var channel = 0; channel < 4; channel++)
            {
                var from = destination[destinationIndex + channel];
                var to = source[sourceIndex + channel];
                destination[destinationIndex + channel] = (byte)(from + ((to - from) * amount) + 0.5f);
            }
        }

        private static void BlurInPlace(byte[] pixels, int width, int height, int radius)
        {
            // Repeated box blurs approximate a Gaussian; all four channels keep premultiplied pixels valid.
            var passes = Math.Min(3, 1 + (radius / 8));
            var scratch = new byte[pixels.Length];
            for (var pass = 0; pass < passes; pass++)
            {
                BoxBlurHorizontal(pixels, scratch, width, height, radius);
                BoxBlurVertical(pixels, scratch, width, height, radius);
            }
        }

        private static void BoxBlurHorizontal(byte[] pixels, byte[] scratch, int width, int height, int radius)
        {
            var window = (radius * 2) + 1;
            Buffer.BlockCopy(pixels, 0, scratch, 0, pixels.Length);
            for (var y = 0; y < height; y++)
            {
                var row = y * width * 4;
                for (var channel = 0; channel < 4; channel++)
                {
                    var sum = 0;
                    for (var x = -radius; x <= radius; x++)
                    {
                        sum += scratch[row + (Math.Clamp(x, 0, width - 1) * 4) + channel];
                    }

                    for (var x = 0; x < width; x++)
                    {
                        pixels[row + (x * 4) + channel] = (byte)(sum / window);
                        sum -= scratch[row + (Math.Clamp(x - radius, 0, width - 1) * 4) + channel];
                        sum += scratch[row + (Math.Clamp(x + radius + 1, 0, width - 1) * 4) + channel];
                    }
                }
            }
        }

        private static void BoxBlurVertical(byte[] pixels, byte[] scratch, int width, int height, int radius)
        {
            var window = (radius * 2) + 1;
            Buffer.BlockCopy(pixels, 0, scratch, 0, pixels.Length);
            for (var x = 0; x < width; x++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    var sum = 0;
                    for (var y = -radius; y <= radius; y++)
                    {
                        sum += scratch[(((Math.Clamp(y, 0, height - 1) * width) + x) * 4) + channel];
                    }

                    for (var y = 0; y < height; y++)
                    {
                        pixels[(((y * width) + x) * 4) + channel] = (byte)(sum / window);
                        sum -= scratch[(((Math.Clamp(y - radius, 0, height - 1) * width) + x) * 4) + channel];
                        sum += scratch[(((Math.Clamp(y + radius + 1, 0, height - 1) * width) + x) * 4) + channel];
                    }
                }
            }
        }
    }
}
