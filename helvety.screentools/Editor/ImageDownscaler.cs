using System;

namespace helvety.screentools.Editor
{
    /// <summary>Shrinks premultiplied BGRA8 images. Never enlarges.</summary>
    internal static class ImageDownscaler
    {
        internal const int MinimumScalePercent = 10;
        internal const int MaximumScalePercent = 100;

        internal static (int Width, int Height) GetOutputSize(int sourceWidth, int sourceHeight, int scalePercent)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                return (0, 0);
            }

            var percent = Math.Clamp(scalePercent, MinimumScalePercent, MaximumScalePercent);
            if (percent >= MaximumScalePercent)
            {
                return (sourceWidth, sourceHeight);
            }

            var width = Math.Clamp((int)Math.Round(sourceWidth * (percent / 100d)), 1, sourceWidth);
            var height = Math.Clamp((int)Math.Round(sourceHeight * (percent / 100d)), 1, sourceHeight);
            return (width, height);
        }

        /// <summary>
        /// Area-average downsample. Returns <paramref name="source"/> when the destination size matches the source.
        /// </summary>
        internal static byte[] Downscale(
            byte[] source,
            int sourceWidth,
            int sourceHeight,
            int destinationWidth,
            int destinationHeight)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0 || destinationWidth <= 0 || destinationHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(destinationWidth), "Width and height must be positive.");
            }

            if (destinationWidth > sourceWidth || destinationHeight > sourceHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(destinationWidth), "Downscale cannot enlarge.");
            }

            var sourceBytes = (long)sourceWidth * sourceHeight * 4;
            if (source.LongLength < sourceBytes)
            {
                throw new ArgumentException("Source buffer is smaller than the stated dimensions.", nameof(source));
            }

            if (destinationWidth == sourceWidth && destinationHeight == sourceHeight)
            {
                return source;
            }

            var destination = new byte[destinationWidth * destinationHeight * 4];
            for (var dy = 0; dy < destinationHeight; dy++)
            {
                var srcY0 = dy * (double)sourceHeight / destinationHeight;
                var srcY1 = (dy + 1) * (double)sourceHeight / destinationHeight;
                var yStart = (int)Math.Floor(srcY0);
                var yEnd = Math.Min(sourceHeight - 1, (int)Math.Ceiling(srcY1) - 1);

                for (var dx = 0; dx < destinationWidth; dx++)
                {
                    var srcX0 = dx * (double)sourceWidth / destinationWidth;
                    var srcX1 = (dx + 1) * (double)sourceWidth / destinationWidth;
                    var xStart = (int)Math.Floor(srcX0);
                    var xEnd = Math.Min(sourceWidth - 1, (int)Math.Ceiling(srcX1) - 1);

                    double sumB = 0;
                    double sumG = 0;
                    double sumR = 0;
                    double sumA = 0;
                    double sumWeight = 0;
                    for (var y = yStart; y <= yEnd; y++)
                    {
                        var yWeight = Math.Min(y + 1d, srcY1) - Math.Max(y, srcY0);
                        if (yWeight <= 0)
                        {
                            continue;
                        }

                        var row = y * sourceWidth;
                        for (var x = xStart; x <= xEnd; x++)
                        {
                            var xWeight = Math.Min(x + 1d, srcX1) - Math.Max(x, srcX0);
                            if (xWeight <= 0)
                            {
                                continue;
                            }

                            var weight = xWeight * yWeight;
                            var sourceIndex = (row + x) * 4;
                            sumB += source[sourceIndex] * weight;
                            sumG += source[sourceIndex + 1] * weight;
                            sumR += source[sourceIndex + 2] * weight;
                            sumA += source[sourceIndex + 3] * weight;
                            sumWeight += weight;
                        }
                    }

                    var destinationIndex = ((dy * destinationWidth) + dx) * 4;
                    if (sumWeight <= 0)
                    {
                        continue;
                    }

                    destination[destinationIndex] = ToByte(sumB / sumWeight);
                    destination[destinationIndex + 1] = ToByte(sumG / sumWeight);
                    destination[destinationIndex + 2] = ToByte(sumR / sumWeight);
                    destination[destinationIndex + 3] = ToByte(sumA / sumWeight);
                }
            }

            return destination;
        }

        private static byte ToByte(double value)
        {
            return (byte)Math.Clamp((int)Math.Round(value), 0, 255);
        }
    }
}
