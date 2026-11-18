using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace helvety.screentools.Editor
{
    /// <summary>
    /// Win2D compositor for blur and highlight effects (semantics in <see cref="PixelEffects"/>).
    /// Region blurs only process a padded ROI; inverted blur processes the full frame once.
    /// </summary>
    internal sealed class GpuImageEffectsRenderer
    {
        private static readonly Color OpaqueMask = Color.FromArgb(255, 255, 255, 255);
        private static readonly Color TransparentMask = Color.FromArgb(0, 0, 0, 0);
        private CanvasDevice _canvasDevice = CanvasDevice.GetSharedDevice();
        private bool _isAvailable = true;

        internal string? LastError { get; private set; }

        /// <returns>False when the GPU path failed; the caller then uses the CPU renderer for this editor session.</returns>
        internal async Task<bool> TryRenderAsync(
            byte[] sourcePixels,
            int imageWidth,
            int imageHeight,
            PixelEffects effects,
            byte[] destinationPixels,
            CancellationToken cancellationToken = default)
        {
            if (!_isAvailable || sourcePixels.Length == 0 || imageWidth <= 0 || imageHeight <= 0)
            {
                return false;
            }

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var device = _canvasDevice;
                    var renderedPixels = await Task.Run(
                        () => RenderInternal(device, sourcePixels, imageWidth, imageHeight, effects, cancellationToken),
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (renderedPixels.Length != destinationPixels.Length)
                    {
                        LastError = "GPU output pixel size mismatch.";
                        _isAvailable = false;
                        return false;
                    }

                    Buffer.BlockCopy(renderedPixels, 0, destinationPixels, 0, renderedPixels.Length);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (attempt == 0 && _canvasDevice.IsDeviceLost(ex.HResult))
                {
                    // Driver update / GPU reset: Win2D hands out a fresh shared device once the old one is lost.
                    AppLog.Warn("Win2D device lost; recreating it.", ex);
                    _canvasDevice = CanvasDevice.GetSharedDevice();
                }
                catch (Exception ex)
                {
                    AppLog.Warn("GPU effects rendering failed; using CPU effects for this editor session.", ex);
                    LastError = ex.Message;
                    _isAvailable = false;
                    return false;
                }
            }
        }

        private static byte[] RenderInternal(
            CanvasDevice device,
            byte[] sourcePixels,
            int imageWidth,
            int imageHeight,
            PixelEffects effects,
            CancellationToken cancellationToken)
        {
            using var sourceBitmap = CanvasBitmap.CreateFromBytes(
                device,
                sourcePixels,
                imageWidth,
                imageHeight,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                96);

            var resources = new List<IDisposable>();
            try
            {
                ICanvasImage currentImage = sourceBitmap;
                if (effects.Blurs.Count > 0)
                {
                    currentImage = effects.BlurInvertMode
                        ? ApplyInvertedBlur(device, currentImage, imageWidth, imageHeight, effects, resources)
                        : ApplyRegionBlurs(device, currentImage, imageWidth, imageHeight, effects, resources, cancellationToken);
                }

                if (effects.HasHighlightDim)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var dimFactor = effects.HighlightDimFactor;
                    var dimmed = Track(resources, new ColorMatrixEffect
                    {
                        Source = currentImage,
                        ColorMatrix = new Matrix5x4 { M11 = dimFactor, M22 = dimFactor, M33 = dimFactor, M44 = 1f }
                    });
                    var dimMask = CreateRegionsMask(
                        device,
                        imageWidth,
                        imageHeight,
                        effects.Highlights.Select(highlight => (highlight.Region, highlight.CornerRadius)),
                        outside: !effects.HighlightInvertMode,
                        feather: 0f,
                        resources);
                    currentImage = BlendByMask(currentImage, dimmed, dimMask, resources);
                }

                using var target = new CanvasRenderTarget(device, imageWidth, imageHeight, 96);
                using (var drawingSession = target.CreateDrawingSession())
                {
                    drawingSession.Clear(TransparentMask);
                    drawingSession.DrawImage(currentImage);
                }

                return target.GetPixelBytes();
            }
            finally
            {
                foreach (var resource in resources)
                {
                    resource.Dispose();
                }
            }
        }

        private static ICanvasImage ApplyRegionBlurs(
            CanvasDevice device,
            ICanvasImage currentImage,
            int imageWidth,
            int imageHeight,
            PixelEffects effects,
            List<IDisposable> resources,
            CancellationToken cancellationToken)
        {
            foreach (var blur in effects.Blurs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var blurAmount = ToBlurAmount(blur.Radius);
                var feather = Math.Clamp(blur.Feather, 0f, PixelEffects.MaxFeather);
                // The blurred patch must cover the feathered mask edge, which reaches about 2 sigma outside the region.
                var pad = (int)Math.Ceiling((blurAmount + feather) * 2f);
                var roi = ExpandRegion(blur.Region, pad, imageWidth, imageHeight);
                var roiRect = ToRect(roi);

                var roiSource = Track(resources, new CropEffect { Source = currentImage, SourceRectangle = roiRect });
                var blurredRoi = Track(resources, new GaussianBlurEffect
                {
                    Source = roiSource,
                    BlurAmount = blurAmount,
                    BorderMode = EffectBorderMode.Hard,
                    Optimization = EffectOptimization.Speed
                });

                // Normalize the cropped ROI into its own surface, then translate it back so mask and pixels align.
                var roiNormalized = Track(resources, new CanvasRenderTarget(device, roi.Width, roi.Height, 96));
                using (var normalizeSession = roiNormalized.CreateDrawingSession())
                {
                    normalizeSession.Clear(TransparentMask);
                    normalizeSession.DrawImage(blurredRoi, new Rect(0, 0, roi.Width, roi.Height), roiRect);
                }

                var placed = Track(resources, new Transform2DEffect
                {
                    Source = roiNormalized,
                    TransformMatrix = Matrix3x2.CreateTranslation(roi.X, roi.Y)
                });
                var mask = CreateRegionsMask(device, imageWidth, imageHeight, [(blur.Region, blur.CornerRadius)], outside: false, feather, resources);
                currentImage = BlendByMask(currentImage, placed, mask, resources);
            }

            return currentImage;
        }

        private static ICanvasImage ApplyInvertedBlur(
            CanvasDevice device,
            ICanvasImage currentImage,
            int imageWidth,
            int imageHeight,
            PixelEffects effects,
            List<IDisposable> resources)
        {
            var blurred = Track(resources, new GaussianBlurEffect
            {
                Source = currentImage,
                BlurAmount = ToBlurAmount(effects.Blurs.Max(blur => blur.Radius)),
                BorderMode = EffectBorderMode.Hard,
                Optimization = EffectOptimization.Speed
            });
            var mask = CreateRegionsMask(
                device,
                imageWidth,
                imageHeight,
                effects.Blurs.Select(blur => (blur.Region, blur.CornerRadius)),
                outside: true,
                feather: Math.Clamp(effects.Blurs.Max(blur => blur.Feather), 0f, PixelEffects.MaxFeather),
                resources);
            return BlendByMask(currentImage, blurred, mask, resources);
        }

        /// <summary>Opaque inside the union of the rounded regions (or outside it), optionally feathered.</summary>
        private static ICanvasImage CreateRegionsMask(
            CanvasDevice device,
            int imageWidth,
            int imageHeight,
            IEnumerable<(EditorRect Region, int CornerRadius)> regions,
            bool outside,
            float feather,
            List<IDisposable> resources)
        {
            var mask = Track(resources, new CanvasCommandList(device));
            using (var drawingSession = mask.CreateDrawingSession())
            {
                drawingSession.Clear(TransparentMask);
                if (!outside)
                {
                    foreach (var (region, cornerRadius) in regions)
                    {
                        drawingSession.FillRoundedRectangle(region.X, region.Y, region.Width, region.Height, cornerRadius, cornerRadius, OpaqueMask);
                    }
                }
                else
                {
                    var outsideGeometry = CanvasGeometry.CreateRectangle(device, 0, 0, imageWidth, imageHeight);
                    foreach (var (region, cornerRadius) in regions)
                    {
                        using var regionGeometry = CanvasGeometry.CreateRoundedRectangle(
                            device,
                            region.X,
                            region.Y,
                            region.Width,
                            region.Height,
                            cornerRadius,
                            cornerRadius);
                        var remaining = outsideGeometry.CombineWith(regionGeometry, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
                        outsideGeometry.Dispose();
                        outsideGeometry = remaining;
                    }

                    drawingSession.FillGeometry(outsideGeometry, OpaqueMask);
                    outsideGeometry.Dispose();
                }
            }

            if (feather <= 0f)
            {
                return mask;
            }

            return Track(resources, new GaussianBlurEffect
            {
                Source = mask,
                BlurAmount = feather,
                BorderMode = EffectBorderMode.Hard,
                Optimization = EffectOptimization.Speed
            });
        }

        private static ICanvasImage BlendByMask(ICanvasImage baseImage, ICanvasImage replacementImage, ICanvasImage mask, List<IDisposable> resources)
        {
            var replacementMasked = Track(resources, new AlphaMaskEffect { Source = replacementImage, AlphaMask = mask });
            return Track(resources, new CompositeEffect
            {
                Mode = CanvasComposite.SourceOver,
                Sources = { baseImage, replacementMasked }
            });
        }

        private static T Track<T>(List<IDisposable> resources, T resource) where T : IDisposable
        {
            resources.Add(resource);
            return resource;
        }

        private static float ToBlurAmount(int radius) => Math.Clamp(radius * 2f, 0f, 96f);

        private static Rect ToRect(EditorRect region) => new(region.X, region.Y, region.Width, region.Height);

        private static EditorRect ExpandRegion(EditorRect region, int pad, int imageWidth, int imageHeight)
        {
            var x = Math.Max(0, region.X - pad);
            var y = Math.Max(0, region.Y - pad);
            var right = Math.Min(imageWidth, region.X + region.Width + pad);
            var bottom = Math.Min(imageHeight, region.Y + region.Height + pad);
            return new EditorRect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
        }
    }
}
