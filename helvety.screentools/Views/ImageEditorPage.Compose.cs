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

        /// <summary>
        /// Requests a recompose. Returns a task that completes once this request (and anything queued behind it) has been
        /// applied, so export paths can await it and never read a half-composed <c>_workingPixels</c> buffer.
        /// </summary>
        private Task RecomposeAsync(bool includeAdorners = true, bool includePixelEffects = true)
        {
            if (_isTornDown || _document is null || _originalPixels is null)
            {
                return Task.CompletedTask;
            }

            if (_isBusy)
            {
                // The running loop picks this up before it completes.
                _recomposeQueued = true;
                _includeAdornersForQueuedRecompose |= includeAdorners;
                _includePixelEffectsForQueuedRecompose |= includePixelEffects;
                return _activeRecompose;
            }

            _activeRecompose = RunRecomposeLoopAsync(includeAdorners, includePixelEffects);
            return _activeRecompose;
        }

        private async Task RunRecomposeLoopAsync(bool includeAdorners, bool includePixelEffects)
        {
            _isBusy = true;
            try
            {
                var runIncludeAdorners = includeAdorners;
                var runIncludePixelEffects = includePixelEffects;
                do
                {
                    _recomposeQueued = false;
                    try
                    {
                        await RecomposeOnceAsync(runIncludeAdorners, runIncludePixelEffects);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("Editor recompose failed.", ex);
                        InAppToastService.Show($"Updating the image failed ({ex.Message}).", InAppToastSeverity.Error);
                    }

                    runIncludeAdorners = _includeAdornersForQueuedRecompose;
                    runIncludePixelEffects = _includePixelEffectsForQueuedRecompose;
                    _includeAdornersForQueuedRecompose = false;
                    _includePixelEffectsForQueuedRecompose = false;
                }
                while (_recomposeQueued && !_isTornDown);
            }
            finally
            {
                _isBusy = false;
            }
        }

        private async Task RecomposeOnceAsync(bool includeAdorners, bool includePixelEffects)
        {
            if (_isTornDown || _document is null || _originalPixels is null)
            {
                return;
            }

            _composeStopwatch.Restart();
            if (includePixelEffects)
            {
                EnsureWorkingPixelsBuffer();
                var composeGeneration = _composePipeline.BeginPixelCompose();
                var useGpu = _gpuEffectsEnabled && !_gpuFailedThisSession;
                var composeOutcome = await _composePipeline.ComposePixelsAsync(
                    new PixelComposeRequest(
                        composeGeneration,
                        _originalPixels,
                        _imageWidth,
                        _imageHeight,
                        // Snapshot on the UI thread: layers keep changing while the GPU renders on a worker thread.
                        PixelEffects.Capture(
                            _document.Layers,
                            _imageWidth,
                            _imageHeight,
                            _blurInvertMode,
                            _highlightDimPercent,
                            _highlightInvertMode),
                        _workingPixels!,
                        useGpu));

                if (_isTornDown)
                {
                    return;
                }

                if (!composeOutcome.IsStale)
                {
                    if (composeOutcome.UsedCpuFallback && !_gpuFailedThisSession)
                    {
                        // Session-only: never written to settings, so the next editor window tries the GPU again.
                        _gpuFailedThisSession = true;
                        var error = string.IsNullOrWhiteSpace(_composePipeline.LastGpuError)
                            ? "unknown error"
                            : _composePipeline.LastGpuError;
                        InAppToastService.Show($"GPU rendering failed ({error}). CPU effects are used for this editing session.", InAppToastSeverity.Warning);
                    }

                    await UpdateBaseImageAsync(composeGeneration);
                }
            }
            else if (_workingPixels is null)
            {
                EnsureWorkingPixelsBuffer();
                await UpdateBaseImageAsync(_composePipeline.BeginPixelCompose());
            }

            RebuildOverlayVisuals(includeAdorners);
            _composeStopwatch.Stop();
            _composeSampleCount++;
            _composeSampleTotalMs += _composeStopwatch.ElapsedMilliseconds;
            if (_composeSampleCount % 20 == 0)
            {
                var avgComposeMs = _composeSampleTotalMs / Math.Max(1, _composeSampleCount);
                Debug.WriteLine($"[ImageEditor] avg recompose {avgComposeMs}ms over {_composeSampleCount} runs");
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
            _workingPixelsVersion++;
        }

        private void RebuildOverlayVisuals(bool includeAdorners)
        {
            if (_document is null)
            {
                return;
            }

            RefreshMagnifierLoupeContents();

            var suppressExpensiveEffects = ShouldSuppressExpensiveEffects();
            _vectorCanvasHost.Sync(
                _document.Layers,
                LayersCanvas,
                suppressExpensiveEffects,
                _forceVectorFullRebuild);
            _forceVectorFullRebuild = false;

            RebuildOverlayAdorners(includeAdorners, suppressExpensiveEffects);
        }

        private void RefreshMagnifierLoupeContents()
        {
            if (_document is null || _workingPixels is null || _imageWidth <= 0 || _imageHeight <= 0)
            {
                return;
            }

            foreach (var layer in _document.Layers)
            {
                if (layer is MagnifierLayer magnifierLayer && magnifierLayer.IsVisible)
                {
                    MagnifierContentBuilder.UpdateLoupePixels(
                        magnifierLayer,
                        _workingPixels,
                        _imageWidth,
                        _imageHeight,
                        _workingPixelsVersion);
                }
            }
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
                    if (selected is MagnifierLayer magnifierLayer)
                    {
                        DrawMagnifierSelectionAdorners(magnifierLayer);
                    }
                    else
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

        private void DrawMagnifierSelectionAdorners(MagnifierLayer magnifierLayer)
        {
            DrawMagnifierPartOutline(magnifierLayer.SourceRegion, magnifierLayer.Shape);
            DrawMagnifierPartOutline(magnifierLayer.LoupeRegion, magnifierLayer.Shape);
            DrawResizeHandles(magnifierLayer.SourceRegion);
            DrawResizeHandles(magnifierLayer.LoupeRegion);
        }

        private void DrawMagnifierPartOutline(EditorRect region, MagnifierShape shape)
        {
            if (shape == MagnifierShape.Circle)
            {
                var ellipse = new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width = region.Width,
                    Height = region.Height,
                    Stroke = EditorXamlResourceCache.GetBrush(DefaultPrimaryColor),
                    StrokeThickness = 2,
                    StrokeDashArray = EditorXamlResourceCache.CreateSelectionStrokeDash(),
                    Fill = EditorXamlResourceCache.SelectionFill,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(ellipse, region.X);
                Canvas.SetTop(ellipse, region.Y);
                OverlayCanvas.Children.Add(ellipse);
                return;
            }

            var rect = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = region.Width,
                Height = region.Height,
                Stroke = EditorXamlResourceCache.GetBrush(DefaultPrimaryColor),
                StrokeThickness = 2,
                StrokeDashArray = EditorXamlResourceCache.CreateSelectionStrokeDash(),
                Fill = EditorXamlResourceCache.SelectionFill,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(rect, region.X);
            Canvas.SetTop(rect, region.Y);
            OverlayCanvas.Children.Add(rect);
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
