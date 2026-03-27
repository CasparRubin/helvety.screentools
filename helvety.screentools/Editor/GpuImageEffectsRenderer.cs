using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;
using Windows.Graphics.DirectX;

namespace helvety.screentools.Editor
{
    internal sealed class GpuImageEffectsRenderer
    {
        private static readonly Color OpaqueMask = Color.FromArgb(255, 255, 255, 255);
        private static readonly Color TransparentMask = Color.FromArgb(0, 0, 0, 0);
        private readonly CanvasDevice _canvasDevice = CanvasDevice.GetSharedDevice();
        private bool _isAvailable = true;

        internal string? LastError { get; private set; }

        internal async Task<bool> TryRenderAsync(
            byte[] sourcePixels,
            int imageWidth,
            int imageHeight,
            IReadOnlyList<EditorLayer> layers,
            bool blurInvertMode,
            int highlightDimPercent,
            bool highlightInvertMode,
            byte[] destinationPixels,
            CancellationToken cancellationToken = default)
        {
            if (!_isAvailable || sourcePixels.Length == 0 || imageWidth <= 0 || imageHeight <= 0)
            {
                return false;
            }

            try
            {
                var renderedPixels = await Task.Run(
                    () => RenderInternal(sourcePixels, imageWidth, imageHeight, layers, blurInvertMode, highlightDimPercent, highlightInvertMode),
                    cancellationToken);
                if (renderedPixels.Length != destinationPixels.Length)
                {
                    LastError = "GPU output pixel size mismatch.";
                    _isAvailable = false;
                    return false;
                }

                Buffer.BlockCopy(renderedPixels, 0, destinationPixels, 0, renderedPixels.Length);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _isAvailable = false;
                return false;
            }
        }

        private byte[] RenderInternal(
            byte[] sourcePixels,
            int imageWidth,
            int imageHeight,
            IReadOnlyList<EditorLayer> layers,
            bool blurInvertMode,
            int highlightDimPercent,
            bool highlightInvertMode)
        {
            using var sourceBitmap = CanvasBitmap.CreateFromBytes(
                _canvasDevice,
                sourcePixels,
                imageWidth,
                imageHeight,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                96);

            var tempResources = new List<IDisposable>();
            try
            {
                ICanvasImage currentImage = sourceBitmap;

                foreach (var blurLayer in layers.OfType<BlurLayer>().Where(layer => layer.IsVisible))
                {
                    var clampedRegion = ClampRegion(blurLayer.Region, imageWidth, imageHeight);
                    if (clampedRegion.IsEmpty)
                    {
                        continue;
                    }

                    var blurAmount = Math.Clamp((float)blurLayer.Radius * 2f, 0f, 96f);
                    if (blurAmount <= 0f)
                    {
                        continue;
                    }

                    var blurred = new GaussianBlurEffect
                    {
                        Source = currentImage,
                        BlurAmount = blurAmount,
                        BorderMode = EffectBorderMode.Hard,
                        Optimization = EffectOptimization.Speed
                    };

                    var effectiveCornerRadius = GetEffectiveCornerRadius(clampedRegion, blurLayer.CornerRadius);
                    var featherAmount = Math.Clamp((float)blurLayer.Feather, 0f, 40f);
                    var blurMask = CreateBlurMask(
                        imageWidth,
                        imageHeight,
                        clampedRegion,
                        effectiveCornerRadius,
                        blurInvertMode,
                        featherAmount,
                        tempResources);

                    currentImage = BlendByMask(currentImage, blurred, blurMask);
                }

                var dimFactor = 1f - (Math.Clamp(highlightDimPercent, 0, 80) / 100f);
                var visibleHighlights = layers.OfType<HighlightLayer>().Where(layer => layer.IsVisible).ToArray();
                if (visibleHighlights.Length > 0 && dimFactor < 1f)
                {
                    var dimmed = new ColorMatrixEffect
                    {
                        Source = currentImage,
                        ColorMatrix = new Matrix5x4
                        {
                            M11 = dimFactor,
                            M22 = dimFactor,
                            M33 = dimFactor,
                            M44 = 1f
                        }
                    };

                    var dimMask = highlightInvertMode
                        ? CreateInsideHighlightsMask(imageWidth, imageHeight, visibleHighlights)
                        : CreateOutsideHighlightsMask(imageWidth, imageHeight, visibleHighlights);
                    tempResources.Add(dimMask);
                    currentImage = BlendByMask(currentImage, dimmed, dimMask);
                }

                using var target = new CanvasRenderTarget(_canvasDevice, imageWidth, imageHeight, 96);
                using (var drawingSession = target.CreateDrawingSession())
                {
                    drawingSession.Clear(Color.FromArgb(0, 0, 0, 0));
                    drawingSession.DrawImage(currentImage);
                }

                return target.GetPixelBytes();
            }
            finally
            {
                foreach (var resource in tempResources)
                {
                    resource.Dispose();
                }
            }
        }

        private CanvasCommandList CreateInsideHighlightsMask(
            int imageWidth,
            int imageHeight,
            IReadOnlyList<HighlightLayer> highlights)
        {
            var mask = new CanvasCommandList(_canvasDevice);
            using var drawingSession = mask.CreateDrawingSession();
            drawingSession.Clear(TransparentMask);
            foreach (var highlightLayer in highlights)
            {
                var region = ClampRegion(highlightLayer.Region, imageWidth, imageHeight);
                if (region.IsEmpty)
                {
                    continue;
                }

                var cornerRadius = GetEffectiveCornerRadius(region, highlightLayer.CornerRadius);
                drawingSession.FillRoundedRectangle(region.X, region.Y, region.Width, region.Height, cornerRadius, cornerRadius, OpaqueMask);
            }

            return mask;
        }

        private CanvasCommandList CreateOutsideHighlightsMask(
            int imageWidth,
            int imageHeight,
            IReadOnlyList<HighlightLayer> highlights)
        {
            var outsideGeometry = CanvasGeometry.CreateRectangle(_canvasDevice, 0, 0, imageWidth, imageHeight);
            foreach (var highlightLayer in highlights)
            {
                var region = ClampRegion(highlightLayer.Region, imageWidth, imageHeight);
                if (region.IsEmpty)
                {
                    continue;
                }

                var cornerRadius = GetEffectiveCornerRadius(region, highlightLayer.CornerRadius);
                using var regionGeometry = CanvasGeometry.CreateRoundedRectangle(
                    _canvasDevice,
                    region.X,
                    region.Y,
                    region.Width,
                    region.Height,
                    cornerRadius,
                    cornerRadius);
                var newOutside = outsideGeometry.CombineWith(regionGeometry, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
                outsideGeometry.Dispose();
                outsideGeometry = newOutside;
            }

            var mask = new CanvasCommandList(_canvasDevice);
            using (var drawingSession = mask.CreateDrawingSession())
            {
                drawingSession.Clear(TransparentMask);
                drawingSession.FillGeometry(outsideGeometry, OpaqueMask);
            }

            outsideGeometry.Dispose();
            return mask;
        }

        private CanvasCommandList CreateInsideRoundedRectMask(
            int imageWidth,
            int imageHeight,
            EditorRect region,
            int cornerRadius)
        {
            var mask = new CanvasCommandList(_canvasDevice);
            using var drawingSession = mask.CreateDrawingSession();
            drawingSession.Clear(TransparentMask);
            drawingSession.FillRoundedRectangle(region.X, region.Y, region.Width, region.Height, cornerRadius, cornerRadius, OpaqueMask);

            return mask;
        }

        private CanvasCommandList CreateOutsideRoundedRectMask(
            int imageWidth,
            int imageHeight,
            EditorRect region,
            int cornerRadius)
        {
            using var fullGeometry = CanvasGeometry.CreateRectangle(_canvasDevice, 0, 0, imageWidth, imageHeight);
            using var insideGeometry = CanvasGeometry.CreateRoundedRectangle(
                _canvasDevice,
                region.X,
                region.Y,
                region.Width,
                region.Height,
                cornerRadius,
                cornerRadius);
            using var outsideGeometry = fullGeometry.CombineWith(insideGeometry, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);

            var mask = new CanvasCommandList(_canvasDevice);
            using var drawingSession = mask.CreateDrawingSession();
            drawingSession.Clear(TransparentMask);
            drawingSession.FillGeometry(outsideGeometry, OpaqueMask);

            return mask;
        }

        private ICanvasImage CreateBlurMask(
            int imageWidth,
            int imageHeight,
            EditorRect region,
            int cornerRadius,
            bool blurInvertMode,
            float featherAmount,
            ICollection<IDisposable> tempResources)
        {
            var baseMask = blurInvertMode
                ? CreateOutsideRoundedRectMask(imageWidth, imageHeight, region, cornerRadius)
                : CreateInsideRoundedRectMask(imageWidth, imageHeight, region, cornerRadius);
            tempResources.Add(baseMask);
            if (featherAmount <= 0f)
            {
                return baseMask;
            }

            return new GaussianBlurEffect
            {
                Source = baseMask,
                BlurAmount = featherAmount,
                BorderMode = EffectBorderMode.Hard,
                Optimization = EffectOptimization.Speed
            };
        }

        private static ICanvasImage BlendByMask(ICanvasImage baseImage, ICanvasImage replacementImage, ICanvasImage mask)
        {
            var replacementMasked = new AlphaMaskEffect
            {
                Source = replacementImage,
                AlphaMask = mask
            };

            return new CompositeEffect
            {
                Mode = CanvasComposite.SourceOver,
                Sources =
                {
                    baseImage,
                    replacementMasked
                }
            };
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

        private static int GetEffectiveCornerRadius(EditorRect region, int requestedRadius)
        {
            var maxAllowed = Math.Max(0, Math.Min(region.Width, region.Height) / 2);
            return Math.Clamp(requestedRadius, 0, maxAllowed);
        }
    }
}
