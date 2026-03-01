using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace helvety.screenshots.Capture
{
    internal sealed class ImageSaveService
    {
        public async Task<SavedSelectionResult> SaveSelectionAsync(FreezeFrame freezeFrame, RectInt32 selection, string outputFolderPath)
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

            var outputPath = BuildOutputPath(outputFolderPath);
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                (uint)clampedSelection.Width,
                (uint)clampedSelection.Height,
                96,
                96,
                cropBuffer);

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
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var baseName = $"Helvety_{timestamp}";
            var candidatePath = Path.Combine(outputFolderPath, $"{baseName}.png");
            var duplicateCounter = 1;
            while (File.Exists(candidatePath))
            {
                candidatePath = Path.Combine(outputFolderPath, $"{baseName}_{duplicateCounter}.png");
                duplicateCounter++;
            }

            return candidatePath;
        }
    }

    internal sealed record SavedSelectionResult(string OutputPath, byte[] PngBytes);
}
