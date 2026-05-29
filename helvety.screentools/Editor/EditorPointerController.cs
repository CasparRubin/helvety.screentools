namespace helvety.screentools.Editor
{
    /// <summary>
    /// Classifies pointer-move gestures so <see cref="Views.ImageEditorPage"/> can choose overlay-only updates vs deferred recomposes.
    /// </summary>
    internal static class EditorPointerController
    {
        internal enum PointerMoveKind
        {
            None = 0,
            ResizeSelection = 1,
            DragLayer = 2,
            ArrowPreview = 3,
            RegionSelection = 4
        }

        internal readonly record struct PointerMovePlan(
            PointerMoveKind Kind,
            bool DeferPixelRecompose,
            bool DeferVectorRecompose);

        internal static PointerMovePlan PlanPointerMoved(
            EditorToolType activeTool,
            bool isResizingSelection,
            bool isDraggingSelection,
            bool isPreviewingArrow,
            EditorLayer? dragLayer)
        {
            if (activeTool == EditorToolType.Canvas && isResizingSelection)
            {
                // Defer flags are resolved in ImageEditorPage from the layer being resized.
                return new PointerMovePlan(PointerMoveKind.ResizeSelection, false, false);
            }

            if (activeTool == EditorToolType.Canvas && dragLayer is not null)
            {
                var affectsPixels = dragLayer is BlurLayer or HighlightLayer;
                return new PointerMovePlan(
                    PointerMoveKind.DragLayer,
                    DeferPixelRecompose: affectsPixels,
                    DeferVectorRecompose: !affectsPixels);
            }

            if (!isDraggingSelection)
            {
                return new PointerMovePlan(PointerMoveKind.None, false, false);
            }

            if (activeTool == EditorToolType.Arrow && isPreviewingArrow)
            {
                return new PointerMovePlan(PointerMoveKind.ArrowPreview, false, false);
            }

            return new PointerMovePlan(PointerMoveKind.RegionSelection, false, false);
        }
    }
}
