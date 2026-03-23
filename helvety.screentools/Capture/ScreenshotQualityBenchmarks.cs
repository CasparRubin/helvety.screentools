#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace helvety.screentools.Capture
{
    internal static class ScreenshotQualityBenchmarks
    {
        private const int ReportEverySamples = 10;
        private static readonly object SyncRoot = new();
        private static readonly Dictionary<ScreenshotQualityMode, QualityStats> StatsByMode = new();

        internal static void RecordSample(
            ScreenshotQualityMode mode,
            int width,
            int height,
            TimeSpan processingDuration,
            byte[] pixels)
        {
            if (width <= 0 || height <= 0 || pixels.Length == 0)
            {
                return;
            }

            var edgeScore = EstimateEdgeContrast(pixels, width, height);

            lock (SyncRoot)
            {
                if (!StatsByMode.TryGetValue(mode, out var stats))
                {
                    stats = new QualityStats();
                    StatsByMode[mode] = stats;
                }

                stats.SampleCount++;
                stats.TotalProcessingMilliseconds += processingDuration.TotalMilliseconds;
                stats.TotalEdgeScore += edgeScore;
                stats.TotalMegapixels += (width * height) / 1_000_000d;

                if (stats.SampleCount % ReportEverySamples == 0)
                {
                    var averageMs = stats.TotalProcessingMilliseconds / stats.SampleCount;
                    var averageEdgeScore = stats.TotalEdgeScore / stats.SampleCount;
                    var averageMegapixels = stats.TotalMegapixels / stats.SampleCount;
                    Debug.WriteLine(
                        $"[ScreenshotQualityBenchmarks] Mode={mode} Samples={stats.SampleCount} AvgMs={averageMs:F1} AvgEdge={averageEdgeScore:F3} AvgMP={averageMegapixels:F2}");
                }
            }
        }

        private static double EstimateEdgeContrast(byte[] pixels, int width, int height)
        {
            var sum = 0d;
            var samples = 0;
            const int sampleStep = 2;

            for (var y = 0; y < height - 1; y += sampleStep)
            {
                for (var x = 0; x < width - 1; x += sampleStep)
                {
                    var index = ((y * width) + x) * 4;
                    var rightIndex = index + 4;
                    var downIndex = ((y + 1) * width + x) * 4;

                    var luminance = Luminance(pixels[index], pixels[index + 1], pixels[index + 2]);
                    var rightLuminance = Luminance(pixels[rightIndex], pixels[rightIndex + 1], pixels[rightIndex + 2]);
                    var downLuminance = Luminance(pixels[downIndex], pixels[downIndex + 1], pixels[downIndex + 2]);

                    sum += Math.Abs(luminance - rightLuminance);
                    sum += Math.Abs(luminance - downLuminance);
                    samples += 2;
                }
            }

            return samples == 0 ? 0d : sum / (samples * 255d);
        }

        private static double Luminance(byte blue, byte green, byte red)
        {
            return (0.0722d * blue) + (0.7152d * green) + (0.2126d * red);
        }

        private sealed class QualityStats
        {
            internal int SampleCount;
            internal double TotalProcessingMilliseconds;
            internal double TotalEdgeScore;
            internal double TotalMegapixels;
        }
    }
}
#endif
