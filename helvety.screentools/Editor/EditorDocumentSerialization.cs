using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace helvety.screentools.Editor
{
    internal sealed record EditorRuntimeState(
        bool BlurInvertMode,
        int HighlightDimPercent,
        bool HighlightInvertMode,
        int RegionCornerRadius);

    internal static class EditorDocumentSerialization
    {
        private const int CurrentSchemaVersion = 2;
        private const int MaxHighlightDimPercent = 80;
        private const int MaxRegionCornerRadius = 24;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        internal static string Serialize(EditorDocument document, EditorRuntimeState runtimeState)
        {
            var payload = new EditorDocumentPayload
            {
                SchemaVersion = CurrentSchemaVersion,
                RasterExcludesVectorOverlays = true,
                CanvasWidth = document.Width,
                CanvasHeight = document.Height,
                SavedAtUtc = DateTimeOffset.UtcNow,
                EditorState = new EditorStatePayload
                {
                    BlurInvertMode = runtimeState.BlurInvertMode,
                    HighlightDimPercent = Clamp(runtimeState.HighlightDimPercent, 0, MaxHighlightDimPercent),
                    HighlightInvertMode = runtimeState.HighlightInvertMode,
                    RegionCornerRadius = Clamp(runtimeState.RegionCornerRadius, 0, MaxRegionCornerRadius)
                },
                Layers = document.Layers.Select(ToLayerPayload).Where(x => x is not null).Cast<LayerPayload>().ToList()
            };

            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        internal static bool TryDeserialize(
            string json,
            string sourcePath,
            int width,
            int height,
            out EditorDocument document,
            out EditorRuntimeState runtimeState,
            out int loadedSchemaVersion)
        {
            loadedSchemaVersion = 0;
            document = new EditorDocument(sourcePath, width, height);
            runtimeState = new EditorRuntimeState(false, 35, false, 8);

            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                var payload = JsonSerializer.Deserialize<EditorDocumentPayload>(json, JsonOptions);
                if (payload is null || payload.SchemaVersion <= 0 || payload.Layers is null)
                {
                    return false;
                }

                if (payload.CanvasWidth != width || payload.CanvasHeight != height)
                {
                    return false;
                }

                loadedSchemaVersion = payload.SchemaVersion;

                foreach (var layerPayload in payload.Layers)
                {
                    var layer = FromLayerPayload(layerPayload);
                    if (layer is not null)
                    {
                        document.Layers.Add(layer);
                    }
                }

                if (payload.EditorState is not null)
                {
                    runtimeState = new EditorRuntimeState(
                        payload.EditorState.BlurInvertMode,
                        Clamp(payload.EditorState.HighlightDimPercent, 0, MaxHighlightDimPercent),
                        payload.EditorState.HighlightInvertMode,
                        Clamp(payload.EditorState.RegionCornerRadius, 0, MaxRegionCornerRadius));
                }

                return true;
            }
            catch
            {
                loadedSchemaVersion = 0;
                return false;
            }
        }

        private static LayerPayload? ToLayerPayload(EditorLayer layer)
        {
            var layerBase = new LayerPayload
            {
                LayerType = layer.LayerType.ToString(),
                Name = layer.Name,
                IsVisible = layer.IsVisible
            };

            switch (layer)
            {
                case TextLayer text:
                    layerBase.Text = text.Text;
                    layerBase.X = text.X;
                    layerBase.Y = text.Y;
                    layerBase.FontSize = text.FontSize;
                    layerBase.ColorHex = text.ColorHex;
                    layerBase.WrapWidth = text.WrapWidth;
                    layerBase.FontFamily = text.FontFamily;
                    layerBase.IsBold = text.IsBold;
                    layerBase.IsItalic = text.IsItalic;
                    layerBase.HasBorder = text.HasBorder;
                    layerBase.BorderColorHex = text.BorderColorHex;
                    layerBase.BorderThickness = text.BorderThickness;
                    layerBase.HasShadow = text.HasShadow;
                    layerBase.ShadowColorHex = text.ShadowColorHex;
                    layerBase.ShadowOffset = text.ShadowOffset;
                    return layerBase;
                case BorderLayer border:
                    layerBase.Region = ToRegion(border.Region);
                    layerBase.Thickness = border.Thickness;
                    layerBase.CornerRadius = border.CornerRadius;
                    layerBase.ColorHex = border.ColorHex;
                    layerBase.HasShadow = border.HasShadow;
                    layerBase.ShadowColorHex = border.ShadowColorHex;
                    layerBase.ShadowOffset = border.ShadowOffset;
                    return layerBase;
                case BlurLayer blur:
                    layerBase.Region = ToRegion(blur.Region);
                    layerBase.Radius = blur.Radius;
                    layerBase.Feather = blur.Feather;
                    layerBase.CornerRadius = blur.CornerRadius;
                    return layerBase;
                case HighlightLayer highlight:
                    layerBase.Region = ToRegion(highlight.Region);
                    layerBase.CornerRadius = highlight.CornerRadius;
                    return layerBase;
                case ArrowLayer arrow:
                    layerBase.StartX = arrow.StartX;
                    layerBase.StartY = arrow.StartY;
                    layerBase.EndX = arrow.EndX;
                    layerBase.EndY = arrow.EndY;
                    layerBase.ThicknessDouble = arrow.Thickness;
                    layerBase.ColorHex = arrow.ColorHex;
                    layerBase.FormStyle = arrow.FormStyle.ToString();
                    layerBase.HasBorder = arrow.HasBorder;
                    layerBase.BorderColorHex = arrow.BorderColorHex;
                    layerBase.BorderThickness = arrow.BorderThickness;
                    layerBase.HasShadow = arrow.HasShadow;
                    layerBase.ShadowColorHex = arrow.ShadowColorHex;
                    layerBase.ShadowOffset = arrow.ShadowOffset;
                    return layerBase;
                default:
                    return null;
            }
        }

        private static EditorLayer? FromLayerPayload(LayerPayload payload)
        {
            if (!Enum.TryParse<EditorLayerType>(payload.LayerType, ignoreCase: true, out var layerType))
            {
                return null;
            }

            EditorLayer? layer = layerType switch
            {
                EditorLayerType.Text => BuildTextLayer(payload),
                EditorLayerType.Border => BuildBorderLayer(payload),
                EditorLayerType.Blur => BuildBlurLayer(payload),
                EditorLayerType.Highlight => BuildHighlightLayer(payload),
                EditorLayerType.Arrow => BuildArrowLayer(payload),
                _ => null
            };

            if (layer is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(payload.Name))
            {
                layer.Name = payload.Name;
            }

            layer.IsVisible = payload.IsVisible;
            return layer;
        }

        private static EditorLayer? BuildTextLayer(LayerPayload payload)
        {
            var textLayer = new TextLayer(
                payload.Text ?? string.Empty,
                payload.X ?? 0,
                payload.Y ?? 0,
                Math.Max(8, payload.FontSize ?? 32),
                payload.ColorHex ?? "#FFD81B60",
                Math.Max(1, payload.WrapWidth ?? 260));

            textLayer.FontFamily = payload.FontFamily ?? "Segoe UI";
            textLayer.IsBold = payload.IsBold ?? true;
            textLayer.IsItalic = payload.IsItalic ?? false;
            textLayer.HasBorder = payload.HasBorder ?? true;
            textLayer.BorderColorHex = payload.BorderColorHex ?? "#FFFFFFFF";
            textLayer.BorderThickness = Math.Max(1, payload.BorderThickness ?? 1);
            textLayer.HasShadow = payload.HasShadow ?? true;
            textLayer.ShadowColorHex = payload.ShadowColorHex ?? "#66000000";
            textLayer.ShadowOffset = Math.Max(0, payload.ShadowOffset ?? 2);
            return textLayer;
        }

        private static EditorLayer? BuildBorderLayer(LayerPayload payload)
        {
            if (payload.Region is null)
            {
                return null;
            }

            var borderLayer = new BorderLayer(
                ToEditorRect(payload.Region.Value),
                Math.Max(1, payload.Thickness ?? 4),
                payload.ColorHex ?? "#FFD81B60");
            borderLayer.CornerRadius = Math.Max(0, payload.CornerRadius ?? 0);
            borderLayer.HasShadow = payload.HasShadow ?? true;
            borderLayer.ShadowColorHex = payload.ShadowColorHex ?? "#66000000";
            borderLayer.ShadowOffset = Math.Max(0, payload.ShadowOffset ?? 2);
            return borderLayer;
        }

        private static EditorLayer? BuildBlurLayer(LayerPayload payload)
        {
            if (payload.Region is null)
            {
                return null;
            }

            var blurLayer = new BlurLayer(
                ToEditorRect(payload.Region.Value),
                Math.Max(1, payload.Radius ?? 6));
            blurLayer.Feather = Math.Max(0, payload.Feather ?? 0);
            blurLayer.CornerRadius = Math.Max(0, payload.CornerRadius ?? 0);
            return blurLayer;
        }

        private static EditorLayer? BuildHighlightLayer(LayerPayload payload)
        {
            if (payload.Region is null)
            {
                return null;
            }

            var highlightLayer = new HighlightLayer(ToEditorRect(payload.Region.Value))
            {
                CornerRadius = Math.Max(0, payload.CornerRadius ?? 0)
            };
            return highlightLayer;
        }

        private static EditorLayer? BuildArrowLayer(LayerPayload payload)
        {
            if (!Enum.TryParse<ArrowFormStyle>(payload.FormStyle, ignoreCase: true, out var arrowFormStyle))
            {
                arrowFormStyle = ArrowFormStyle.Tapered;
            }

            var arrowLayer = new ArrowLayer(
                payload.StartX ?? 0,
                payload.StartY ?? 0,
                payload.EndX ?? 0,
                payload.EndY ?? 0,
                Math.Max(1, payload.ThicknessDouble ?? 4),
                payload.ColorHex ?? "#FFD81B60",
                arrowFormStyle);

            arrowLayer.HasBorder = payload.HasBorder ?? true;
            arrowLayer.BorderColorHex = payload.BorderColorHex ?? "#FFFFFFFF";
            arrowLayer.BorderThickness = Math.Max(1, payload.BorderThickness ?? 1);
            arrowLayer.HasShadow = payload.HasShadow ?? true;
            arrowLayer.ShadowColorHex = payload.ShadowColorHex ?? "#66000000";
            arrowLayer.ShadowOffset = Math.Max(0, payload.ShadowOffset ?? 2);
            return arrowLayer;
        }

        private static RegionPayload ToRegion(EditorRect region)
            => new()
            {
                X = region.X,
                Y = region.Y,
                Width = region.Width,
                Height = region.Height
            };

        private static EditorRect ToEditorRect(RegionPayload region)
            => new(
                region.X,
                region.Y,
                Math.Max(1, region.Width),
                Math.Max(1, region.Height));

        private static int Clamp(int value, int minValue, int maxValue)
            => Math.Max(minValue, Math.Min(maxValue, value));

        private sealed class EditorDocumentPayload
        {
            public int SchemaVersion { get; set; }
            public bool? RasterExcludesVectorOverlays { get; set; }
            public int CanvasWidth { get; set; }
            public int CanvasHeight { get; set; }
            public DateTimeOffset SavedAtUtc { get; set; }
            public EditorStatePayload? EditorState { get; set; }
            public List<LayerPayload>? Layers { get; set; }
        }

        private sealed class EditorStatePayload
        {
            public bool BlurInvertMode { get; set; }
            public int HighlightDimPercent { get; set; }
            public bool HighlightInvertMode { get; set; }
            public int RegionCornerRadius { get; set; }
        }

        private sealed class LayerPayload
        {
            public string LayerType { get; set; } = string.Empty;
            public string? Name { get; set; }
            public bool IsVisible { get; set; } = true;

            public string? Text { get; set; }
            public double? X { get; set; }
            public double? Y { get; set; }
            public double? FontSize { get; set; }
            public int? WrapWidth { get; set; }
            public string? FontFamily { get; set; }
            public bool? IsBold { get; set; }
            public bool? IsItalic { get; set; }

            public RegionPayload? Region { get; set; }
            public int? Thickness { get; set; }
            public int? Radius { get; set; }
            public int? Feather { get; set; }
            public int? CornerRadius { get; set; }

            public double? StartX { get; set; }
            public double? StartY { get; set; }
            public double? EndX { get; set; }
            public double? EndY { get; set; }
            public double? ThicknessDouble { get; set; }
            public string? FormStyle { get; set; }

            public string? ColorHex { get; set; }
            public bool? HasBorder { get; set; }
            public string? BorderColorHex { get; set; }
            public int? BorderThickness { get; set; }
            public bool? HasShadow { get; set; }
            public string? ShadowColorHex { get; set; }
            public int? ShadowOffset { get; set; }
        }

        private readonly struct RegionPayload
        {
            public int X { get; init; }
            public int Y { get; init; }
            public int Width { get; init; }
            public int Height { get; init; }
        }
    }
}
