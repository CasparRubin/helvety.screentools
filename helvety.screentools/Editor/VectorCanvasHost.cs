using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace helvety.screentools.Editor
{
    /// <summary>
    /// Reuses vector layer visuals on a <see cref="Canvas"/> when layer content and z-order are unchanged between recomposes.
    /// </summary>
    internal sealed class VectorCanvasHost
    {
        private readonly Dictionary<Guid, LayerSlot> _slots = new();
        private readonly List<Guid> _zOrder = new();

        internal void Sync(
            IEnumerable<EditorLayer> layers,
            Canvas targetCanvas,
            bool suppressExpensiveEffects,
            bool forceFullRebuild)
        {
            var visibleVectors = layers
                .Where(layer => layer.IsVisible && IsVectorLayer(layer))
                .Reverse()
                .ToList();

            var nextZOrder = visibleVectors.Select(layer => layer.Id).ToList();
            if (forceFullRebuild || !SameZOrder(nextZOrder))
            {
                FullRebuild(visibleVectors, targetCanvas, suppressExpensiveEffects);
                _zOrder.Clear();
                _zOrder.AddRange(nextZOrder);
                return;
            }

            var liveIds = new HashSet<Guid>(nextZOrder);
            var staleIds = _slots.Keys.Where(id => !liveIds.Contains(id)).ToList();
            foreach (var staleId in staleIds)
            {
                RemoveSlot(staleId, targetCanvas);
            }

            foreach (var layer in visibleVectors)
            {
                var key = LayerRenderKey.Compute(layer, suppressExpensiveEffects);
                if (_slots.TryGetValue(layer.Id, out var slot) && slot.RenderKey == key)
                {
                    continue;
                }

                if (_slots.TryGetValue(layer.Id, out var existing))
                {
                    RemoveElements(existing.Elements, targetCanvas);
                }

                var elements = EditorVectorOverlayRenderer.CreateElementsForLayer(layer, suppressExpensiveEffects);
                _slots[layer.Id] = new LayerSlot(key, elements);
                foreach (var element in elements)
                {
                    targetCanvas.Children.Add(element);
                }
            }

            ApplyZOrder(targetCanvas);
        }

        internal void Clear(Canvas targetCanvas)
        {
            foreach (var slot in _slots.Values)
            {
                RemoveElements(slot.Elements, targetCanvas);
            }

            _slots.Clear();
            _zOrder.Clear();
        }

        private void FullRebuild(
            IReadOnlyList<EditorLayer> visibleVectors,
            Canvas targetCanvas,
            bool suppressExpensiveEffects)
        {
            Clear(targetCanvas);
            foreach (var layer in visibleVectors)
            {
                var key = LayerRenderKey.Compute(layer, suppressExpensiveEffects);
                var elements = EditorVectorOverlayRenderer.CreateElementsForLayer(layer, suppressExpensiveEffects);
                _slots[layer.Id] = new LayerSlot(key, elements);
                foreach (var element in elements)
                {
                    targetCanvas.Children.Add(element);
                }
            }
        }

        private bool SameZOrder(IReadOnlyList<Guid> nextZOrder)
        {
            if (_zOrder.Count != nextZOrder.Count)
            {
                return false;
            }

            for (var i = 0; i < _zOrder.Count; i++)
            {
                if (_zOrder[i] != nextZOrder[i])
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyZOrder(Canvas targetCanvas)
        {
            var index = 0;
            foreach (var id in _zOrder)
            {
                if (!_slots.TryGetValue(id, out var slot))
                {
                    continue;
                }

                foreach (var element in slot.Elements)
                {
                    Canvas.SetZIndex(element, index++);
                }
            }
        }

        private void RemoveSlot(Guid id, Canvas targetCanvas)
        {
            if (_slots.TryGetValue(id, out var slot))
            {
                RemoveElements(slot.Elements, targetCanvas);
                _slots.Remove(id);
            }
        }

        private static void RemoveElements(IReadOnlyList<UIElement> elements, Canvas targetCanvas)
        {
            foreach (var element in elements)
            {
                targetCanvas.Children.Remove(element);
            }
        }

        private static bool IsVectorLayer(EditorLayer layer)
        {
            return layer is TextLayer or BorderLayer or ArrowLayer or MagnifierLayer;
        }

        private sealed record LayerSlot(long RenderKey, IReadOnlyList<UIElement> Elements);

        private static class LayerRenderKey
        {
            internal static long Compute(EditorLayer layer, bool suppressExpensiveEffects)
            {
                var hash = new HashCode();
                hash.Add(suppressExpensiveEffects);
                hash.Add(layer.Id);
                hash.Add(layer.IsVisible);
                switch (layer)
                {
                    case TextLayer text:
                        hash.Add(text.Text);
                        hash.Add(text.X);
                        hash.Add(text.Y);
                        hash.Add(text.FontSize);
                        hash.Add(text.WrapWidth);
                        hash.Add(text.ColorHex);
                        hash.Add(text.BorderColorHex);
                        hash.Add(text.BorderThickness);
                        hash.Add(text.HasBorder);
                        hash.Add(text.HasShadow);
                        hash.Add(text.ShadowColorHex);
                        hash.Add(text.ShadowOffset);
                        hash.Add(text.FontFamily);
                        hash.Add(text.IsBold);
                        hash.Add(text.IsItalic);
                        break;
                    case BorderLayer border:
                        hash.Add(border.Region.X);
                        hash.Add(border.Region.Y);
                        hash.Add(border.Region.Width);
                        hash.Add(border.Region.Height);
                        hash.Add(border.Thickness);
                        hash.Add(border.ColorHex);
                        hash.Add(border.CornerRadius);
                        hash.Add(border.HasShadow);
                        hash.Add(border.ShadowColorHex);
                        hash.Add(border.ShadowOffset);
                        break;
                    case ArrowLayer arrow:
                        hash.Add(arrow.StartX);
                        hash.Add(arrow.StartY);
                        hash.Add(arrow.EndX);
                        hash.Add(arrow.EndY);
                        hash.Add(arrow.MidX);
                        hash.Add(arrow.MidY);
                        hash.Add(arrow.Thickness);
                        hash.Add(arrow.ColorHex);
                        hash.Add(arrow.BorderColorHex);
                        hash.Add(arrow.BorderThickness);
                        hash.Add(arrow.HasBorder);
                        hash.Add(arrow.HasShadow);
                        hash.Add(arrow.ShadowColorHex);
                        hash.Add(arrow.ShadowOffset);
                        hash.Add((int)arrow.FormStyle);
                        break;
                    case MagnifierLayer magnifier:
                        hash.Add(magnifier.SourceRegion.X);
                        hash.Add(magnifier.SourceRegion.Y);
                        hash.Add(magnifier.SourceRegion.Width);
                        hash.Add(magnifier.SourceRegion.Height);
                        hash.Add(magnifier.LoupeRegion.X);
                        hash.Add(magnifier.LoupeRegion.Y);
                        hash.Add(magnifier.LoupeRegion.Width);
                        hash.Add(magnifier.LoupeRegion.Height);
                        hash.Add((int)magnifier.Shape);
                        hash.Add(magnifier.Zoom);
                        hash.Add(magnifier.Thickness);
                        hash.Add(magnifier.ColorHex);
                        hash.Add(magnifier.HasShadow);
                        hash.Add(magnifier.ShadowColorHex);
                        hash.Add(magnifier.ShadowOffset);
                        hash.Add(magnifier.ContentGeneration);
                        break;
                }

                return hash.ToHashCode();
            }
        }
    }
}
