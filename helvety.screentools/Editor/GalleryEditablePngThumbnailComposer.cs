using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace helvety.screentools.Editor
{
    /// <summary>
    /// Detects editable PNGs whose raster omits vector overlays (schema v2+) and builds scaled layer clones for thumbnail compositing.
    /// </summary>
    internal static class GalleryEditablePngThumbnailComposer
    {
        internal const uint MaxThumbnailDecodeWidth = 520;
        internal const long MaxPixelAreaForComposite = 40_000_000;

        internal static bool HasVisibleVectorOverlay(IEnumerable<EditorLayer> layers)
        {
            return layers.Any(layer =>
                layer.IsVisible && layer is TextLayer or BorderLayer or ArrowLayer);
        }

        /// <summary>
        /// Returns true when the gallery should composite vectors on top of the decoded raster for a thumbnail.
        /// </summary>
        internal static bool TryGetThumbnailCompositePlan(
            byte[] pngBytes,
            string sourcePath,
            int pixelWidth,
            int pixelHeight,
            [NotNullWhen(true)] out EditorDocument? document,
            out int loadedSchemaVersion,
            out double uniformScale,
            out int scaledWidth,
            out int scaledHeight)
        {
            document = null;
            loadedSchemaVersion = 0;
            uniformScale = 1;
            scaledWidth = pixelWidth;
            scaledHeight = pixelHeight;

            if (pngBytes.Length == 0 ||
                pixelWidth <= 0 ||
                pixelHeight <= 0 ||
                (long)pixelWidth * pixelHeight > MaxPixelAreaForComposite)
            {
                return false;
            }

            if (!PngEditableMetadataCodec.TryReadEditableState(pngBytes, out var payloadJson))
            {
                return false;
            }

            if (!EditorDocumentSerialization.TryDeserialize(
                    payloadJson,
                    sourcePath,
                    pixelWidth,
                    pixelHeight,
                    out var restored,
                    out _,
                    out loadedSchemaVersion))
            {
                return false;
            }

            if (loadedSchemaVersion < 2 || !HasVisibleVectorOverlay(restored.Layers))
            {
                return false;
            }

            document = restored;

            uint targetW = (uint)pixelWidth;
            uint targetH = (uint)pixelHeight;
            if (targetW > MaxThumbnailDecodeWidth)
            {
                var ratio = (double)MaxThumbnailDecodeWidth / targetW;
                targetW = MaxThumbnailDecodeWidth;
                targetH = (uint)Math.Max(1, Math.Round(targetH * ratio));
            }

            uniformScale = targetW / (double)pixelWidth;
            scaledWidth = (int)targetW;
            scaledHeight = (int)targetH;
            return true;
        }

        /// <summary>
        /// Visible vector layers scaled to thumbnail space, in back-to-front paint order (matches editor <c>Layers.Where(visible).Reverse()</c>).
        /// </summary>
        internal static List<EditorLayer> ScaleVectorLayersInDrawOrder(IReadOnlyList<EditorLayer> layers, double scale)
        {
            var result = new List<EditorLayer>();
            foreach (var layer in layers.Where(l => l.IsVisible).Reverse())
            {
                switch (layer)
                {
                    case TextLayer text:
                        result.Add(ScaleTextLayer(text, scale));
                        break;
                    case BorderLayer border:
                        result.Add(ScaleBorderLayer(border, scale));
                        break;
                    case ArrowLayer arrow:
                        result.Add(ScaleArrowLayer(arrow, scale));
                        break;
                }
            }

            return result;
        }

        private static TextLayer ScaleTextLayer(TextLayer text, double scale)
        {
            var scaled = new TextLayer(
                text.Text,
                text.X * scale,
                text.Y * scale,
                Math.Max(6, text.FontSize * scale),
                text.ColorHex,
                Math.Max(1, (int)Math.Round(text.WrapWidth * scale)))
            {
                FontFamily = text.FontFamily,
                IsBold = text.IsBold,
                IsItalic = text.IsItalic,
                HasBorder = text.HasBorder,
                BorderColorHex = text.BorderColorHex,
                BorderThickness = Math.Max(1, (int)Math.Round(text.BorderThickness * scale)),
                HasShadow = text.HasShadow,
                ShadowColorHex = text.ShadowColorHex,
                ShadowOffset = Math.Max(0, (int)Math.Round(text.ShadowOffset * scale))
            };
            scaled.Name = text.Name;
            scaled.IsVisible = text.IsVisible;
            return scaled;
        }

        private static BorderLayer ScaleBorderLayer(BorderLayer border, double scale)
        {
            var r = border.Region;
            var scaledRegion = new EditorRect(
                (int)Math.Round(r.X * scale),
                (int)Math.Round(r.Y * scale),
                Math.Max(1, (int)Math.Round(r.Width * scale)),
                Math.Max(1, (int)Math.Round(r.Height * scale)));
            var scaled = new BorderLayer(
                scaledRegion,
                Math.Max(1, (int)Math.Round(border.Thickness * scale)),
                border.ColorHex)
            {
                CornerRadius = (int)Math.Round(border.CornerRadius * scale),
                HasShadow = border.HasShadow,
                ShadowColorHex = border.ShadowColorHex,
                ShadowOffset = Math.Max(0, (int)Math.Round(border.ShadowOffset * scale))
            };
            scaled.Name = border.Name;
            scaled.IsVisible = border.IsVisible;
            return scaled;
        }

        private static ArrowLayer ScaleArrowLayer(ArrowLayer arrow, double scale)
        {
            var scaled = new ArrowLayer(
                arrow.StartX * scale,
                arrow.StartY * scale,
                arrow.EndX * scale,
                arrow.EndY * scale,
                Math.Max(1, arrow.Thickness * scale),
                arrow.ColorHex,
                arrow.FormStyle)
            {
                HasBorder = arrow.HasBorder,
                BorderColorHex = arrow.BorderColorHex,
                BorderThickness = Math.Max(1, (int)Math.Round(arrow.BorderThickness * scale)),
                HasShadow = arrow.HasShadow,
                ShadowColorHex = arrow.ShadowColorHex,
                ShadowOffset = Math.Max(0, (int)Math.Round(arrow.ShadowOffset * scale))
            };
            scaled.Name = arrow.Name;
            scaled.IsVisible = arrow.IsVisible;
            return scaled;
        }
    }
}
