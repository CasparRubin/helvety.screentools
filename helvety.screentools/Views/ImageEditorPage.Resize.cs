using helvety.screentools.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace helvety.screentools.Views
{
    public sealed partial class ImageEditorPage
    {
        private const int ResizeMeasureDebounceMs = 300;

        private int _resizeScalePercent = ImageDownscaler.MaximumScalePercent;
        private int _resizeMeasureGeneration;
        private CancellationTokenSource? _resizeMeasureDelayCts;
        private bool _isFittingResize;
        private long? _sourceFileBytes;

        private void ResizeScaleSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isInitializingUi)
            {
                return;
            }

            _resizeScalePercent = Clamp(
                (int)Math.Round(e.NewValue),
                ImageDownscaler.MinimumScalePercent,
                ImageDownscaler.MaximumScalePercent);
            ResizeScaleValueText.Text = $"{_resizeScalePercent}%";
            UpdateResizeDimensionReadout();
            UpdateExportButtonLabels();
            if (_isFittingResize)
            {
                return;
            }

            ScheduleResizeSizeMeasure();
        }

        private async void ResizeFitButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isFittingResize || _imageWidth <= 0 || _imageHeight <= 0)
            {
                return;
            }

            var requestedKb = ResizeMaxSizeNumberBox.Value;
            if (double.IsNaN(requestedKb) || requestedKb < 1)
            {
                return;
            }

            var targetBytes = (long)Math.Round(requestedKb * 1024d);
            _isFittingResize = true;
            ResizeFitButton.IsEnabled = false;
            ResizeScaleSlider.IsEnabled = false;
            ResizeMaxSizeNumberBox.IsEnabled = false;
            _resizeMeasureDelayCts?.Cancel();
            var generation = ++_resizeMeasureGeneration;
            ShowResizeMeasuring();

            try
            {
                var frame = await CaptureExportFrameAsync();
                var fit = await FindLargestFittingScaleAsync(frame.Pixels, frame.Width, frame.Height, targetBytes, generation);
                if (fit is null || generation != _resizeMeasureGeneration)
                {
                    return;
                }

                ResizeScaleSlider.Value = fit.Value.Percent;
                _resizeScalePercent = fit.Value.Percent;
                ResizeScaleValueText.Text = $"{_resizeScalePercent}%";
                UpdateResizeDimensionReadout();
                UpdateExportButtonLabels();
                ShowResizeFileSize(fit.Value.Bytes);
            }
            catch (Exception ex)
            {
                if (generation == _resizeMeasureGeneration)
                {
                    ResizeFileSizeText.Text = "Size unavailable";
                    InAppToastService.Show($"Could not fit the image ({ex.Message}).", InAppToastSeverity.Error);
                }
            }
            finally
            {
                _isFittingResize = false;
                ResizeFitButton.IsEnabled = true;
                ResizeScaleSlider.IsEnabled = true;
                ResizeMaxSizeNumberBox.IsEnabled = true;
            }
        }

        private void RefreshResizeReadout(bool scheduleMeasure)
        {
            if (_imageWidth <= 0 || _imageHeight <= 0)
            {
                return;
            }

            ResizeScaleValueText.Text = $"{_resizeScalePercent}%";
            UpdateResizeDimensionReadout();
            if (scheduleMeasure)
            {
                ScheduleResizeSizeMeasure();
            }
        }

        private void CancelResizeMeasure()
        {
            _resizeMeasureGeneration++;
            _resizeMeasureDelayCts?.Cancel();
        }

        private void ScheduleResizeSizeMeasure()
        {
            if (_isFittingResize || _isInitializingUi || _imageWidth <= 0 || _imageHeight <= 0)
            {
                return;
            }

            var generation = ++_resizeMeasureGeneration;
            _resizeMeasureDelayCts?.Cancel();
            _resizeMeasureDelayCts = new CancellationTokenSource();
            var token = _resizeMeasureDelayCts.Token;
            ShowResizeMeasuring();
            _ = MeasureResizeSizeAfterDelayAsync(generation, _resizeScalePercent, token);
        }

        private async Task MeasureResizeSizeAfterDelayAsync(int generation, int scalePercent, CancellationToken token)
        {
            try
            {
                await Task.Delay(ResizeMeasureDebounceMs, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (generation != _resizeMeasureGeneration)
            {
                return;
            }

            await MeasureResizeOutputAsync(generation, scalePercent);
        }

        private async Task MeasureResizeOutputAsync(int generation, int scalePercent)
        {
            try
            {
                var frame = await CaptureExportFrameAsync();
                var bytes = await EncodeScaledPngLengthAsync(frame.Pixels, frame.Width, frame.Height, scalePercent, generation);
                if (bytes is null || generation != _resizeMeasureGeneration)
                {
                    return;
                }

                ShowResizeFileSize(bytes.Value);
            }
            catch (Exception)
            {
                if (generation == _resizeMeasureGeneration)
                {
                    ResizeFileSizeText.Text = "Size unavailable";
                }
            }
        }

        private async Task SaveExportFrameAndCloseAsync(bool copyToClipboard)
        {
            var frame = await CaptureExportFrameAsync();
            var output = ImageDownscaler.GetOutputSize(frame.Width, frame.Height, _resizeScalePercent);
            var pixels = frame.Pixels;
            var width = frame.Width;
            var height = frame.Height;
            if (output.Width != width || output.Height != height)
            {
                pixels = await Task.Run(() => ImageDownscaler.Downscale(pixels, width, height, output.Width, output.Height));
                width = output.Width;
                height = output.Height;
            }

            var isResized = _resizeScalePercent < ImageDownscaler.MaximumScalePercent;
            var suffix = isResized ? "_resized" : frame.Cropped ? "_crop" : "_edited";
            var label = isResized ? "Resized image saved" : frame.Cropped ? "Cropped image saved" : "Saved";
            var outputPath = BuildOutputPath(_filePath, suffix);
            await SaveNewImageAndCloseAsync(outputPath, pixels, width, height, label, copyToClipboard);
        }

        private async Task<(byte[] Pixels, int Width, int Height, bool Cropped)> CaptureExportFrameAsync()
        {
            if (_pendingCropRect is { IsEmpty: false } region)
            {
                var fullPixels = await RenderCompositePixelsAsync();
                var cropPixels = CropPixels(fullPixels, _imageWidth, _imageHeight, region);
                return (cropPixels, region.Width, region.Height, true);
            }

            var pixels = await RenderCompositePixelsAsync();
            return (pixels, _imageWidth, _imageHeight, false);
        }

        /// <summary>
        /// Largest scale whose PNG is at or under <paramref name="targetBytes"/>.
        /// PNG size is treated as increasing with scale, which holds for this downsample.
        /// </summary>
        private async Task<(int Percent, long Bytes)?> FindLargestFittingScaleAsync(
            byte[] pixels,
            int width,
            int height,
            long targetBytes,
            int generation)
        {
            var full = await EncodeScaledPngLengthAsync(pixels, width, height, ImageDownscaler.MaximumScalePercent, generation);
            if (full is null)
            {
                return null;
            }

            if (full.Value <= targetBytes)
            {
                return (ImageDownscaler.MaximumScalePercent, full.Value);
            }

            var minimum = await EncodeScaledPngLengthAsync(pixels, width, height, ImageDownscaler.MinimumScalePercent, generation);
            if (minimum is null)
            {
                return null;
            }

            if (minimum.Value > targetBytes)
            {
                return (ImageDownscaler.MinimumScalePercent, minimum.Value);
            }

            var low = ImageDownscaler.MinimumScalePercent + 1;
            var high = ImageDownscaler.MaximumScalePercent - 1;
            var bestPercent = ImageDownscaler.MinimumScalePercent;
            var bestBytes = minimum.Value;
            while (low <= high)
            {
                if (generation != _resizeMeasureGeneration)
                {
                    return null;
                }

                var mid = (low + high) / 2;
                var size = await EncodeScaledPngLengthAsync(pixels, width, height, mid, generation);
                if (size is null)
                {
                    return null;
                }

                if (size.Value <= targetBytes)
                {
                    bestPercent = mid;
                    bestBytes = size.Value;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return (bestPercent, bestBytes);
        }

        private async Task<long?> EncodeScaledPngLengthAsync(
            byte[] pixels,
            int width,
            int height,
            int scalePercent,
            int generation)
        {
            if (generation != _resizeMeasureGeneration || width <= 0 || height <= 0)
            {
                return null;
            }

            var output = ImageDownscaler.GetOutputSize(width, height, scalePercent);
            var scaled = pixels;
            if (output.Width != width || output.Height != height)
            {
                scaled = await Task.Run(() => ImageDownscaler.Downscale(pixels, width, height, output.Width, output.Height));
            }

            if (generation != _resizeMeasureGeneration)
            {
                return null;
            }

            var png = await EncodePngBytesAsync(scaled, output.Width, output.Height);
            if (generation != _resizeMeasureGeneration)
            {
                return null;
            }

            return png.LongLength;
        }

        private void UpdateResizeDimensionReadout()
        {
            var frame = GetExportFrameSize();
            if (frame.Width <= 0 || frame.Height <= 0)
            {
                ResizeOutputSizeText.Text = "—";
                return;
            }

            var output = ImageDownscaler.GetOutputSize(frame.Width, frame.Height, _resizeScalePercent);
            ResizeOutputSizeText.Text = output.Width == frame.Width && output.Height == frame.Height
                ? $"{frame.Width} × {frame.Height}"
                : $"{frame.Width} × {frame.Height} → {output.Width} × {output.Height}";
        }

        private (int Width, int Height) GetExportFrameSize()
        {
            if (_pendingCropRect is { IsEmpty: false } crop)
            {
                return (crop.Width, crop.Height);
            }

            return (_imageWidth, _imageHeight);
        }

        private void ShowResizeMeasuring()
        {
            var source = TryGetSourceFileBytes();
            ResizeFileSizeText.Text = source is long sourceBytes
                ? $"{FormatByteSize(sourceBytes)} → Measuring…"
                : "Measuring…";
        }

        private void ShowResizeFileSize(long outputBytes)
        {
            var source = TryGetSourceFileBytes();
            ResizeFileSizeText.Text = source is long sourceBytes
                ? $"{FormatByteSize(sourceBytes)} → {FormatByteSize(outputBytes)}"
                : FormatByteSize(outputBytes);
        }

        private long? TryGetSourceFileBytes()
        {
            if (_sourceFileBytes.HasValue)
            {
                return _sourceFileBytes;
            }

            try
            {
                var info = new FileInfo(_filePath);
                if (info.Exists)
                {
                    _sourceFileBytes = info.Length;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return _sourceFileBytes;
        }

        private static string FormatByteSize(long bytes)
        {
            const double kilobyte = 1024d;
            const double megabyte = 1024d * 1024d;
            if (bytes >= megabyte)
            {
                return $"{bytes / megabyte:0.#} MB";
            }

            if (bytes >= kilobyte)
            {
                return $"{Math.Round(bytes / kilobyte):0} KB";
            }

            return $"{bytes} B";
        }
    }
}
