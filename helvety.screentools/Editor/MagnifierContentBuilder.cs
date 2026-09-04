using System;

namespace helvety.screentools.Editor
{
    /// <summary>
    /// Builds nearest-neighbor upscaled loupe pixel buffers from the composed base image.
    /// </summary>
    internal static class MagnifierContentBuilder
    {
        internal static void UpdateLoupePixels(
            MagnifierLayer layer,
            byte[] sourcePixels,
            int sourceWidth,
            int sourceHeight)
        {
            var sourceRegion = ClampRegion(layer.SourceRegion, sourceWidth, sourceHeight);
            var loupe = layer.LoupeRegion;
            if (sourceRegion.IsEmpty || loupe.Width <= 0 || loupe.Height <= 0)
            {
                layer.LoupePixels = null;
                layer.LoupePixelWidth = 0;
                layer.LoupePixelHeight = 0;
                layer.ContentGeneration++;
                return;
            }

            var destWidth = loupe.Width;
            var destHeight = loupe.Height;
            var output = new byte[destWidth * destHeight * 4];

            for (var dy = 0; dy < destHeight; dy++)
            {
                var srcY = sourceRegion.Y + (dy * sourceRegion.Height / destHeight);
                srcY = Math.Clamp(srcY, sourceRegion.Y, sourceRegion.Y + sourceRegion.Height - 1);
                for (var dx = 0; dx < destWidth; dx++)
                {
                    var srcX = sourceRegion.X + (dx * sourceRegion.Width / destWidth);
                    srcX = Math.Clamp(srcX, sourceRegion.X, sourceRegion.X + sourceRegion.Width - 1);
                    var srcOffset = ((srcY * sourceWidth) + srcX) * 4;
                    var destOffset = ((dy * destWidth) + dx) * 4;

                    if (layer.Shape == MagnifierShape.Circle && !IsInsideEllipse(dx, dy, destWidth, destHeight))
                    {
                        output[destOffset] = 0;
                        output[destOffset + 1] = 0;
                        output[destOffset + 2] = 0;
                        output[destOffset + 3] = 0;
                        continue;
                    }

                    output[destOffset] = sourcePixels[srcOffset];
                    output[destOffset + 1] = sourcePixels[srcOffset + 1];
                    output[destOffset + 2] = sourcePixels[srcOffset + 2];
                    output[destOffset + 3] = sourcePixels[srcOffset + 3];
                }
            }

            layer.LoupePixels = output;
            layer.LoupePixelWidth = destWidth;
            layer.LoupePixelHeight = destHeight;
            layer.ContentGeneration++;
        }

        private static bool IsInsideEllipse(int x, int y, int width, int height)
        {
            var radiusX = width / 2.0;
            var radiusY = height / 2.0;
            if (radiusX <= 0 || radiusY <= 0)
            {
                return false;
            }

            var nx = (x + 0.5 - radiusX) / radiusX;
            var ny = (y + 0.5 - radiusY) / radiusY;
            return (nx * nx) + (ny * ny) <= 1.0;
        }

        private static EditorRect ClampRegion(EditorRect region, int maxWidth, int maxHeight)
        {
            if (maxWidth <= 0 || maxHeight <= 0 || region.IsEmpty)
            {
                return default;
            }

            var x = Math.Clamp(region.X, 0, maxWidth - 1);
            var y = Math.Clamp(region.Y, 0, maxHeight - 1);
            var right = Math.Clamp(region.X + region.Width, 0, maxWidth);
            var bottom = Math.Clamp(region.Y + region.Height, 0, maxHeight);
            var width = Math.Max(0, right - x);
            var height = Math.Max(0, bottom - y);
            return new EditorRect(x, y, width, height);
        }
    }
}
