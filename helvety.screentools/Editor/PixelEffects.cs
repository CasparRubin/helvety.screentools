using System;
using System.Collections.Generic;
using System.Linq;

namespace helvety.screentools.Editor
{
    /// <summary>Immutable copy of a visible blur layer, taken on the UI thread before rendering on a worker thread.</summary>
    internal readonly record struct BlurEffect(EditorRect Region, int Radius, int Feather, int CornerRadius);

    /// <summary>Immutable copy of a visible highlight layer, taken on the UI thread before rendering on a worker thread.</summary>
    internal readonly record struct HighlightEffect(EditorRect Region, int CornerRadius);

    /// <summary>
    /// Pixel-effect description shared by the GPU and CPU renderers so both produce the same result:
    /// <list type="bullet">
    /// <item>Blur: each region is blurred; in invert mode everything outside the union of all regions is blurred.</item>
    /// <item>Highlight: everything outside the union of all regions is dimmed; in invert mode the regions themselves are dimmed.</item>
    /// </list>
    /// </summary>
    internal sealed record PixelEffects(
        IReadOnlyList<BlurEffect> Blurs,
        bool BlurInvertMode,
        IReadOnlyList<HighlightEffect> Highlights,
        int HighlightDimPercent,
        bool HighlightInvertMode)
    {
        internal const float MaxFeather = 40f;

        internal float HighlightDimFactor => 1f - (Math.Clamp(HighlightDimPercent, 0, 80) / 100f);

        internal bool HasHighlightDim => Highlights.Count > 0 && HighlightDimFactor < 1f;

        internal static PixelEffects Capture(
            IEnumerable<EditorLayer> layers,
            int imageWidth,
            int imageHeight,
            bool blurInvertMode,
            int highlightDimPercent,
            bool highlightInvertMode)
        {
            var visible = layers.Where(layer => layer.IsVisible).ToArray();
            var blurs = visible
                .OfType<BlurLayer>()
                .Select(layer => (layer, region: ClampRegion(layer.Region, imageWidth, imageHeight)))
                .Where(item => !item.region.IsEmpty && item.layer.Radius > 0)
                .Select(item => new BlurEffect(
                    item.region,
                    item.layer.Radius,
                    item.layer.Feather,
                    GetEffectiveCornerRadius(item.region, item.layer.CornerRadius)))
                .ToArray();
            var highlights = visible
                .OfType<HighlightLayer>()
                .Select(layer => (layer, region: ClampRegion(layer.Region, imageWidth, imageHeight)))
                .Where(item => !item.region.IsEmpty)
                .Select(item => new HighlightEffect(item.region, GetEffectiveCornerRadius(item.region, item.layer.CornerRadius)))
                .ToArray();
            return new PixelEffects(blurs, blurInvertMode, highlights, highlightDimPercent, highlightInvertMode);
        }

        /// <summary>
        /// Coverage (0..1) of a pixel center by a rounded rectangle, softened across the edge by <paramref name="feather"/>.
        /// </summary>
        internal static float Coverage(int x, int y, EditorRect region, int cornerRadius, float feather)
        {
            var halfWidth = region.Width / 2f;
            var halfHeight = region.Height / 2f;
            var qx = MathF.Abs(x + 0.5f - (region.X + halfWidth)) - (halfWidth - cornerRadius);
            var qy = MathF.Abs(y + 0.5f - (region.Y + halfHeight)) - (halfHeight - cornerRadius);
            var outside = MathF.Sqrt((MathF.Max(qx, 0f) * MathF.Max(qx, 0f)) + (MathF.Max(qy, 0f) * MathF.Max(qy, 0f)));
            var distance = outside + MathF.Min(MathF.Max(qx, qy), 0f) - cornerRadius;
            if (feather <= 0f)
            {
                return distance <= 0f ? 1f : 0f;
            }

            // A smoothstep over +/-2 feather closely matches the GPU path's Gaussian-blurred mask (sigma = feather).
            var t = Math.Clamp(((2f * feather) - distance) / (4f * feather), 0f, 1f);
            return t * t * (3f - (2f * t));
        }

        private static EditorRect ClampRegion(EditorRect region, int imageWidth, int imageHeight)
        {
            var x = Math.Clamp(region.X, 0, Math.Max(0, imageWidth));
            var y = Math.Clamp(region.Y, 0, Math.Max(0, imageHeight));
            var width = Math.Clamp(region.Width, 0, Math.Max(0, imageWidth - x));
            var height = Math.Clamp(region.Height, 0, Math.Max(0, imageHeight - y));
            return new EditorRect(x, y, width, height);
        }

        private static int GetEffectiveCornerRadius(EditorRect region, int requestedRadius)
        {
            var maxAllowed = Math.Max(0, Math.Min(region.Width, region.Height) / 2);
            return Math.Clamp(requestedRadius, 0, maxAllowed);
        }
    }
}
