using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace helvety.screentools.Capture
{
    internal sealed class ImageSaveService
    {
        public async Task<SavedSelectionResult> SaveSelectionAsync(
            FreezeFrame freezeFrame,
            RectInt32 selection,
            string outputFolderPath,
            ScreenshotQualityMode qualityMode)
        {
            var clampedSelection = ClampToBounds(selection, freezeFrame.VirtualBounds);
            if (clampedSelection.Width <= 0 || clampedSelection.Height <= 0)
            {
                throw new InvalidOperationException("Selection bounds are outside the captured frame.");
            }

            var cropBuffer = new byte[clampedSelection.Width * clampedSelection.Height * 4];
            var sourceStartX = clampedSelection.X - freezeFrame.VirtualBounds.X;
            var sourceStartY = clampedSelection.Y - freezeFrame.VirtualBounds.Y;

            for (var row = 0; row < clampedSelection.Height; row++)
            {
                var sourceOffset = ((sourceStartY + row) * freezeFrame.Stride) + (sourceStartX * 4);
                var destinationOffset = row * clampedSelection.Width * 4;
                System.Buffer.BlockCopy(
                    freezeFrame.PixelData,
                    sourceOffset,
                    cropBuffer,
                    destinationOffset,
                    clampedSelection.Width * 4);
            }

            var processingWatch = Stopwatch.StartNew();
            var processedImage = ApplyQualityMode(cropBuffer, clampedSelection.Width, clampedSelection.Height, qualityMode);
            processingWatch.Stop();
            ScreenshotQualityBenchmarks.RecordSample(qualityMode, processedImage.Width, processedImage.Height, processingWatch.Elapsed, processedImage.Pixels);
            var outputPath = BuildOutputPath(outputFolderPath);
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                (uint)processedImage.Width,
                (uint)processedImage.Height,
                96,
                96,
                processedImage.Pixels);

            await encoder.FlushAsync();
            stream.Seek(0);

            byte[] pngBytes;
            using (var memory = new MemoryStream())
            {
                using var input = stream.AsStreamForRead();
                await input.CopyToAsync(memory);
                pngBytes = memory.ToArray();
            }

            await File.WriteAllBytesAsync(outputPath, pngBytes);
            return new SavedSelectionResult(outputPath, pngBytes);
        }

        private static ProcessedImage ApplyQualityMode(byte[] sourcePixels, int width, int height, ScreenshotQualityMode qualityMode)
        {
            if (sourcePixels.Length == 0 || width <= 0 || height <= 0)
            {
                return new ProcessedImage(sourcePixels, width, height);
            }

            return qualityMode switch
            {
                ScreenshotQualityMode.Optimized => ApplyOptimizedMode(sourcePixels, width, height),
                ScreenshotQualityMode.Heavy => ApplyHeavyMode(sourcePixels, width, height),
                _ => new ProcessedImage(sourcePixels, width, height)
            };
        }

        private static ProcessedImage ApplyOptimizedMode(byte[] sourcePixels, int width, int height)
        {
            var upscaledPixels = UpscaleBilinear(sourcePixels, width, height, 2, out var upscaledWidth, out var upscaledHeight);
            var sharpenedPixels = ApplyUnsharpMask(upscaledPixels, upscaledWidth, upscaledHeight, amount: 0.45f, radius: 1);
            ApplyGlobalContrast(sharpenedPixels, factor: 1.08f);
            return new ProcessedImage(sharpenedPixels, upscaledWidth, upscaledHeight);
        }

        private static ProcessedImage ApplyHeavyMode(byte[] sourcePixels, int width, int height)
        {
            var upscaledPixels = UpscaleBilinear(sourcePixels, width, height, 3, out var upscaledWidth, out var upscaledHeight);
            var denoisedPixels = BoxBlur(upscaledPixels, upscaledWidth, upscaledHeight, radius: 1);
            var sharpenedPixels = ApplyUnsharpMask(denoisedPixels, upscaledWidth, upscaledHeight, amount: 0.75f, radius: 2);
            ApplyGlobalContrast(sharpenedPixels, factor: 1.14f);
            return new ProcessedImage(sharpenedPixels, upscaledWidth, upscaledHeight);
        }

        private static byte[] UpscaleBilinear(byte[] source, int sourceWidth, int sourceHeight, int scaleFactor, out int targetWidth, out int targetHeight)
        {
            targetWidth = Math.Max(1, sourceWidth * scaleFactor);
            targetHeight = Math.Max(1, sourceHeight * scaleFactor);
            var destination = new byte[targetWidth * targetHeight * 4];
            var maxSourceX = Math.Max(0, sourceWidth - 1);
            var maxSourceY = Math.Max(0, sourceHeight - 1);

            for (var y = 0; y < targetHeight; y++)
            {
                var sourceY = (y + 0.5f) / scaleFactor - 0.5f;
                var y0 = (int)Math.Floor(sourceY);
                var y1 = y0 + 1;
                var yLerp = sourceY - y0;
                y0 = Math.Clamp(y0, 0, maxSourceY);
                y1 = Math.Clamp(y1, 0, maxSourceY);

                for (var x = 0; x < targetWidth; x++)
                {
                    var sourceX = (x + 0.5f) / scaleFactor - 0.5f;
                    var x0 = (int)Math.Floor(sourceX);
                    var x1 = x0 + 1;
                    var xLerp = sourceX - x0;
                    x0 = Math.Clamp(x0, 0, maxSourceX);
                    x1 = Math.Clamp(x1, 0, maxSourceX);

                    var topLeft = (y0 * sourceWidth + x0) * 4;
                    var topRight = (y0 * sourceWidth + x1) * 4;
                    var bottomLeft = (y1 * sourceWidth + x0) * 4;
                    var bottomRight = (y1 * sourceWidth + x1) * 4;
                    var destinationIndex = (y * targetWidth + x) * 4;

                    for (var channel = 0; channel < 3; channel++)
                    {
                        var top = Lerp(source[topLeft + channel], source[topRight + channel], xLerp);
                        var bottom = Lerp(source[bottomLeft + channel], source[bottomRight + channel], xLerp);
                        destination[destinationIndex + channel] = ClampToByte(Lerp(top, bottom, yLerp));
                    }

                    destination[destinationIndex + 3] = 255;
                }
            }

            return destination;
        }

        private static byte[] ApplyUnsharpMask(byte[] source, int width, int height, float amount, int radius)
        {
            var blurred = BoxBlur(source, width, height, radius);
            var output = new byte[source.Length];

            for (var index = 0; index < source.Length; index += 4)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    var original = source[index + channel];
                    var blur = blurred[index + channel];
                    var enhanced = original + ((original - blur) * amount);
                    output[index + channel] = ClampToByte(enhanced);
                }

                output[index + 3] = 255;
            }

            return output;
        }

        private static byte[] BoxBlur(byte[] source, int width, int height, int radius)
        {
            if (radius <= 0)
            {
                var clone = new byte[source.Length];
                System.Buffer.BlockCopy(source, 0, clone, 0, source.Length);
                return clone;
            }

            var destination = new byte[source.Length];
            var kernelSize = (radius * 2) + 1;
            var kernelArea = kernelSize * kernelSize;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var b = 0;
                    var g = 0;
                    var r = 0;

                    for (var ky = -radius; ky <= radius; ky++)
                    {
                        var sampleY = Math.Clamp(y + ky, 0, height - 1);
                        for (var kx = -radius; kx <= radius; kx++)
                        {
                            var sampleX = Math.Clamp(x + kx, 0, width - 1);
                            var sampleIndex = (sampleY * width + sampleX) * 4;
                            b += source[sampleIndex];
                            g += source[sampleIndex + 1];
                            r += source[sampleIndex + 2];
                        }
                    }

                    var destinationIndex = (y * width + x) * 4;
                    destination[destinationIndex] = (byte)(b / kernelArea);
                    destination[destinationIndex + 1] = (byte)(g / kernelArea);
                    destination[destinationIndex + 2] = (byte)(r / kernelArea);
                    destination[destinationIndex + 3] = 255;
                }
            }

            return destination;
        }

        private static void ApplyGlobalContrast(byte[] pixels, float factor)
        {
            if (pixels.Length == 0 || factor <= 0f)
            {
                return;
            }

            for (var index = 0; index < pixels.Length; index += 4)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    var normalized = pixels[index + channel] / 255f;
                    var adjusted = ((normalized - 0.5f) * factor) + 0.5f;
                    pixels[index + channel] = ClampToByte(adjusted * 255f);
                }

                pixels[index + 3] = 255;
            }
        }

        private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

        private static byte ClampToByte(float value)
        {
            var rounded = (int)MathF.Round(value);
            return (byte)Math.Clamp(rounded, 0, 255);
        }

        private static RectInt32 ClampToBounds(RectInt32 selection, RectInt32 bounds)
        {
            var x1 = Math.Max(selection.X, bounds.X);
            var y1 = Math.Max(selection.Y, bounds.Y);
            var x2 = Math.Min(selection.X + selection.Width, bounds.X + bounds.Width);
            var y2 = Math.Min(selection.Y + selection.Height, bounds.Y + bounds.Height);
            return new RectInt32(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
        }

        private static string BuildOutputPath(string outputFolderPath)
        {
            var timestamp = DateTime.Now.ToString("yyMMdd_HHmm_ss_fff");
            var baseName = $"{timestamp}-HelvetyCapture";
            var candidatePath = Path.Combine(outputFolderPath, $"{baseName}.png");
            var duplicateCounter = 1;
            while (File.Exists(candidatePath))
            {
                candidatePath = Path.Combine(outputFolderPath, $"{baseName}_{duplicateCounter}.png");
                duplicateCounter++;
            }

            return candidatePath;
        }

        private readonly record struct ProcessedImage(byte[] Pixels, int Width, int Height);
    }

    internal sealed record SavedSelectionResult(string OutputPath, byte[] PngBytes);
}
