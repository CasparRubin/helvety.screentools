using helvety.screentools.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace helvety.screentools.Views
{
    public sealed partial class ImageEditorPage
    {
        private void QueueRecompose(bool includeAdorners = true, bool includePixelEffects = true)
        {
            _ = RecomposeAsync(includeAdorners, includePixelEffects);
        }

        private void QueueOverlayOnlyRebuild(bool includeAdorners)
        {
            if (!ShouldRenderInteractiveOverlayFrame())
            {
                return;
            }

            var suppressExpensiveEffects = ShouldSuppressExpensiveEffects();
            RebuildOverlayAdorners(includeAdorners, suppressExpensiveEffects);
        }

        private void QueueArrowPreviewOnlyRebuild()
        {
            if (!ShouldRenderInteractiveOverlayFrame())
            {
                return;
            }

            RebuildArrowPreviewOnly(includeAdorners: true);
        }

        private bool ShouldSuppressExpensiveEffects()
        {
            return _performanceModeEnabled &&
                   (_isResizingSelection || _dragLayer is not null || _isDraggingSelection || _isPreviewingArrow);
        }

        private bool ShouldRenderInteractiveOverlayFrame()
        {
            var now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastInteractiveOverlayTicks) < InteractiveOverlayThrottleMs)
            {
                return false;
            }

            Interlocked.Exchange(ref _lastInteractiveOverlayTicks, now);
            return true;
        }

        private bool LayerAffectsPixels(EditorLayer layer)
        {
            return layer is BlurLayer or HighlightLayer;
        }

        private async Task UpdateBaseImageAsync(long composeGeneration)
        {
            if (_workingPixels is null || _imageWidth <= 0 || _imageHeight <= 0)
            {
                return;
            }

            if (_baseBitmap is null || _baseBitmap.PixelWidth != _imageWidth || _baseBitmap.PixelHeight != _imageHeight)
            {
                _baseBitmap = new WriteableBitmap(_imageWidth, _imageHeight);
                BaseImage.Source = _baseBitmap;
            }

            await _composePipeline.UploadToWriteableBitmapAsync(_workingPixels, _baseBitmap, composeGeneration);
        }

        private async Task RecomposeAsync(bool includeAdorners = true, bool includePixelEffects = true)
        {
            if (_document is null || _originalPixels is null)
            {
                return;
            }

            if (_isBusy)
            {
                _recomposeQueued = true;
                _includeAdornersForQueuedRecompose |= includeAdorners;
                _includePixelEffectsForQueuedRecompose |= includePixelEffects;
                return;
            }

            _isBusy = true;
            try
            {
                var runIncludeAdorners = includeAdorners;
                var runIncludePixelEffects = includePixelEffects;
                do
                {
                    _recomposeQueued = false;
                    _composeStopwatch.Restart();
                    if (runIncludePixelEffects)
                    {
                        EnsureWorkingPixelsBuffer();
                        var composeGeneration = _composePipeline.BeginPixelCompose();
                        var composeOutcome = await _composePipeline.ComposePixelsAsync(
                            new PixelComposeRequest(
                                composeGeneration,
                                _originalPixels!,
                                _imageWidth,
                                _imageHeight,
                                _document.Layers,
                                _blurInvertMode,
                                _highlightDimPercent,
                                _highlightInvertMode,
                                _workingPixels!,
                                _gpuEffectsEnabled));

                        if (composeOutcome.IsStale)
                        {
                            continue;
                        }

                        if (composeOutcome.UsedCpuFallback && _gpuEffectsEnabled)
                        {
                            _gpuEffectsEnabled = false;
                            if (!_gpuEffectWarningShown)
                            {
                                _gpuEffectWarningShown = true;
                                var error = string.IsNullOrWhiteSpace(_composePipeline.LastGpuError)
                                    ? "unknown error"
                                    : _composePipeline.LastGpuError;
                                InAppToastService.Show($"GPU rendering failed ({error}). CPU effects are used for this editing session.", InAppToastSeverity.Warning);
                            }
                        }

                        await UpdateBaseImageAsync(composeGeneration);
                    }
                    else
                    {
                        if (_workingPixels is null)
                        {
                            EnsureWorkingPixelsBuffer();
                            var composeGeneration = _composePipeline.BeginPixelCompose();
                            await UpdateBaseImageAsync(composeGeneration);
                        }
                    }

                    RebuildOverlayVisuals(runIncludeAdorners);
                    _composeStopwatch.Stop();
                    _composeSampleCount++;
                    _composeSampleTotalMs += _composeStopwatch.ElapsedMilliseconds;
                    if (_composeSampleCount % 20 == 0)
                    {
                        var avgComposeMs = _composeSampleTotalMs / Math.Max(1, _composeSampleCount);
                        Debug.WriteLine($"[ImageEditor] avg recompose {avgComposeMs}ms over {_composeSampleCount} runs");
                    }

                    runIncludeAdorners = _includeAdornersForQueuedRecompose;
                    runIncludePixelEffects = _includePixelEffectsForQueuedRecompose;
                    _includeAdornersForQueuedRecompose = false;
                    _includePixelEffectsForQueuedRecompose = false;
                }
                while (_recomposeQueued);
            }
            finally
            {
                _isBusy = false;
            }
        }

        private void EnsureWorkingPixelsBuffer()
        {
            if (_originalPixels is null)
            {
                return;
            }

            if (_workingPixels is null || _workingPixels.Length != _originalPixels.Length)
            {
                _workingPixels = new byte[_originalPixels.Length];
            }

            Buffer.BlockCopy(_originalPixels, 0, _workingPixels, 0, _originalPixels.Length);
        }

        private void RebuildOverlayVisuals(bool includeAdorners)
        {
            if (_document is null)
            {
                return;
            }

            var suppressExpensiveEffects = ShouldSuppressExpensiveEffects();
            _vectorCanvasHost.Sync(
                _document.Layers,
                LayersCanvas,
                suppressExpensiveEffects,
                _forceVectorFullRebuild);
            _forceVectorFullRebuild = false;

            RebuildOverlayAdorners(includeAdorners, suppressExpensiveEffects);
        }

        private void RebuildArrowPreviewOnly(bool includeAdorners)
        {
            if (_document is null)
            {
                return;
            }

            RebuildOverlayAdorners(includeAdorners, ShouldSuppressExpensiveEffects());
        }

        private void RebuildOverlayAdorners(bool includeAdorners, bool suppressExpensiveEffects)
        {
            ClearOverlayExceptInlineEditor();
            DrawArrowPreview(suppressExpensiveEffects);

            if (includeAdorners && _selectedLayerId.HasValue)
            {
                var selected = TryGetLayerById(_selectedLayerId.Value);
                if (selected is not null && !selected.IsVisible)
                {
                    selected = null;
                }

                if (selected is not null)
                {
                    var bounds = selected.GetBounds();
                    var selectedCornerRadius = GetEffectiveCornerRadius(bounds, GetLayerCornerRadius(selected));
                    var selectedRect = new Microsoft.UI.Xaml.Shapes.Rectangle
                    {
                        Width = bounds.Width,
                        Height = bounds.Height,
                        Stroke = EditorXamlResourceCache.GetBrush(DefaultPrimaryColor),
                        StrokeThickness = 2,
                        StrokeDashArray = EditorXamlResourceCache.CreateSelectionStrokeDash(),
                        Fill = EditorXamlResourceCache.SelectionFill,
                        RadiusX = selectedCornerRadius,
                        RadiusY = selectedCornerRadius
                    };
                    Canvas.SetLeft(selectedRect, bounds.X);
                    Canvas.SetTop(selectedRect, bounds.Y);
                    OverlayCanvas.Children.Add(selectedRect);

                    if (selected is BorderLayer or BlurLayer or TextLayer or HighlightLayer)
                    {
                        DrawResizeHandles(bounds);
                    }
                    else if (selected is ArrowLayer arrowLayer)
                    {
                        DrawArrowEndpointHandles(arrowLayer);
                    }
                }
            }

            if (includeAdorners && _pendingCropRect.HasValue && !_pendingCropRect.Value.IsEmpty)
            {
                var crop = _pendingCropRect.Value;
                var cropCornerRadius = GetEffectiveCornerRadius(crop, _regionCornerRadius);
                var cropRect = new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Width = crop.Width,
                    Height = crop.Height,
                    Stroke = _isCropSelected
                        ? EditorXamlResourceCache.GetBrush(DefaultPrimaryColor)
                        : EditorXamlResourceCache.CropStrokeMuted,
                    Fill = EditorXamlResourceCache.CropFill,
                    StrokeThickness = _isCropSelected ? 2 : 1.5,
                    StrokeDashArray = EditorXamlResourceCache.CreateCropStrokeDash(),
                    RadiusX = cropCornerRadius,
                    RadiusY = cropCornerRadius
                };
                Canvas.SetLeft(cropRect, crop.X);
                Canvas.SetTop(cropRect, crop.Y);
                OverlayCanvas.Children.Add(cropRect);

                if (_isCropSelected)
                {
                    DrawResizeHandles(crop);
                }
            }

            if (includeAdorners && _selectionRectangle is not null && !OverlayCanvas.Children.Contains(_selectionRectangle))
            {
                OverlayCanvas.Children.Add(_selectionRectangle);
            }

            if (_inlineTextEditor is not null)
            {
                EnsureInlineTextEditorOnOverlay();
                DrawInlineTextPlacementChrome();
            }
        }

        private void ClearOverlayExceptInlineEditor()
        {
            for (var i = OverlayCanvas.Children.Count - 1; i >= 0; i--)
            {
                if (OverlayCanvas.Children[i] != _inlineTextEditor)
                {
                    OverlayCanvas.Children.RemoveAt(i);
                }
            }
        }

        private void EnsureInlineTextEditorOnOverlay()
        {
            if (_inlineTextEditor is not null && !OverlayCanvas.Children.Contains(_inlineTextEditor))
            {
                OverlayCanvas.Children.Add(_inlineTextEditor);
            }
        }

        private void RefreshInlineTextOverlayChrome()
        {
            ClearOverlayExceptInlineEditor();
            EnsureInlineTextEditorOnOverlay();
            DrawInlineTextPlacementChrome();
        }

        private void DrawInlineTextPlacementChrome()
        {
            if (!_inlineTextRegion.HasValue)
            {
                return;
            }

            var region = _inlineTextRegion.Value;
            var frame = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = region.Width,
                Height = region.Height,
                Stroke = EditorXamlResourceCache.GetBrush(DefaultPrimaryColor),
                StrokeThickness = 2,
                StrokeDashArray = EditorXamlResourceCache.CreateSelectionStrokeDash(),
                Fill = EditorXamlResourceCache.SelectionFill,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(frame, region.X);
            Canvas.SetTop(frame, region.Y);
            OverlayCanvas.Children.Add(frame);
            EnsureInlineTextEditorOnOverlay();
            DrawResizeHandles(region);
        }

        private void DrawArrowPreview(bool suppressExpensiveEffects)
        {
            if (!_isPreviewingArrow || _activeTool != EditorToolType.Arrow)
            {
                return;
            }

            var previewLayer = new ArrowLayer(
                _dragStartPoint.X,
                _dragStartPoint.Y,
                _arrowPreviewEndPoint.X,
                _arrowPreviewEndPoint.Y,
                1,
                DefaultPrimaryColor,
                ArrowFormStyle.Tapered);
            ApplyArrowStyleFromUi(previewLayer);

            foreach (var element in EditorVectorOverlayRenderer.CreateElementsForLayer(previewLayer, suppressExpensiveEffects))
            {
                OverlayCanvas.Children.Add(element);
            }
        }
    }
}
