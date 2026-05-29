using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;

namespace helvety.screentools.Editor
{
    /// <summary>
    /// GPU/CPU pixel compositing with generation tokens and in-flight cancellation.
    /// </summary>
    internal sealed class EditorComposePipeline
    {
        private readonly GpuImageEffectsRenderer _gpuImageEffectsRenderer = new();
        private readonly Stopwatch _composeStopwatch = new();
        private long _composeGeneration;
        private CancellationTokenSource? _gpuComposeCts;
        private int _composeSampleCount;
        private long _composeSampleTotalMs;
        private int _staleComposeDiscardCount;

        internal string? LastGpuError => _gpuImageEffectsRenderer.LastError;

        internal int StaleComposeDiscardCount => _staleComposeDiscardCount;

        internal long BeginPixelCompose()
        {
            _gpuComposeCts?.Cancel();
            _gpuComposeCts?.Dispose();
            _gpuComposeCts = new CancellationTokenSource();
            return Interlocked.Increment(ref _composeGeneration);
        }

        internal bool IsGenerationCurrent(long generation)
        {
            return generation == Volatile.Read(ref _composeGeneration);
        }

        internal async Task<PixelComposeOutcome> ComposePixelsAsync(PixelComposeRequest request)
        {
            var generation = request.Generation;
            var cancellationToken = _gpuComposeCts?.Token ?? CancellationToken.None;

            _composeStopwatch.Restart();
            var usedGpu = false;
            if (request.UseGpu)
            {
                usedGpu = await _gpuImageEffectsRenderer.TryRenderAsync(
                    request.SourcePixels,
                    request.ImageWidth,
                    request.ImageHeight,
                    request.Layers,
                    request.BlurInvertMode,
                    request.HighlightDimPercent,
                    request.HighlightInvertMode,
                    request.DestinationPixels,
                    cancellationToken).ConfigureAwait(true);
            }

            if (!IsGenerationCurrent(generation))
            {
                Interlocked.Increment(ref _staleComposeDiscardCount);
                return PixelComposeOutcome.Stale;
            }

            if (!usedGpu)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Interlocked.Increment(ref _staleComposeDiscardCount);
                    return PixelComposeOutcome.Stale;
                }

                CpuImageEffectsRenderer.Apply(
                    request.SourcePixels,
                    request.ImageWidth,
                    request.ImageHeight,
                    request.Layers,
                    request.BlurInvertMode,
                    request.HighlightDimPercent,
                    request.HighlightInvertMode,
                    request.DestinationPixels);
            }

            _composeStopwatch.Stop();
            _composeSampleCount++;
            _composeSampleTotalMs += _composeStopwatch.ElapsedMilliseconds;
            if (_composeSampleCount % 20 == 0)
            {
                var avgComposeMs = _composeSampleTotalMs / Math.Max(1, _composeSampleCount);
                Debug.WriteLine($"[ImageEditor] avg pixel compose {avgComposeMs}ms over {_composeSampleCount} runs (stale discarded {_staleComposeDiscardCount})");
            }

            return new PixelComposeOutcome(
                Succeeded: true,
                UsedGpu: usedGpu,
                UsedCpuFallback: request.UseGpu && !usedGpu);
        }

        internal async Task UploadToWriteableBitmapAsync(
            byte[] workingPixels,
            WriteableBitmap baseBitmap,
            long generation)
        {
            if (!IsGenerationCurrent(generation))
            {
                Interlocked.Increment(ref _staleComposeDiscardCount);
                return;
            }

            using (var stream = baseBitmap.PixelBuffer.AsStream())
            {
                stream.Seek(0, System.IO.SeekOrigin.Begin);
                await stream.WriteAsync(workingPixels, 0, workingPixels.Length).ConfigureAwait(true);
            }

            if (IsGenerationCurrent(generation))
            {
                baseBitmap.Invalidate();
            }
        }
    }

    internal readonly record struct PixelComposeRequest(
        long Generation,
        byte[] SourcePixels,
        int ImageWidth,
        int ImageHeight,
        IReadOnlyList<EditorLayer> Layers,
        bool BlurInvertMode,
        int HighlightDimPercent,
        bool HighlightInvertMode,
        byte[] DestinationPixels,
        bool UseGpu);

    internal readonly record struct PixelComposeOutcome(bool Succeeded, bool UsedGpu, bool UsedCpuFallback)
    {
        public static PixelComposeOutcome Stale => new(false, false, false);

        public bool IsStale => !Succeeded;
    }
}
