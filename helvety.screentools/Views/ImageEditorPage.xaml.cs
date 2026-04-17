using helvety.screentools.Editor;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Text;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using Ellipse = Microsoft.UI.Xaml.Shapes.Ellipse;
using Line = Microsoft.UI.Xaml.Shapes.Line;
using Polygon = Microsoft.UI.Xaml.Shapes.Polygon;
using Polyline = Microsoft.UI.Xaml.Shapes.Polyline;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using UiFontStyle = Windows.UI.Text.FontStyle;

namespace helvety.screentools.Views
{
    /// <summary>
    /// Layer-based image editor opened from the General home gallery, or after a screenshot: tools, layers, export, and editor settings.
    /// </summary>
    public sealed partial class ImageEditorPage : Page
    {
        private const string DefaultPrimaryColor = "#FFD81B60";
        private const string DefaultTextColor = DefaultPrimaryColor;
        private const string DefaultBorderColor = "#FFFFFFFF";
        private const string DefaultSharedShadowColor = "#66000000";
        private const int DefaultSharedShadowOffset = 2;
        private const string BlurOverlayColor = "#66A0A0A0";
        private const int ResizeHandleSize = 12;
        private const int MinResizableRegionSize = 8;
        private const int MinTextRegionWidth = 80;
        private const int MinTextRegionHeight = 40;
        private const int MaxHighlightDimPercent = 80;
        private const int DefaultRegionCornerRadius = 8;
        private const int MaxRegionCornerRadius = 24;
        private const int MaxPrimaryThickness = 24;
        private const int InteractiveOverlayThrottleMs = 8;
        private const double MoveZoomMinimum = 0.1d;
        private const double MoveZoomMaximum = 8d;
        private const double MoveZoomSliderStep = 0.05d;
        private const double MoveZoomButtonStep = 0.10d;

        private enum ResizeHandle
        {
            None = 0,
            NorthWest = 1,
            North = 2,
            NorthEast = 3,
            East = 4,
            SouthEast = 5,
            South = 6,
            SouthWest = 7,
            West = 8,
            ArrowStart = 9,
            ArrowEnd = 10
        }

        private readonly string _filePath;
        private EditorDocument? _document;
        private byte[]? _originalPixels;
        private byte[]? _workingPixels;
        private WriteableBitmap? _baseBitmap;
        private int _imageWidth;
        private int _imageHeight;
        private bool _isDraggingSelection;
        private Point _dragStartPoint;
        private bool _isPreviewingArrow;
        private Point _arrowPreviewEndPoint;
        private Rectangle? _selectionRectangle;
        private EditorRect? _pendingCropRect;
        private bool _isBusy;
        private EditorToolType _activeTool = EditorToolType.Move;
        private Guid? _selectedLayerId;
        private EditorLayer? _dragLayer;
        private Point _lastPointerPoint;
        private TextBox? _inlineTextEditor;
        private Guid? _editingTextLayerId;
        private Point _inlineTextPoint;
        private int _inlineTextWrapWidth = 260;
        private bool _isCommittingInlineText;
        private bool _isApplyingFitWidthZoom;
        private bool _isSyncingMoveZoomControls;
        private bool _hasAppliedInitialFitWidth;
        private bool _userAdjustedZoom;
        private double _lastAppliedFitWidthZoom = 1d;
        private bool _isCropSelected;
        private bool _isSyncingToolSettings;
        private bool _isInitializingUi = true;
        private bool _isSyncingRegionCornerRadius;
        private bool _isResizingSelection;
        private ResizeHandle _activeResizeHandle;
        private EditorRect _resizeStartBounds;
        private Guid? _resizeLayerId;
        private bool _resizeTargetIsCrop;
        private double _resizeStartTextFontSize;
        private EditorToolType _settingsTool = EditorToolType.Move;
        private int _highlightDimPercent = 35;
        private bool _blurInvertMode;
        private bool _highlightInvertMode;
        private int _regionCornerRadius = DefaultRegionCornerRadius;
        private bool _performanceModeEnabled;
        private bool _gpuEffectsEnabled = true;
        private bool _gpuEffectWarningShown;
        private bool _deferPixelRecomposeUntilPointerRelease;
        private bool _recomposeQueued;
        private bool _includeAdornersForQueuedRecompose;
        private bool _includePixelEffectsForQueuedRecompose;
        private long _lastInteractiveOverlayTicks;
        private readonly Dictionary<Guid, EditorLayer> _layersById = new();
        private readonly GpuImageEffectsRenderer _gpuImageEffectsRenderer = new();
        private readonly Stopwatch _composeStopwatch = new();
        private int _composeSampleCount;
        private long _composeSampleTotalMs;

        public ImageEditorPage(string filePath)
        {
            _filePath = filePath;
            InitializeComponent();
            UpdateExportButtonLabels();
            ConfigureMoveZoomControlBounds();
            SyncMoveZoomControlsFromScrollViewer();
            _isInitializingUi = false;
            Loaded += ImageEditorPage_Loaded;
            Unloaded += ImageEditorPage_Unloaded;
            EditorScrollViewer.SizeChanged += EditorScrollViewer_SizeChanged;
            EditorScrollViewer.ViewChanged += EditorScrollViewer_ViewChanged;
        }

        private void ImageEditorPage_Unloaded(object sender, RoutedEventArgs e)
        {
            CommitInlineTextEditor();
            if (_document is not null)
            {
                _document.Layers.CollectionChanged -= Layers_CollectionChanged;
            }

            Loaded -= ImageEditorPage_Loaded;
            Unloaded -= ImageEditorPage_Unloaded;
            EditorScrollViewer.SizeChanged -= EditorScrollViewer_SizeChanged;
            EditorScrollViewer.ViewChanged -= EditorScrollViewer_ViewChanged;
        }

        private async void ImageEditorPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadImageAsync();
        }

        private async Task LoadImageAsync()
        {
            try
            {
                if (!string.Equals(Path.GetExtension(_filePath), ".png", StringComparison.OrdinalIgnoreCase))
                {
                    InAppToastService.Show("Only PNG files are supported in the editor.", InAppToastSeverity.Warning);
                    return;
                }

                var file = await StorageFile.GetFileFromPathAsync(_filePath);
                using var stream = await file.OpenAsync(FileAccessMode.Read);
                var decoder = await BitmapDecoder.CreateAsync(stream);
                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    new BitmapTransform(),
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                _imageWidth = (int)decoder.PixelWidth;
                _imageHeight = (int)decoder.PixelHeight;
                _originalPixels = pixelData.DetachPixelData();
                _workingPixels = (byte[])_originalPixels.Clone();
                _document = new EditorDocument(_filePath, _imageWidth, _imageHeight);
                _document.Layers.CollectionChanged += Layers_CollectionChanged;
                RebuildLayerIndex();
                _hasAppliedInitialFitWidth = false;
                _userAdjustedZoom = false;
                _lastAppliedFitWidthZoom = 1d;
                _isCropSelected = false;
                _isResizingSelection = false;
                _activeResizeHandle = ResizeHandle.None;
                _resizeLayerId = null;
                _resizeTargetIsCrop = false;

                LayersListView.ItemsSource = _document.Layers;
                EditorSurfaceGrid.Width = _imageWidth;
                EditorSurfaceGrid.Height = _imageHeight;
                _baseBitmap = null;
                OverlayCanvas.Width = _imageWidth;
                OverlayCanvas.Height = _imageHeight;
                LayersCanvas.Width = _imageWidth;
                LayersCanvas.Height = _imageHeight;
                LayersCanvas.CacheMode = new BitmapCache();
                OverlayCanvas.CacheMode = new BitmapCache();
                _composeSampleCount = 0;
                _composeSampleTotalMs = 0;
                ApplyPersistedEditorUiSettings();
                SetActiveTool(EditorToolType.Move);
                UpdateSelectedTextEditorVisibility();
                await UpdateBaseImageAsync();
                ApplyFitWidthZoom();
            }
            catch (Exception ex)
            {
                InAppToastService.Show($"Could not open image ({ex.Message}).", InAppToastSeverity.Error);
            }
        }

        private void EditorScrollViewer_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (_userAdjustedZoom && _hasAppliedInitialFitWidth)
            {
                return;
            }

            ApplyFitWidthZoom();
        }

        private void EditorScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            SyncMoveZoomControlsFromScrollViewer();

            if (_isApplyingFitWidthZoom || !_hasAppliedInitialFitWidth)
            {
                return;
            }

            var currentZoom = EditorScrollViewer.ZoomFactor;
            if (Math.Abs(currentZoom - _lastAppliedFitWidthZoom) > 0.01d)
            {
                _userAdjustedZoom = true;
            }
        }

        private void ApplyFitWidthZoom()
        {
            if (_imageWidth <= 0)
            {
                return;
            }

            var viewportWidth = EditorScrollViewer.ViewportWidth;
            if (viewportWidth <= 0)
            {
                viewportWidth = EditorScrollViewer.ActualWidth;
            }

            if (viewportWidth <= 0)
            {
                return;
            }

            var targetZoom = viewportWidth / _imageWidth;
            var (minZoom, maxZoom) = GetEffectiveZoomRange();
            var clampedZoom = Math.Clamp(targetZoom, minZoom, maxZoom);

            _isApplyingFitWidthZoom = true;
            try
            {
                EditorScrollViewer.ChangeView(horizontalOffset: null, verticalOffset: 0, zoomFactor: (float)clampedZoom, disableAnimation: true);
                _lastAppliedFitWidthZoom = clampedZoom;
                _hasAppliedInitialFitWidth = true;
            }
            finally
            {
                _isApplyingFitWidthZoom = false;
                SyncMoveZoomControlsFromScrollViewer();
            }
        }

        private (double MinZoom, double MaxZoom) GetEffectiveZoomRange()
        {
            var minZoomFromViewer = EditorScrollViewer.MinZoomFactor > 0
                ? (double)EditorScrollViewer.MinZoomFactor
                : MoveZoomMinimum;
            var maxZoomFromViewer = EditorScrollViewer.MaxZoomFactor > minZoomFromViewer
                ? (double)EditorScrollViewer.MaxZoomFactor
                : MoveZoomMaximum;
            var effectiveMin = Math.Clamp(MoveZoomMinimum, minZoomFromViewer, maxZoomFromViewer);
            var effectiveMax = Math.Clamp(MoveZoomMaximum, effectiveMin, maxZoomFromViewer);
            return (effectiveMin, effectiveMax);
        }

        private void ConfigureMoveZoomControlBounds()
        {
            var (minZoom, maxZoom) = GetEffectiveZoomRange();
            MoveZoomSlider.Minimum = minZoom;
            MoveZoomSlider.Maximum = maxZoom;
            MoveZoomSlider.StepFrequency = MoveZoomSliderStep;
        }

        private void SyncMoveZoomControlsFromScrollViewer()
        {
            ConfigureMoveZoomControlBounds();
            var (minZoom, maxZoom) = GetEffectiveZoomRange();
            var currentZoom = Math.Clamp(
                EditorScrollViewer.ZoomFactor > 0 ? (double)EditorScrollViewer.ZoomFactor : 1d,
                minZoom,
                maxZoom);

            _isSyncingMoveZoomControls = true;
            try
            {
                if (Math.Abs(MoveZoomSlider.Value - currentZoom) > 0.0001d)
                {
                    MoveZoomSlider.Value = currentZoom;
                }

                MoveZoomValueText.Text = $"{Math.Round(currentZoom * 100d):0}%";
            }
            finally
            {
                _isSyncingMoveZoomControls = false;
            }
        }

        private void ApplyEditorZoom(double requestedZoom, bool markAsUserAdjusted)
        {
            var (minZoom, maxZoom) = GetEffectiveZoomRange();
            var clampedZoom = Math.Clamp(requestedZoom, minZoom, maxZoom);
            var currentZoom = EditorScrollViewer.ZoomFactor > 0 ? (double)EditorScrollViewer.ZoomFactor : 1d;
            if (Math.Abs(currentZoom - clampedZoom) < 0.0001d)
            {
                SyncMoveZoomControlsFromScrollViewer();
                return;
            }

            EditorScrollViewer.ChangeView(horizontalOffset: null, verticalOffset: null, zoomFactor: (float)clampedZoom, disableAnimation: true);
            if (markAsUserAdjusted && _hasAppliedInitialFitWidth)
            {
                _userAdjustedZoom = true;
            }

            SyncMoveZoomControlsFromScrollViewer();
        }

        private void MoveZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            var currentZoom = EditorScrollViewer.ZoomFactor > 0 ? (double)EditorScrollViewer.ZoomFactor : 1d;
            ApplyEditorZoom(currentZoom - MoveZoomButtonStep, markAsUserAdjusted: true);
        }

        private void MoveZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            var currentZoom = EditorScrollViewer.ZoomFactor > 0 ? (double)EditorScrollViewer.ZoomFactor : 1d;
            ApplyEditorZoom(currentZoom + MoveZoomButtonStep, markAsUserAdjusted: true);
        }

        private void MoveZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isInitializingUi || _isSyncingMoveZoomControls)
            {
                return;
            }

            ApplyEditorZoom(e.NewValue, markAsUserAdjusted: true);
        }

        private void Layers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildLayerIndex();
            QueueRecompose(includeAdorners: true, includePixelEffects: true);
        }

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

            var suppressExpensiveEffects = _performanceModeEnabled && (_isResizingSelection || _dragLayer is not null || _isDraggingSelection);
            RebuildOverlayAdorners(includeAdorners, suppressExpensiveEffects);
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

        private async void ToolButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.Tag is not string tag ||
                !Enum.TryParse<EditorToolType>(tag, true, out var tool))
            {
                return;
            }

            if (tool != EditorToolType.Text)
            {
                CommitInlineTextEditor();
            }

            SetActiveTool(tool);
            await RecomposeAsync(includeAdorners: true, includePixelEffects: false);
        }

        private async Task UpdateBaseImageAsync()
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

            using (var stream = _baseBitmap.PixelBuffer.AsStream())
            {
                stream.Seek(0, SeekOrigin.Begin);
                await stream.WriteAsync(_workingPixels, 0, _workingPixels.Length);
            }

            _baseBitmap.Invalidate();
        }

        private void SetActiveTool(EditorToolType tool)
        {
            _activeTool = tool;
            UpdateDisplayedToolContext();
            UpdateToolButtonVisuals();
            UpdateSelectedTextEditorVisibility();
        }

        private void UpdateDisplayedToolContext()
        {
            _settingsTool = ResolveSettingsTool();
            MoveToolSettingsPanel.Visibility = _settingsTool == EditorToolType.Move ? Visibility.Visible : Visibility.Collapsed;
            TextToolSettingsPanel.Visibility = _settingsTool == EditorToolType.Text ? Visibility.Visible : Visibility.Collapsed;
            BorderToolSettingsPanel.Visibility = _settingsTool == EditorToolType.Border ? Visibility.Visible : Visibility.Collapsed;
            BlurToolSettingsPanel.Visibility = _settingsTool == EditorToolType.Blur ? Visibility.Visible : Visibility.Collapsed;
            HighlightToolSettingsPanel.Visibility = _settingsTool == EditorToolType.Highlight ? Visibility.Visible : Visibility.Collapsed;
            ArrowToolSettingsPanel.Visibility = _settingsTool == EditorToolType.Arrow ? Visibility.Visible : Visibility.Collapsed;
            CropToolSettingsPanel.Visibility = _settingsTool == EditorToolType.Crop ? Visibility.Visible : Visibility.Collapsed;
            UpdateExportButtonLabels();
            UpdateArrowShadowToggleState(forceOffWhenUnsupported: true);
        }

        private void UpdateExportButtonLabels()
        {
            var cropIsActive = _pendingCropRect.HasValue && !_pendingCropRect.Value.IsEmpty;
            SaveCopyAndCloseButton.Content = cropIsActive ? "Save crop, copy and close" : "Save, copy and close";
            SaveAndCloseButton.Content = cropIsActive ? "Save crop and close" : "Save and close";
        }

        private EditorToolType ResolveSettingsTool()
        {
            if (_activeTool != EditorToolType.Move)
            {
                return _activeTool;
            }

            if (_isCropSelected && _pendingCropRect.HasValue && !_pendingCropRect.Value.IsEmpty)
            {
                return EditorToolType.Crop;
            }

            if (!TryGetSelectedLayer(out var selectedLayer) || selectedLayer is null)
            {
                return EditorToolType.Move;
            }

            return selectedLayer.LayerType switch
            {
                EditorLayerType.Text => EditorToolType.Text,
                EditorLayerType.Border => EditorToolType.Border,
                EditorLayerType.Blur => EditorToolType.Blur,
                EditorLayerType.Arrow => EditorToolType.Arrow,
                EditorLayerType.Highlight => EditorToolType.Highlight,
                _ => EditorToolType.Move
            };
        }

        private bool TryGetSelectedLayer(out EditorLayer? layer)
        {
            layer = null;
            if (_document is null || !_selectedLayerId.HasValue)
            {
                return false;
            }

            layer = TryGetLayerById(_selectedLayerId.Value);
            return layer is not null;
        }

        private EditorLayer? TryGetLayerById(Guid id)
        {
            return _layersById.TryGetValue(id, out var layer) ? layer : null;
        }

        private void RebuildLayerIndex()
        {
            _layersById.Clear();
            if (_document is null)
            {
                return;
            }

            foreach (var layer in _document.Layers)
            {
                _layersById[layer.Id] = layer;
            }
        }

        private void UpdateToolButtonVisuals()
        {
            var selectedBrush = new SolidColorBrush(ColorHelper.FromArgb(70, 216, 27, 96));
            var normalBrush = Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var normal)
                && normal is Brush normalThemeBrush
                ? normalThemeBrush
                : new SolidColorBrush(ColorHelper.FromArgb(255, 40, 40, 40));

            var highlightedTool = _activeTool == EditorToolType.Move ? _settingsTool : _activeTool;
            MoveToolButton.Background = highlightedTool == EditorToolType.Move ? selectedBrush : normalBrush;
            TextToolButton.Background = highlightedTool == EditorToolType.Text ? selectedBrush : normalBrush;
            BorderToolButton.Background = highlightedTool == EditorToolType.Border ? selectedBrush : normalBrush;
            BlurToolButton.Background = highlightedTool == EditorToolType.Blur ? selectedBrush : normalBrush;
            HighlightToolButton.Background = highlightedTool == EditorToolType.Highlight ? selectedBrush : normalBrush;
            ArrowToolButton.Background = highlightedTool == EditorToolType.Arrow ? selectedBrush : normalBrush;
            CropToolButton.Background = highlightedTool == EditorToolType.Crop ? selectedBrush : normalBrush;
        }

        private void OverlayCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_document is null)
            {
                return;
            }

            var point = e.GetCurrentPoint(OverlayCanvas).Position;
            if (!IsPointInImage(point))
            {
                return;
            }

            if (_inlineTextEditor is not null)
            {
                if (IsPointOutsideInlineEditor(point))
                {
                    CommitInlineTextEditor();
                }
                else
                {
                    return;
                }
            }

            if (_activeTool == EditorToolType.Text)
            {
                BeginSelectionDrag(point);
                return;
            }

            if (_activeTool == EditorToolType.Move)
            {
                if (TryBeginResize(point, e))
                {
                    return;
                }

                var hasSelectedLayer = SelectTopMostLayerAt(point);
                if (_selectedLayerId.HasValue)
                {
                    _dragLayer = TryGetLayerById(_selectedLayerId.Value);
                    _lastPointerPoint = point;
                }
                else if (!hasSelectedLayer && TrySelectCropAt(point))
                {
                    _dragLayer = null;
                }

                QueueOverlayOnlyRebuild(includeAdorners: true);
                return;
            }

            BeginSelectionDrag(point);
        }

        private void OverlayCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (_document is null || _inlineTextEditor is not null)
            {
                return;
            }

            var point = e.GetPosition(OverlayCanvas);
            if (!IsPointInImage(point))
            {
                return;
            }

            if (!SelectTopMostLayerAt(point))
            {
                return;
            }

            if (!TryGetSelectedLayer(out var layer) || layer is not TextLayer textLayer)
            {
                return;
            }

            BeginInlineTextEntryForLayer(textLayer);
            e.Handled = true;
        }

        private void OverlayCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_activeTool == EditorToolType.Move && _isResizingSelection)
            {
                var currentPoint = e.GetCurrentPoint(OverlayCanvas).Position;
                ApplyResize(currentPoint);
                if (_resizeTargetIsCrop || _document is null || !_resizeLayerId.HasValue)
                {
                    QueueOverlayOnlyRebuild(includeAdorners: true);
                    return;
                }

                var resizeLayer = TryGetLayerById(_resizeLayerId.Value);
                if (resizeLayer is not null && LayerAffectsPixels(resizeLayer))
                {
                    _deferPixelRecomposeUntilPointerRelease = true;
                    QueueOverlayOnlyRebuild(includeAdorners: true);
                }
                else
                {
                    QueueRecompose(includeAdorners: true, includePixelEffects: false);
                }

                return;
            }

            if (_activeTool == EditorToolType.Move && _dragLayer is not null)
            {
                var currentPoint = e.GetCurrentPoint(OverlayCanvas).Position;
                var dx = currentPoint.X - _lastPointerPoint.X;
                var dy = currentPoint.Y - _lastPointerPoint.Y;
                _dragLayer.MoveBy(dx, dy, _imageWidth, _imageHeight);
                _lastPointerPoint = currentPoint;
                if (LayerAffectsPixels(_dragLayer))
                {
                    _deferPixelRecomposeUntilPointerRelease = true;
                    QueueOverlayOnlyRebuild(includeAdorners: true);
                }
                else
                {
                    QueueRecompose(includeAdorners: true, includePixelEffects: false);
                }

                return;
            }

            if (!_isDraggingSelection)
            {
                return;
            }

            var point = e.GetCurrentPoint(OverlayCanvas).Position;
            if (_activeTool == EditorToolType.Arrow && _isPreviewingArrow)
            {
                _arrowPreviewEndPoint = ClampPointToImage(point);
                RebuildOverlayVisuals(includeAdorners: true);
                return;
            }

            UpdateSelectionRectangle(point);
        }

        private async void OverlayCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isResizingSelection)
            {
                _isResizingSelection = false;
                _activeResizeHandle = ResizeHandle.None;
                _resizeLayerId = null;
                _resizeTargetIsCrop = false;
                _resizeStartTextFontSize = 0;
                OverlayCanvas.ReleasePointerCaptures();
                if (_deferPixelRecomposeUntilPointerRelease)
                {
                    _deferPixelRecomposeUntilPointerRelease = false;
                    await RecomposeAsync(includeAdorners: true, includePixelEffects: true);
                }
                else
                {
                    RebuildOverlayVisuals(includeAdorners: true);
                }

                return;
            }

            if (!_isDraggingSelection || _document is null)
            {
                if (_deferPixelRecomposeUntilPointerRelease)
                {
                    _deferPixelRecomposeUntilPointerRelease = false;
                    await RecomposeAsync(includeAdorners: true, includePixelEffects: true);
                }

                _dragLayer = null;
                return;
            }

            var endPoint = e.GetCurrentPoint(OverlayCanvas).Position;
            if (_activeTool == EditorToolType.Arrow)
            {
                _isDraggingSelection = false;
                _isPreviewingArrow = false;
                RemoveSelectionRectangle();
                RebuildOverlayVisuals(includeAdorners: true);
                if (AddArrowLayer(endPoint, _dragStartPoint))
                {
                    await FinalizeRegionPlacementAsync(clearCropSelection: true);
                }

                _dragLayer = null;
                return;
            }

            var maybeRegion = CompleteSelectionDrag(endPoint);
            if (maybeRegion is null)
            {
                _dragLayer = null;
                return;
            }

            await HandleCompletedRegionSelectionAsync(maybeRegion.Value, endPoint);

            if (_deferPixelRecomposeUntilPointerRelease)
            {
                _deferPixelRecomposeUntilPointerRelease = false;
                await RecomposeAsync(includeAdorners: true, includePixelEffects: true);
            }

            _dragLayer = null;
        }

        private void BeginSelectionDrag(Point point)
        {
            _isDraggingSelection = true;
            _dragStartPoint = point;
            if (_activeTool == EditorToolType.Arrow)
            {
                _isPreviewingArrow = true;
                _arrowPreviewEndPoint = point;
                RemoveSelectionRectangle();
                RebuildOverlayVisuals(includeAdorners: true);
                return;
            }

            _isPreviewingArrow = false;
            EnsureSelectionRectangle();
            UpdateSelectionRectangle(point);
        }

        private EditorRect? CompleteSelectionDrag(Point endPoint)
        {
            _isDraggingSelection = false;
            _isPreviewingArrow = false;
            var region = BuildRegion(_dragStartPoint, endPoint);
            RemoveSelectionRectangle();
            return region.IsEmpty ? null : region;
        }

        private async Task HandleCompletedRegionSelectionAsync(EditorRect region, Point endPoint)
        {
            switch (_activeTool)
            {
                case EditorToolType.Text:
                    BeginInlineTextEntry(region);
                    return;
                case EditorToolType.Border:
                    AddBorderLayer(region);
                    await FinalizeRegionPlacementAsync(clearCropSelection: true);
                    return;
                case EditorToolType.Blur:
                    AddBlurLayer(region);
                    await FinalizeRegionPlacementAsync(clearCropSelection: true);
                    return;
                case EditorToolType.Highlight:
                    AddHighlightLayer(region);
                    await FinalizeRegionPlacementAsync(clearCropSelection: true);
                    return;
                case EditorToolType.Arrow:
                    if (AddArrowLayer(_dragStartPoint, endPoint))
                    {
                        await FinalizeRegionPlacementAsync(clearCropSelection: true);
                    }
                    return;
                case EditorToolType.Crop:
                    SelectCropRegion(region);
                    await FinalizeRegionPlacementAsync(clearCropSelection: false);
                    return;
                default:
                    return;
            }
        }

        private async Task FinalizeRegionPlacementAsync(bool clearCropSelection)
        {
            if (clearCropSelection)
            {
                ClearCropSelection();
            }

            SetActiveTool(EditorToolType.Move);
            await RecomposeAsync(includeAdorners: true, includePixelEffects: true);
        }

        private void BeginInlineTextEntry(EditorRect region)
        {
            if (_document is null)
            {
                return;
            }

            var left = Clamp(region.X, 0, _imageWidth);
            var top = Clamp(region.Y, 0, _imageHeight);
            var wrapWidth = Clamp(region.Width, MinTextRegionWidth, Math.Max(MinTextRegionWidth, _imageWidth - left));
            var editorHeight = Clamp(region.Height, MinTextRegionHeight, Math.Max(MinTextRegionHeight, _imageHeight - top));
            _editingTextLayerId = null;
            _inlineTextPoint = new Point(left, top);
            _inlineTextWrapWidth = wrapWidth;
            _inlineTextEditor = new TextBox
            {
                Width = wrapWidth,
                MinHeight = MinTextRegionHeight,
                Height = editorHeight,
                PlaceholderText = "Type text and click outside to finish",
                FontSize = Clamp((int)Math.Round(TextSizeNumberBox.Value), 8, 180),
                FontFamily = new FontFamily(GetSelectedFont()),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap
            };
            ApplyTextStyleToEditor(_inlineTextEditor, TextBoldToggle.IsOn, TextItalicToggle.IsOn);
            _inlineTextEditor.KeyDown += InlineTextEditor_KeyDown;
            _inlineTextEditor.LostFocus += InlineTextEditor_LostFocus;
            Canvas.SetLeft(_inlineTextEditor, left);
            Canvas.SetTop(_inlineTextEditor, top);
            OverlayCanvas.Children.Add(_inlineTextEditor);
            _inlineTextEditor.Focus(FocusState.Programmatic);
        }

        private void BeginInlineTextEntryForLayer(TextLayer textLayer)
        {
            if (_document is null || _inlineTextEditor is not null)
            {
                return;
            }

            _editingTextLayerId = textLayer.Id;
            _inlineTextPoint = new Point(textLayer.X, textLayer.Y);
            _inlineTextWrapWidth = Math.Max(MinTextRegionWidth, textLayer.WrapWidth);
            var bounds = textLayer.GetBounds();
            var editorHeight = Math.Max(MinTextRegionHeight, bounds.Height + 12);
            _inlineTextEditor = new TextBox
            {
                Width = _inlineTextWrapWidth,
                MinHeight = MinTextRegionHeight,
                Height = editorHeight,
                FontSize = textLayer.FontSize,
                FontFamily = new FontFamily(GetFontName(textLayer.FontFamily)),
                Text = textLayer.Text,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap
            };
            ApplyTextStyleToEditor(_inlineTextEditor, textLayer.IsBold, textLayer.IsItalic);
            _inlineTextEditor.KeyDown += InlineTextEditor_KeyDown;
            _inlineTextEditor.LostFocus += InlineTextEditor_LostFocus;
            Canvas.SetLeft(_inlineTextEditor, _inlineTextPoint.X);
            Canvas.SetTop(_inlineTextEditor, _inlineTextPoint.Y);
            OverlayCanvas.Children.Add(_inlineTextEditor);
            _inlineTextEditor.Focus(FocusState.Programmatic);
            _inlineTextEditor.SelectAll();
        }

        private void AddBorderLayer(EditorRect region)
        {
            if (_document is null)
            {
                return;
            }

            var layer = new BorderLayer(region, 1, DefaultPrimaryColor);
            ApplyBorderStyleFromUi(layer);
            _document.Layers.Insert(0, layer);
            SelectLayer(layer);
        }

        private void AddBlurLayer(EditorRect region)
        {
            if (_document is null)
            {
                return;
            }

            var radius = Clamp((int)Math.Round(BlurRadiusNumberBox.Value), 1, 25);
            var layer = new BlurLayer(region, radius)
            {
                Feather = Clamp((int)Math.Round(BlurFeatherSlider.Value), 0, 40),
                CornerRadius = _regionCornerRadius
            };
            _document.Layers.Insert(0, layer);
            SelectLayer(layer);
        }

        private bool AddArrowLayer(Point start, Point end)
        {
            if (_document is null)
            {
                return false;
            }

            var clampedStart = ClampPointToImage(start);
            var clampedEnd = ClampPointToImage(end);
            var deltaX = clampedEnd.X - clampedStart.X;
            var deltaY = clampedEnd.Y - clampedStart.Y;
            var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
            if (length < 4)
            {
                return false;
            }

            var layer = new ArrowLayer(
                clampedStart.X,
                clampedStart.Y,
                clampedEnd.X,
                clampedEnd.Y,
                1,
                DefaultPrimaryColor,
                ArrowFormStyle.Tapered);
            ApplyArrowStyleFromUi(layer);
            _document.Layers.Insert(0, layer);
            SelectLayer(layer);
            return true;
        }

        private void AddHighlightLayer(EditorRect region)
        {
            if (_document is null)
            {
                return;
            }

            var layer = new HighlightLayer(region)
            {
                CornerRadius = _regionCornerRadius
            };
            _document.Layers.Insert(0, layer);
            SelectLayer(layer);
        }

        private Point ClampPointToImage(Point point)
        {
            var x = Math.Clamp(point.X, 0, _imageWidth);
            var y = Math.Clamp(point.Y, 0, _imageHeight);
            return new Point(x, y);
        }

        private void SelectLayer(EditorLayer layer)
        {
            _selectedLayerId = layer.Id;
            _isCropSelected = false;
            LayersListView.SelectedItem = layer;
            SyncToolSettingsFromSelectedLayer();
            UpdateDisplayedToolContext();
            UpdateToolButtonVisuals();
            UpdateSelectedTextEditorVisibility();
        }

        private void ClearCropSelection()
        {
            _pendingCropRect = null;
            _isCropSelected = false;
            UpdateDisplayedToolContext();
            UpdateToolButtonVisuals();
            UpdateSelectedTextEditorVisibility();
        }

        private void SelectCropRegion(EditorRect region)
        {
            _pendingCropRect = region;
            _selectedLayerId = null;
            _isCropSelected = true;
            LayersListView.SelectedItem = null;
            UpdateDisplayedToolContext();
            UpdateToolButtonVisuals();
            UpdateSelectedTextEditorVisibility();
        }

        private async void RemoveLayerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_document is null ||
                sender is not Button button ||
                button.Tag is not Guid id)
            {
                return;
            }

            var layer = TryGetLayerById(id);
            if (layer is null)
            {
                return;
            }

            _document.Layers.Remove(layer);
            if (_selectedLayerId == layer.Id)
            {
                _selectedLayerId = null;
            }

            UpdateSelectedTextEditorVisibility();
            await RecomposeAsync(includeAdorners: true, includePixelEffects: LayerAffectsPixels(layer));
        }

        private async void LayerVisibilityCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_document is null)
            {
                return;
            }

            Guid id;
            bool isVisible;
            if (sender is CheckBox checkBox && checkBox.Tag is Guid checkBoxId)
            {
                id = checkBoxId;
                isVisible = checkBox.IsChecked ?? false;
            }
            else if (sender is ToggleButton toggleButton && toggleButton.Tag is Guid toggleId)
            {
                id = toggleId;
                isVisible = toggleButton.IsChecked ?? false;
            }
            else
            {
                return;
            }

            var layer = TryGetLayerById(id);
            if (layer is null)
            {
                return;
            }

            layer.IsVisible = isVisible;
            await RecomposeAsync(includeAdorners: true, includePixelEffects: LayerAffectsPixels(layer));
        }

        private async void LayersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LayersListView.SelectedItem is not EditorLayer layer)
            {
                _selectedLayerId = null;
                _isCropSelected = false;
                UpdateDisplayedToolContext();
                UpdateToolButtonVisuals();
                UpdateSelectedTextEditorVisibility();
                await RecomposeAsync(includeAdorners: true, includePixelEffects: false);
                return;
            }

            _selectedLayerId = layer.Id;
            _isCropSelected = false;
            SyncToolSettingsFromSelectedLayer();
            UpdateDisplayedToolContext();
            UpdateToolButtonVisuals();
            UpdateSelectedTextEditorVisibility();
            await RecomposeAsync(includeAdorners: true, includePixelEffects: false);
        }

        private async void LayersListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            await RecomposeAsync(includeAdorners: true, includePixelEffects: true);
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
                        var gpuRendered = false;
                        if (_gpuEffectsEnabled)
                        {
                            gpuRendered = await _gpuImageEffectsRenderer.TryRenderAsync(
                                _originalPixels!,
                                _imageWidth,
                                _imageHeight,
                                _document.Layers,
                                _blurInvertMode,
                                _highlightDimPercent,
                                _highlightInvertMode,
                                _workingPixels!);

                            if (!gpuRendered)
                            {
                                _gpuEffectsEnabled = false;
                                if (!_gpuEffectWarningShown)
                                {
                                    _gpuEffectWarningShown = true;
                                    var error = string.IsNullOrWhiteSpace(_gpuImageEffectsRenderer.LastError)
                                        ? "unknown error"
                                        : _gpuImageEffectsRenderer.LastError;
                                    InAppToastService.Show($"GPU rendering failed ({error}). GPU effects are disabled for this editing session.", InAppToastSeverity.Warning);
                                }
                            }
                        }

                        if (!gpuRendered)
                        {
                            EnsureWorkingPixelsBuffer();
                        }

                        await UpdateBaseImageAsync();
                    }
                    else
                    {
                        if (_workingPixels is null)
                        {
                            EnsureWorkingPixelsBuffer();
                            await UpdateBaseImageAsync();
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

            System.Buffer.BlockCopy(_originalPixels, 0, _workingPixels, 0, _originalPixels.Length);
        }

        private void RebuildOverlayVisuals(bool includeAdorners)
        {
            if (_document is null)
            {
                return;
            }

            var suppressExpensiveEffects = _performanceModeEnabled && (_isResizingSelection || _dragLayer is not null || _isDraggingSelection);
            EditorVectorOverlayRenderer.DrawVisibleVectorLayers(_document.Layers, LayersCanvas, suppressExpensiveEffects);

            RebuildOverlayAdorners(includeAdorners, suppressExpensiveEffects);
        }

        private void RebuildOverlayAdorners(bool includeAdorners, bool suppressExpensiveEffects)
        {
            OverlayCanvas.Children.Clear();
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
                    var selectedRect = new Rectangle
                    {
                        Width = bounds.Width,
                        Height = bounds.Height,
                        Stroke = new SolidColorBrush(ParseColor(DefaultPrimaryColor)),
                        StrokeThickness = 2,
                        StrokeDashArray = new DoubleCollection { 4, 2 },
                        Fill = new SolidColorBrush(ColorHelper.FromArgb(35, 216, 27, 96)),
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
                var cropRect = new Rectangle
                {
                    Width = crop.Width,
                    Height = crop.Height,
                    Stroke = _isCropSelected
                        ? new SolidColorBrush(ParseColor(DefaultPrimaryColor))
                        : new SolidColorBrush(ColorHelper.FromArgb(220, 216, 27, 96)),
                    Fill = new SolidColorBrush(ColorHelper.FromArgb(48, 216, 27, 96)),
                    StrokeThickness = _isCropSelected ? 2 : 1.5,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
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
                OverlayCanvas.Children.Add(_inlineTextEditor);
            }
        }

        private void DrawArrowPreview(bool suppressExpensiveEffects)
        {
            if (!_isPreviewingArrow || _activeTool != EditorToolType.Arrow)
            {
                return;
            }

            var previewLayer = new ArrowLayer(
                _arrowPreviewEndPoint.X,
                _arrowPreviewEndPoint.Y,
                _dragStartPoint.X,
                _dragStartPoint.Y,
                1,
                DefaultPrimaryColor,
                ArrowFormStyle.Tapered);
            ApplyArrowStyleFromUi(previewLayer);

            EditorVectorOverlayRenderer.DrawVectorLayersBottomToTop(
                new[] { previewLayer },
                OverlayCanvas,
                suppressExpensiveEffects);
        }

        private async void SaveCopyAndCloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteSaveAndCloseAsync(copyToClipboard: true);
            }
            catch (Exception ex)
            {
                InAppToastService.Show($"Save failed ({ex.Message}).", InAppToastSeverity.Error);
            }
        }

        private async void SaveAndCloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteSaveAndCloseAsync(copyToClipboard: false);
            }
            catch (Exception ex)
            {
                InAppToastService.Show($"Save failed ({ex.Message}).", InAppToastSeverity.Error);
            }
        }

        private async Task ExecuteSaveAndCloseAsync(bool copyToClipboard)
        {
            CommitInlineTextEditor();
            await RecomposeAsync(includeAdorners: true, includePixelEffects: true);

            if (_pendingCropRect.HasValue && !_pendingCropRect.Value.IsEmpty)
            {
                var region = _pendingCropRect.Value;
                var fullPixels = await RenderCompositePixelsAsync();
                var cropPixels = CropPixels(fullPixels, _imageWidth, _imageHeight, region);
                var outputPath = BuildOutputPath(_filePath, "_crop");
                await SaveNewImageAndCloseAsync(outputPath, cropPixels, region.Width, region.Height, "Cropped image saved", copyToClipboard);
                return;
            }

            var pixels = await RenderCompositePixelsAsync();
            var fullOutputPath = BuildOutputPath(_filePath, "_edited");
            await SaveNewImageAndCloseAsync(fullOutputPath, pixels, _imageWidth, _imageHeight, "Saved", copyToClipboard);
        }

        /// <summary>
        /// Captures the full editor surface as pixels. All exports are flattened so they can be viewed everywhere without editor state.
        /// </summary>
        private async Task<byte[]> RenderCompositePixelsAsync()
        {
            if (_document is not null &&
                _workingPixels is not null &&
                !_document.Layers.Any(layer => layer.IsVisible && layer is TextLayer or BorderLayer or ArrowLayer))
            {
                var pixels = new byte[_workingPixels.Length];
                System.Buffer.BlockCopy(_workingPixels, 0, pixels, 0, _workingPixels.Length);
                return pixels;
            }

            RebuildOverlayVisuals(includeAdorners: false);
            var renderTarget = new RenderTargetBitmap();
            await renderTarget.RenderAsync(EditorSurfaceGrid, _imageWidth, _imageHeight);
            var buffer = await renderTarget.GetPixelsAsync();
            RebuildOverlayVisuals(includeAdorners: true);
            return buffer.ToArray();
        }

        private async Task SaveNewImageAndCloseAsync(string outputPath, byte[] pixels, int width, int height, string successLabel, bool copyToClipboard)
        {
            await SavePngAsync(outputPath, pixels, width, height);
            if (copyToClipboard)
            {
                await CopySavedImageToClipboardAsync(outputPath);
                InAppToastService.Show($"{successLabel}: {outputPath}. Copied to clipboard.", InAppToastSeverity.Success);
            }
            else
            {
                InAppToastService.Show($"{successLabel}: {outputPath}.", InAppToastSeverity.Success);
            }

            ImageEditorLauncher.CloseEditor(_filePath);
        }

        private static async Task CopySavedImageToClipboardAsync(string filePath)
        {
            var savedFile = await StorageFile.GetFileFromPathAsync(filePath);
            var package = new DataPackage();
            package.SetBitmap(RandomAccessStreamReference.CreateFromFile(savedFile));
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }

        private async Task SavePngAsync(string outputPath, byte[] pixels, int width, int height)
        {
            var pngBytes = await EncodePngBytesAsync(pixels, width, height);
            await File.WriteAllBytesAsync(outputPath, pngBytes);
        }

        private static async Task<byte[]> EncodePngBytesAsync(byte[] pixels, int width, int height)
        {
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)width,
                (uint)height,
                96,
                96,
                pixels);
            await encoder.FlushAsync();
            stream.Seek(0);
            using var memory = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(memory);
            return memory.ToArray();
        }

        private static byte[] CropPixels(byte[] source, int sourceWidth, int sourceHeight, EditorRect region)
        {
            var clampedRegion = ClampRegion(region, sourceWidth, sourceHeight);
            if (clampedRegion.IsEmpty)
            {
                return Array.Empty<byte>();
            }

            var output = new byte[clampedRegion.Width * clampedRegion.Height * 4];
            for (var row = 0; row < clampedRegion.Height; row++)
            {
                var sourceOffset = ((clampedRegion.Y + row) * sourceWidth + clampedRegion.X) * 4;
                var destinationOffset = row * clampedRegion.Width * 4;
                System.Buffer.BlockCopy(source, sourceOffset, output, destinationOffset, clampedRegion.Width * 4);
            }

            return output;
        }

        private static string BuildOutputPath(string sourcePath, string suffix)
        {
            var directory = Path.GetDirectoryName(sourcePath) ?? AppContext.BaseDirectory;
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(sourcePath);
            var candidatePath = Path.Combine(directory, $"{nameWithoutExtension}{suffix}.png");
            var counter = 1;
            while (File.Exists(candidatePath))
            {
                candidatePath = Path.Combine(directory, $"{nameWithoutExtension}{suffix}_{counter}.png");
                counter++;
            }

            return candidatePath;
        }

        private static int GetEffectiveCornerRadius(EditorRect region, int requestedRadius)
        {
            var maxAllowed = Math.Max(0, Math.Min(region.Width, region.Height) / 2);
            return Math.Clamp(requestedRadius, 0, maxAllowed);
        }

        private void EnsureSelectionRectangle()
        {
            if (_selectionRectangle is not null)
            {
                if (!OverlayCanvas.Children.Contains(_selectionRectangle))
                {
                    OverlayCanvas.Children.Add(_selectionRectangle);
                }

                return;
            }

            _selectionRectangle = new Rectangle
            {
                Stroke = new SolidColorBrush(ParseColor(DefaultPrimaryColor)),
                Fill = new SolidColorBrush(ParseColor(BlurOverlayColor)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 4 },
                RadiusX = _regionCornerRadius,
                RadiusY = _regionCornerRadius
            };
            OverlayCanvas.Children.Add(_selectionRectangle);
        }

        private void UpdateSelectionRectangle(Point currentPoint)
        {
            if (_selectionRectangle is null)
            {
                return;
            }

            var region = BuildRegion(_dragStartPoint, currentPoint);
            _selectionRectangle.Width = Math.Max(1, region.Width);
            _selectionRectangle.Height = Math.Max(1, region.Height);
            var selectionCornerRadius = GetEffectiveCornerRadius(region, _regionCornerRadius);
            _selectionRectangle.RadiusX = selectionCornerRadius;
            _selectionRectangle.RadiusY = selectionCornerRadius;
            Canvas.SetLeft(_selectionRectangle, region.X);
            Canvas.SetTop(_selectionRectangle, region.Y);
        }

        private void RemoveSelectionRectangle()
        {
            if (_selectionRectangle is null)
            {
                return;
            }

            OverlayCanvas.Children.Remove(_selectionRectangle);
            _selectionRectangle = null;
        }

        private bool IsPointInImage(Point point)
        {
            return point.X >= 0 &&
                   point.Y >= 0 &&
                   point.X < _imageWidth &&
                   point.Y < _imageHeight;
        }

        private void DrawResizeHandles(EditorRect bounds)
        {
            foreach (var (_, centerX, centerY) in EnumerateHandleCenters(bounds))
            {
                var handle = new Rectangle
                {
                    Width = ResizeHandleSize,
                    Height = ResizeHandleSize,
                    Fill = new SolidColorBrush(Colors.White),
                    Stroke = new SolidColorBrush(ParseColor(DefaultPrimaryColor)),
                    StrokeThickness = 2
                };
                Canvas.SetLeft(handle, centerX - (ResizeHandleSize / 2d));
                Canvas.SetTop(handle, centerY - (ResizeHandleSize / 2d));
                OverlayCanvas.Children.Add(handle);
            }
        }

        private void DrawArrowEndpointHandles(ArrowLayer arrowLayer)
        {
            DrawArrowEndpointHandle(ResizeHandle.ArrowStart, arrowLayer.StartX, arrowLayer.StartY);
            DrawArrowEndpointHandle(ResizeHandle.ArrowEnd, arrowLayer.EndX, arrowLayer.EndY);
        }

        private void DrawArrowEndpointHandle(ResizeHandle handle, double x, double y)
        {
            var size = ResizeHandleSize + 2;
            var endpoint = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(Colors.White),
                Stroke = new SolidColorBrush(ParseColor(DefaultPrimaryColor)),
                StrokeThickness = 2,
                Tag = handle
            };
            Canvas.SetLeft(endpoint, x - (size / 2d));
            Canvas.SetTop(endpoint, y - (size / 2d));
            OverlayCanvas.Children.Add(endpoint);
        }

        private bool TryBeginResize(Point point, PointerRoutedEventArgs e)
        {
            if (_document is not null &&
                _selectedLayerId.HasValue &&
                TryGetLayerById(_selectedLayerId.Value) is ArrowLayer arrowLayer)
            {
                var endpointHandle = GetArrowEndpointHandleAt(point, arrowLayer);
                if (endpointHandle is ResizeHandle.ArrowStart or ResizeHandle.ArrowEnd)
                {
                    _isResizingSelection = true;
                    _activeResizeHandle = endpointHandle;
                    _resizeStartBounds = arrowLayer.GetBounds();
                    _resizeLayerId = arrowLayer.Id;
                    _resizeTargetIsCrop = false;
                    _dragLayer = null;
                    OverlayCanvas.CapturePointer(e.Pointer);
                    return true;
                }
            }

            if (!TryGetActiveResizeTarget(out var bounds, out var layerId, out var isCrop))
            {
                return false;
            }

            var handle = GetResizeHandleAt(point, bounds);
            if (handle == ResizeHandle.None)
            {
                return false;
            }

            _isResizingSelection = true;
            _activeResizeHandle = handle;
            _resizeStartBounds = bounds;
            _resizeLayerId = layerId;
            _resizeTargetIsCrop = isCrop;
            _resizeStartTextFontSize = 0;
            if (_document is not null && _resizeLayerId.HasValue)
            {
                var layer = TryGetLayerById(_resizeLayerId.Value);
                if (layer is TextLayer textLayer)
                {
                    _resizeStartTextFontSize = textLayer.FontSize;
                }
            }
            _dragLayer = null;
            OverlayCanvas.CapturePointer(e.Pointer);
            return true;
        }

        private void ApplyResize(Point point)
        {
            if (_activeResizeHandle is ResizeHandle.ArrowStart or ResizeHandle.ArrowEnd)
            {
                ApplyArrowEndpointResize(point);
                return;
            }

            var left = _resizeStartBounds.X;
            var top = _resizeStartBounds.Y;
            var right = _resizeStartBounds.X + _resizeStartBounds.Width;
            var bottom = _resizeStartBounds.Y + _resizeStartBounds.Height;
            var pointX = Clamp((int)Math.Round(point.X), 0, _imageWidth);
            var pointY = Clamp((int)Math.Round(point.Y), 0, _imageHeight);

            switch (_activeResizeHandle)
            {
                case ResizeHandle.NorthWest:
                    left = Clamp(pointX, 0, right - MinResizableRegionSize);
                    top = Clamp(pointY, 0, bottom - MinResizableRegionSize);
                    break;
                case ResizeHandle.North:
                    top = Clamp(pointY, 0, bottom - MinResizableRegionSize);
                    break;
                case ResizeHandle.NorthEast:
                    right = Clamp(pointX, left + MinResizableRegionSize, _imageWidth);
                    top = Clamp(pointY, 0, bottom - MinResizableRegionSize);
                    break;
                case ResizeHandle.East:
                    right = Clamp(pointX, left + MinResizableRegionSize, _imageWidth);
                    break;
                case ResizeHandle.SouthEast:
                    right = Clamp(pointX, left + MinResizableRegionSize, _imageWidth);
                    bottom = Clamp(pointY, top + MinResizableRegionSize, _imageHeight);
                    break;
                case ResizeHandle.South:
                    bottom = Clamp(pointY, top + MinResizableRegionSize, _imageHeight);
                    break;
                case ResizeHandle.SouthWest:
                    left = Clamp(pointX, 0, right - MinResizableRegionSize);
                    bottom = Clamp(pointY, top + MinResizableRegionSize, _imageHeight);
                    break;
                case ResizeHandle.West:
                    left = Clamp(pointX, 0, right - MinResizableRegionSize);
                    break;
                default:
                    return;
            }

            var resized = new EditorRect(
                left,
                top,
                Math.Max(MinResizableRegionSize, right - left),
                Math.Max(MinResizableRegionSize, bottom - top));

            if (_resizeTargetIsCrop)
            {
                _pendingCropRect = resized;
                _isCropSelected = true;
                return;
            }

            if (_document is null || !_resizeLayerId.HasValue)
            {
                return;
            }

            var layer = TryGetLayerById(_resizeLayerId.Value);
            switch (layer)
            {
                case BorderLayer borderLayer:
                    borderLayer.Region = resized;
                    borderLayer.Name = $"Border ({resized.Width}x{resized.Height})";
                    break;
                case BlurLayer blurLayer:
                    blurLayer.Region = resized;
                    blurLayer.Name = $"Blur ({resized.Width}x{resized.Height})";
                    break;
                case HighlightLayer highlightLayer:
                    highlightLayer.Region = resized;
                    highlightLayer.Name = $"Highlight ({resized.Width}x{resized.Height})";
                    break;
                case TextLayer textLayer:
                    textLayer.X = resized.X;
                    textLayer.Y = resized.Y;
                    textLayer.UpdateWrapWidth(resized.Width);
                    var baseFontSize = _resizeStartTextFontSize > 0 ? _resizeStartTextFontSize : textLayer.FontSize;
                    var heightRatio = resized.Height / Math.Max(1.0, _resizeStartBounds.Height);
                    textLayer.FontSize = Math.Clamp(baseFontSize * heightRatio, 8, 180);
                    break;
            }
        }

        private void ApplyArrowEndpointResize(Point point)
        {
            if (_document is null || !_resizeLayerId.HasValue)
            {
                return;
            }

            if (TryGetLayerById(_resizeLayerId.Value) is not ArrowLayer arrowLayer)
            {
                return;
            }

            var clamped = ClampPointToImage(point);
            if (_activeResizeHandle == ResizeHandle.ArrowStart)
            {
                arrowLayer.StartX = clamped.X;
                arrowLayer.StartY = clamped.Y;
            }
            else if (_activeResizeHandle == ResizeHandle.ArrowEnd)
            {
                arrowLayer.EndX = clamped.X;
                arrowLayer.EndY = clamped.Y;
            }
        }

        private bool TryGetActiveResizeTarget(out EditorRect bounds, out Guid? layerId, out bool isCrop)
        {
            if (_isCropSelected && _pendingCropRect.HasValue && !_pendingCropRect.Value.IsEmpty)
            {
                bounds = _pendingCropRect.Value;
                layerId = null;
                isCrop = true;
                return true;
            }

            if (_document is not null && _selectedLayerId.HasValue)
            {
                var layer = TryGetLayerById(_selectedLayerId.Value);
                if (layer is BorderLayer borderLayer)
                {
                    bounds = borderLayer.Region;
                    layerId = borderLayer.Id;
                    isCrop = false;
                    return true;
                }

                if (layer is BlurLayer blurLayer)
                {
                    bounds = blurLayer.Region;
                    layerId = blurLayer.Id;
                    isCrop = false;
                    return true;
                }

                if (layer is HighlightLayer highlightLayer)
                {
                    bounds = highlightLayer.Region;
                    layerId = highlightLayer.Id;
                    isCrop = false;
                    return true;
                }

                if (layer is TextLayer textLayer)
                {
                    bounds = textLayer.GetBounds();
                    layerId = textLayer.Id;
                    isCrop = false;
                    return true;
                }
            }

            bounds = default;
            layerId = null;
            isCrop = false;
            return false;
        }

        private ResizeHandle GetResizeHandleAt(Point point, EditorRect bounds)
        {
            foreach (var (handle, centerX, centerY) in EnumerateHandleCenters(bounds))
            {
                var half = ResizeHandleSize / 2d;
                var rect = new Rect(centerX - half, centerY - half, ResizeHandleSize, ResizeHandleSize);
                if (rect.Contains(point))
                {
                    return handle;
                }
            }

            return ResizeHandle.None;
        }

        private ResizeHandle GetArrowEndpointHandleAt(Point point, ArrowLayer arrowLayer)
        {
            var tolerance = (ResizeHandleSize + 4) / 2d;
            if (Distance(point, new Point(arrowLayer.StartX, arrowLayer.StartY)) <= tolerance)
            {
                return ResizeHandle.ArrowStart;
            }

            if (Distance(point, new Point(arrowLayer.EndX, arrowLayer.EndY)) <= tolerance)
            {
                return ResizeHandle.ArrowEnd;
            }

            return ResizeHandle.None;
        }

        private static double Distance(Point a, Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static (ResizeHandle Handle, double X, double Y)[] EnumerateHandleCenters(EditorRect bounds)
        {
            var left = bounds.X;
            var top = bounds.Y;
            var right = bounds.X + bounds.Width;
            var bottom = bounds.Y + bounds.Height;
            var midX = left + (bounds.Width / 2d);
            var midY = top + (bounds.Height / 2d);

            return
            [
                (ResizeHandle.NorthWest, left, top),
                (ResizeHandle.North, midX, top),
                (ResizeHandle.NorthEast, right, top),
                (ResizeHandle.East, right, midY),
                (ResizeHandle.SouthEast, right, bottom),
                (ResizeHandle.South, midX, bottom),
                (ResizeHandle.SouthWest, left, bottom),
                (ResizeHandle.West, left, midY)
            ];
        }

        private EditorRect BuildRegion(Point start, Point end)
        {
            var x = (int)Math.Floor(Math.Min(start.X, end.X));
            var y = (int)Math.Floor(Math.Min(start.Y, end.Y));
            var width = (int)Math.Ceiling(Math.Abs(end.X - start.X));
            var height = (int)Math.Ceiling(Math.Abs(end.Y - start.Y));
            var region = new EditorRect(x, y, width, height);
            return ClampRegion(region, _imageWidth, _imageHeight);
        }

        private bool SelectTopMostLayerAt(Point point)
        {
            if (_document is null)
            {
                return false;
            }

            var selected = _document.Layers
                .Where(layer => layer.IsVisible)
                .FirstOrDefault(layer => layer.ContainsPoint(point.X, point.Y));

            if (selected is null)
            {
                _selectedLayerId = null;
                LayersListView.SelectedItem = null;
                UpdateDisplayedToolContext();
                UpdateToolButtonVisuals();
                UpdateSelectedTextEditorVisibility();
                return false;
            }

            _selectedLayerId = selected.Id;
            _isCropSelected = false;
            LayersListView.SelectedItem = selected;
            SyncToolSettingsFromSelectedLayer();
            UpdateDisplayedToolContext();
            UpdateToolButtonVisuals();
            UpdateSelectedTextEditorVisibility();
            return true;
        }

        private bool TrySelectCropAt(Point point)
        {
            if (!_pendingCropRect.HasValue || _pendingCropRect.Value.IsEmpty)
            {
                _isCropSelected = false;
                UpdateDisplayedToolContext();
                UpdateToolButtonVisuals();
                return false;
            }

            var crop = _pendingCropRect.Value;
            var isInsideCrop = point.X >= crop.X &&
                               point.Y >= crop.Y &&
                               point.X <= crop.X + crop.Width &&
                               point.Y <= crop.Y + crop.Height;
            if (!isInsideCrop)
            {
                _isCropSelected = false;
                UpdateDisplayedToolContext();
                UpdateToolButtonVisuals();
                UpdateSelectedTextEditorVisibility();
                return false;
            }

            _selectedLayerId = null;
            _isCropSelected = true;
            LayersListView.SelectedItem = null;
            UpdateDisplayedToolContext();
            UpdateToolButtonVisuals();
            UpdateSelectedTextEditorVisibility();
            return true;
        }

        private void UpdateSelectedTextEditorVisibility()
        {
            if (_document is null || _isCropSelected || _activeTool != EditorToolType.Move || !_selectedLayerId.HasValue)
            {
                EditSelectedTextButton.Visibility = Visibility.Collapsed;
                return;
            }

            var layer = TryGetLayerById(_selectedLayerId.Value);
            if (layer is not TextLayer)
            {
                EditSelectedTextButton.Visibility = Visibility.Collapsed;
                return;
            }

            EditSelectedTextButton.Visibility = Visibility.Visible;
        }

        private void EditSelectedTextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_inlineTextEditor is not null || !TryGetSelectedLayer(out var selectedLayer) || selectedLayer is not TextLayer textLayer)
            {
                return;
            }

            BeginInlineTextEntryForLayer(textLayer);
        }

        private void CommitInlineTextEditor()
        {
            if (_isCommittingInlineText || _inlineTextEditor is null || _document is null)
            {
                return;
            }

            _isCommittingInlineText = true;
            try
            {
                var text = _inlineTextEditor.Text;
                var editingLayerId = _editingTextLayerId;
                _editingTextLayerId = null;
                _inlineTextEditor.KeyDown -= InlineTextEditor_KeyDown;
                _inlineTextEditor.LostFocus -= InlineTextEditor_LostFocus;
                OverlayCanvas.Children.Remove(_inlineTextEditor);
                _inlineTextEditor = null;

                if (editingLayerId.HasValue)
                {
                    var existingLayer = TryGetLayerById(editingLayerId.Value);
                    if (existingLayer is TextLayer existingTextLayer && !string.IsNullOrWhiteSpace(text))
                    {
                        existingTextLayer.UpdateText(text);
                    }

                    if (existingLayer is not null)
                    {
                        SelectLayer(existingLayer);
                    }

                    SetActiveTool(EditorToolType.Move);
                    QueueRecompose(includeAdorners: true, includePixelEffects: false);
                    return;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    SetActiveTool(EditorToolType.Move);
                    QueueRecompose(includeAdorners: true, includePixelEffects: false);
                    return;
                }

                var layer = new TextLayer(
                    text,
                    _inlineTextPoint.X,
                    _inlineTextPoint.Y,
                    8,
                    DefaultTextColor,
                    _inlineTextWrapWidth);
                ApplyTextStyleFromUi(layer);

                _document.Layers.Insert(0, layer);
                SelectLayer(layer);
                SetActiveTool(EditorToolType.Move);
                QueueRecompose(includeAdorners: true, includePixelEffects: false);
            }
            finally
            {
                _isCommittingInlineText = false;
            }
        }

        private void CancelInlineTextEditor()
        {
            if (_inlineTextEditor is null)
            {
                return;
            }

            _editingTextLayerId = null;
            _inlineTextEditor.KeyDown -= InlineTextEditor_KeyDown;
            _inlineTextEditor.LostFocus -= InlineTextEditor_LostFocus;
            OverlayCanvas.Children.Remove(_inlineTextEditor);
            _inlineTextEditor = null;
            SetActiveTool(EditorToolType.Move);
            QueueRecompose(includeAdorners: true, includePixelEffects: false);
            _isCommittingInlineText = false;
        }

        private void InlineTextEditor_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitInlineTextEditor();
        }

        private void InlineTextEditor_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                CancelInlineTextEditor();
            }
        }

        private void ToolSettingSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingToolSettings)
            {
                return;
            }

            if (_isInitializingUi)
            {
                return;
            }

            if (ReferenceEquals(sender, ArrowFormComboBox))
            {
                UpdateArrowShadowToggleState(forceOffWhenUnsupported: true);
            }

            ApplySettingsToSelectedLayer();
            SaveCurrentEditorUiSettings();
        }

        private async void PickTextColorButton_Click(object sender, RoutedEventArgs e)
        {
            await PickColorForComboBoxAsync(TextColorComboBox, DefaultTextColor, "Pick text color");
        }

        private async void PickBorderColorButton_Click(object sender, RoutedEventArgs e)
        {
            await PickColorForComboBoxAsync(BorderColorComboBox, DefaultPrimaryColor, "Pick border color");
        }

        private async void PickTextBorderColorButton_Click(object sender, RoutedEventArgs e)
        {
            await PickColorForComboBoxAsync(TextBorderColorComboBox, DefaultBorderColor, "Pick text border color");
        }

        private async void PickArrowBorderColorButton_Click(object sender, RoutedEventArgs e)
        {
            await PickColorForComboBoxAsync(ArrowBorderColorComboBox, DefaultBorderColor, "Pick arrow border color");
        }

        private async void PickArrowColorButton_Click(object sender, RoutedEventArgs e)
        {
            await PickColorForComboBoxAsync(ArrowColorComboBox, DefaultPrimaryColor, "Pick arrow color");
        }

        private void ToolSettingSliderValueChanged(object sender, RangeBaseValueChangedEventArgs args)
        {
            if (_isSyncingToolSettings)
            {
                return;
            }

            if (_isInitializingUi)
            {
                return;
            }

            UpdateAllSettingValueTexts();
            ApplySettingsToSelectedLayer();
            SaveCurrentEditorUiSettings();
        }

        private void RegionCornerRadiusSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isSyncingRegionCornerRadius)
            {
                return;
            }

            if (_isInitializingUi)
            {
                return;
            }

            SetRegionCornerRadius(Clamp((int)Math.Round(e.NewValue), 0, MaxRegionCornerRadius), applyToSelectedLayer: true);
            SaveCurrentEditorUiSettings();
        }

        private void HighlightDimSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isSyncingToolSettings)
            {
                return;
            }

            if (_isInitializingUi)
            {
                return;
            }

            _highlightDimPercent = Clamp((int)Math.Round(e.NewValue), 0, MaxHighlightDimPercent);
            HighlightDimValueText.Text = $"{_highlightDimPercent}%";
            QueueRecompose(includeAdorners: true, includePixelEffects: true);
            SaveCurrentEditorUiSettings();
        }

        private void BlurInvertToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isSyncingToolSettings)
            {
                return;
            }

            if (_isInitializingUi)
            {
                return;
            }

            _blurInvertMode = BlurInvertToggle.IsOn;
            QueueRecompose(includeAdorners: true, includePixelEffects: true);
            SaveCurrentEditorUiSettings();
        }

        private void HighlightInvertToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isSyncingToolSettings)
            {
                return;
            }

            if (_isInitializingUi)
            {
                return;
            }

            _highlightInvertMode = HighlightInvertToggle.IsOn;
            QueueRecompose(includeAdorners: true, includePixelEffects: true);
            SaveCurrentEditorUiSettings();
        }

        private void ToolSettingToggled(object sender, RoutedEventArgs e)
        {
            if (_isSyncingToolSettings)
            {
                return;
            }

            if (_isInitializingUi)
            {
                return;
            }

            ApplySettingsToSelectedLayer();
            SaveCurrentEditorUiSettings();
        }

        private void SyncToolSettingsFromSelectedLayer()
        {
            if (!TryGetSelectedLayer(out var layer) || layer is null)
            {
                return;
            }

            _isSyncingToolSettings = true;
            try
            {
                switch (layer)
                {
                    case TextLayer textLayer:
                        SetComboBoxSelectedColor(TextColorComboBox, textLayer.ColorHex);
                        SetComboBoxSelectedColor(TextBorderColorComboBox, textLayer.BorderColorHex);
                        TextSizeNumberBox.Value = textLayer.FontSize;
                        TextBoldToggle.IsOn = textLayer.IsBold;
                        TextItalicToggle.IsOn = textLayer.IsItalic;
                        TextBorderToggle.IsOn = textLayer.HasBorder;
                        TextBorderThicknessNumberBox.Value = textLayer.BorderThickness;
                        TextShadowToggle.IsOn = textLayer.HasShadow;
                        SelectComboItemByContent(TextFontComboBox, GetFontName(textLayer.FontFamily));
                        break;
                    case BorderLayer borderLayer:
                        BorderThicknessNumberBox.Value = borderLayer.Thickness;
                        SetRegionCornerRadius(borderLayer.CornerRadius);
                        SetComboBoxSelectedColor(BorderColorComboBox, borderLayer.ColorHex);
                        BorderShadowToggle.IsOn = borderLayer.HasShadow;
                        break;
                    case BlurLayer blurLayer:
                        BlurRadiusNumberBox.Value = blurLayer.Radius;
                        BlurFeatherSlider.Value = Clamp(blurLayer.Feather, 0, 40);
                        SetRegionCornerRadius(blurLayer.CornerRadius);
                        BlurInvertToggle.IsOn = _blurInvertMode;
                        break;
                    case ArrowLayer arrowLayer:
                        ArrowThicknessNumberBox.Value = arrowLayer.Thickness;
                        SetComboBoxSelectedColor(ArrowColorComboBox, arrowLayer.ColorHex);
                        ArrowBorderToggle.IsOn = arrowLayer.HasBorder;
                        SetComboBoxSelectedColor(ArrowBorderColorComboBox, arrowLayer.BorderColorHex);
                        ArrowBorderThicknessNumberBox.Value = arrowLayer.BorderThickness;
                        var arrowFormTag = arrowLayer.FormStyle == ArrowFormStyle.LineOnly
                            ? nameof(ArrowFormStyle.Straight)
                            : arrowLayer.FormStyle.ToString();
                        SelectComboItemByTag(ArrowFormComboBox, arrowFormTag);
                        ArrowShadowToggle.IsOn = arrowLayer.FormStyle == ArrowFormStyle.Tapered
                            ? false
                            : arrowLayer.HasShadow;
                        break;
                    case HighlightLayer highlightLayer:
                        SetRegionCornerRadius(highlightLayer.CornerRadius);
                        HighlightDimSlider.Value = _highlightDimPercent;
                        HighlightDimValueText.Text = $"{_highlightDimPercent}%";
                        HighlightInvertToggle.IsOn = _highlightInvertMode;
                        break;
                }
            }
            finally
            {
                _isSyncingToolSettings = false;
            }

            UpdateArrowShadowToggleState(forceOffWhenUnsupported: true);
            UpdateAllSettingValueTexts();
        }

        private void ApplyPersistedEditorUiSettings()
        {
            var settings = SettingsService.LoadEditorUiSettings();

            _isSyncingToolSettings = true;
            _isSyncingRegionCornerRadius = true;
            try
            {
                SetComboBoxSelectedColor(TextColorComboBox, settings.PrimaryColorHex);
                SetComboBoxSelectedColor(BorderColorComboBox, settings.PrimaryColorHex);
                SetComboBoxSelectedColor(ArrowColorComboBox, settings.PrimaryColorHex);

                TextBorderThicknessNumberBox.Value = Clamp(settings.TextBorderThickness, 1, MaxPrimaryThickness);
                BorderThicknessNumberBox.Value = Clamp(settings.PrimaryThickness, 1, MaxPrimaryThickness);
                ArrowThicknessNumberBox.Value = Clamp(settings.PrimaryThickness, 1, MaxPrimaryThickness);

                SelectComboItemByContent(TextFontComboBox, settings.TextFont);
                TextSizeNumberBox.Value = Clamp(settings.TextSize, 8, 180);
                TextBoldToggle.IsOn = settings.TextBoldEnabled;
                TextItalicToggle.IsOn = settings.TextItalicEnabled;
                TextBorderToggle.IsOn = settings.TextBorderEnabled;
                SetComboBoxSelectedColor(TextBorderColorComboBox, settings.TextBorderColorHex);
                TextShadowToggle.IsOn = settings.TextShadowEnabled;
                BorderShadowToggle.IsOn = settings.BorderShadowEnabled;
                ArrowShadowToggle.IsOn = settings.ArrowShadowEnabled;

                ArrowBorderToggle.IsOn = settings.ArrowBorderEnabled;
                SetComboBoxSelectedColor(ArrowBorderColorComboBox, settings.ArrowBorderColorHex);
                ArrowBorderThicknessNumberBox.Value = Clamp(settings.ArrowBorderThickness, 1, 8);
                SelectComboItemByTag(ArrowFormComboBox, settings.ArrowFormStyle);

                BlurRadiusNumberBox.Value = Clamp(settings.BlurRadius, 1, 25);
                BlurFeatherSlider.Value = Clamp(settings.BlurFeather, 0, 40);
                _blurInvertMode = settings.BlurInvertMode;
                BlurInvertToggle.IsOn = _blurInvertMode;

                _highlightDimPercent = Clamp(settings.HighlightDimPercent, 0, MaxHighlightDimPercent);
                HighlightDimSlider.Value = _highlightDimPercent;
                HighlightDimValueText.Text = $"{_highlightDimPercent}%";
                _highlightInvertMode = settings.HighlightInvertMode;
                HighlightInvertToggle.IsOn = _highlightInvertMode;

                _regionCornerRadius = Clamp(settings.RegionCornerRadius, 0, MaxRegionCornerRadius);
                SetRegionCornerRadius(_regionCornerRadius);
                _performanceModeEnabled = settings.PerformanceModeEnabled;
                _gpuEffectsEnabled = settings.GpuEffectsEnabled;
            }
            finally
            {
                _isSyncingRegionCornerRadius = false;
                _isSyncingToolSettings = false;
            }

            UpdateArrowShadowToggleState(forceOffWhenUnsupported: true);
            UpdateAllSettingValueTexts();
        }

        private void SaveCurrentEditorUiSettings()
        {
            var arrowShadowEnabled = GetCurrentArrowShadowEnabled();
            var settings = new EditorUiSettings(
                GetSelectedColorHex(BorderColorComboBox, DefaultPrimaryColor),
                Clamp((int)Math.Round(BorderThicknessNumberBox.Value), 1, MaxPrimaryThickness),
                GetSelectedFont(),
                Clamp((int)Math.Round(TextSizeNumberBox.Value), 8, 180),
                TextBoldToggle.IsOn,
                TextItalicToggle.IsOn,
                TextBorderToggle.IsOn,
                GetSelectedColorHex(TextBorderColorComboBox, DefaultBorderColor),
                Clamp((int)Math.Round(TextBorderThicknessNumberBox.Value), 1, MaxPrimaryThickness),
                TextShadowToggle.IsOn,
                BorderShadowToggle.IsOn,
                ArrowBorderToggle.IsOn,
                GetSelectedColorHex(ArrowBorderColorComboBox, DefaultBorderColor),
                Clamp((int)Math.Round(ArrowBorderThicknessNumberBox.Value), 1, 8),
                arrowShadowEnabled,
                GetSelectedArrowFormStyle().ToString(),
                Clamp((int)Math.Round(BlurRadiusNumberBox.Value), 1, 25),
                Clamp((int)Math.Round(BlurFeatherSlider.Value), 0, 40),
                _blurInvertMode,
                Clamp(_highlightDimPercent, 0, MaxHighlightDimPercent),
                _highlightInvertMode,
                Clamp(_regionCornerRadius, 0, MaxRegionCornerRadius),
                _performanceModeEnabled,
                _gpuEffectsEnabled);

            SettingsService.SaveEditorUiSettings(settings);
        }

        private static void SelectComboItemByContent(ComboBox comboBox, string content)
        {
            var match = comboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                comboBox.SelectedItem = match;
            }
        }

        private static void SelectComboItemByTag(ComboBox comboBox, string tag)
        {
            var match = comboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                comboBox.SelectedItem = match;
            }
        }

        private void SetRegionCornerRadius(int cornerRadius, bool applyToSelectedLayer = false)
        {
            var normalizedRadius = Clamp(cornerRadius, 0, MaxRegionCornerRadius);
            _regionCornerRadius = normalizedRadius;

            _isSyncingRegionCornerRadius = true;
            BorderCornerRadiusSlider.Value = normalizedRadius;
            BlurCornerRadiusSlider.Value = normalizedRadius;
            HighlightCornerRadiusSlider.Value = normalizedRadius;
            _isSyncingRegionCornerRadius = false;
            BorderCornerRadiusValueText.Text = $"{normalizedRadius} px";
            BlurCornerRadiusValueText.Text = $"{normalizedRadius} px";
            HighlightCornerRadiusValueText.Text = $"{normalizedRadius} px";
            UpdateSelectionRectangleCornerRadius();

            if (!applyToSelectedLayer)
            {
                return;
            }

            if (TryGetSelectedLayer(out var layer) &&
                layer is BorderLayer or BlurLayer or HighlightLayer)
            {
                ApplySettingsToSelectedLayer();
                return;
            }

            RebuildOverlayVisuals(includeAdorners: true);
        }

        private void UpdateAllSettingValueTexts()
        {
            TextBorderThicknessValueText.Text = $"{Clamp((int)Math.Round(TextBorderThicknessNumberBox.Value), 1, MaxPrimaryThickness)} px";
            BorderThicknessValueText.Text = $"{Clamp((int)Math.Round(BorderThicknessNumberBox.Value), 1, MaxPrimaryThickness)} px";
            ArrowBorderThicknessValueText.Text = $"{Clamp((int)Math.Round(ArrowBorderThicknessNumberBox.Value), 1, 8)} px";
            BorderCornerRadiusValueText.Text = $"{Clamp((int)Math.Round(BorderCornerRadiusSlider.Value), 0, MaxRegionCornerRadius)} px";
            BlurCornerRadiusValueText.Text = $"{Clamp((int)Math.Round(BlurCornerRadiusSlider.Value), 0, MaxRegionCornerRadius)} px";
            BlurFeatherValueText.Text = $"{Clamp((int)Math.Round(BlurFeatherSlider.Value), 0, 40)} px";
            HighlightCornerRadiusValueText.Text = $"{Clamp((int)Math.Round(HighlightCornerRadiusSlider.Value), 0, MaxRegionCornerRadius)} px";
            HighlightDimValueText.Text = $"{Clamp(_highlightDimPercent, 0, MaxHighlightDimPercent)}%";
        }

        private void UpdateSelectionRectangleCornerRadius()
        {
            if (_selectionRectangle is null)
            {
                return;
            }

            var width = Math.Max(1, (int)Math.Round(_selectionRectangle.Width));
            var height = Math.Max(1, (int)Math.Round(_selectionRectangle.Height));
            var effectiveCornerRadius = Math.Min(_regionCornerRadius, Math.Min(width, height) / 2);
            _selectionRectangle.RadiusX = effectiveCornerRadius;
            _selectionRectangle.RadiusY = effectiveCornerRadius;
        }

        private static int GetLayerCornerRadius(EditorLayer layer)
        {
            return layer switch
            {
                BorderLayer borderLayer => borderLayer.CornerRadius,
                BlurLayer blurLayer => blurLayer.CornerRadius,
                HighlightLayer highlightLayer => highlightLayer.CornerRadius,
                _ => 0
            };
        }

        private void ApplySettingsToSelectedLayer()
        {
            if (_document is null || !_selectedLayerId.HasValue)
            {
                return;
            }

            var layer = TryGetLayerById(_selectedLayerId.Value);
            if (layer is null)
            {
                return;
            }

            var includePixelEffects = layer is BlurLayer or HighlightLayer;
            if (layer is TextLayer textLayer)
            {
                ApplyTextStyleFromUi(textLayer);
            }
            else if (layer is BorderLayer borderLayer)
            {
                ApplyBorderStyleFromUi(borderLayer);
            }
            else if (layer is BlurLayer blurLayer)
            {
                blurLayer.Radius = Clamp((int)Math.Round(BlurRadiusNumberBox.Value), 1, 25);
                blurLayer.Feather = Clamp((int)Math.Round(BlurFeatherSlider.Value), 0, 40);
                blurLayer.CornerRadius = Clamp((int)Math.Round(BlurCornerRadiusSlider.Value), 0, MaxRegionCornerRadius);
            }
            else if (layer is HighlightLayer highlightLayer)
            {
                highlightLayer.CornerRadius = Clamp((int)Math.Round(HighlightCornerRadiusSlider.Value), 0, MaxRegionCornerRadius);
            }
            else if (layer is ArrowLayer arrowLayer)
            {
                ApplyArrowStyleFromUi(arrowLayer);
            }

            QueueRecompose(includeAdorners: true, includePixelEffects: includePixelEffects);
        }

        private void ApplyTextStyleFromUi(TextLayer textLayer)
        {
            textLayer.FontSize = Clamp((int)Math.Round(TextSizeNumberBox.Value), 8, 180);
            textLayer.ColorHex = GetSelectedColorHex(TextColorComboBox, DefaultTextColor);
            textLayer.IsBold = TextBoldToggle.IsOn;
            textLayer.IsItalic = TextItalicToggle.IsOn;
            textLayer.HasBorder = TextBorderToggle.IsOn;
            textLayer.BorderColorHex = GetSelectedColorHex(TextBorderColorComboBox, DefaultBorderColor);
            textLayer.BorderThickness = Clamp((int)Math.Round(TextBorderThicknessNumberBox.Value), 1, MaxPrimaryThickness);
            textLayer.HasShadow = TextShadowToggle.IsOn;
            textLayer.ShadowOffset = DefaultSharedShadowOffset;
            textLayer.ShadowColorHex = DefaultSharedShadowColor;
            textLayer.FontFamily = GetSelectedFont();
        }

        private void ApplyBorderStyleFromUi(BorderLayer borderLayer)
        {
            borderLayer.Thickness = Clamp((int)Math.Round(BorderThicknessNumberBox.Value), 1, 24);
            borderLayer.CornerRadius = _regionCornerRadius;
            borderLayer.ColorHex = GetSelectedColorHex(BorderColorComboBox, DefaultPrimaryColor);
            borderLayer.HasShadow = BorderShadowToggle.IsOn;
            borderLayer.ShadowColorHex = DefaultSharedShadowColor;
            borderLayer.ShadowOffset = DefaultSharedShadowOffset;
        }

        private void ApplyArrowStyleFromUi(ArrowLayer arrowLayer)
        {
            var formStyle = GetSelectedArrowFormStyle();
            arrowLayer.Thickness = Clamp((int)Math.Round(ArrowThicknessNumberBox.Value), 1, 24);
            arrowLayer.ColorHex = GetSelectedColorHex(ArrowColorComboBox, DefaultPrimaryColor);
            arrowLayer.HasBorder = ArrowBorderToggle.IsOn;
            arrowLayer.BorderColorHex = GetSelectedColorHex(ArrowBorderColorComboBox, DefaultBorderColor);
            arrowLayer.BorderThickness = Clamp((int)Math.Round(ArrowBorderThicknessNumberBox.Value), 1, 8);
            arrowLayer.HasShadow = formStyle != ArrowFormStyle.Tapered && ArrowShadowToggle.IsOn;
            arrowLayer.ShadowColorHex = DefaultSharedShadowColor;
            arrowLayer.ShadowOffset = DefaultSharedShadowOffset;
            arrowLayer.FormStyle = formStyle;
        }

        private bool GetCurrentArrowShadowEnabled()
        {
            return GetSelectedArrowFormStyle() != ArrowFormStyle.Tapered && ArrowShadowToggle.IsOn;
        }

        private void UpdateArrowShadowToggleState(bool forceOffWhenUnsupported)
        {
            var supportsShadow = GetSelectedArrowFormStyle() != ArrowFormStyle.Tapered;
            ArrowShadowToggle.IsEnabled = supportsShadow;

            if (!supportsShadow && forceOffWhenUnsupported)
            {
                _isSyncingToolSettings = true;
                try
                {
                    ArrowShadowToggle.IsOn = false;
                }
                finally
                {
                    _isSyncingToolSettings = false;
                }
            }
        }

        private ArrowFormStyle GetSelectedArrowFormStyle()
        {
            if (ArrowFormComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag &&
                Enum.TryParse<ArrowFormStyle>(tag, true, out var style))
            {
                return style;
            }

            return ArrowFormStyle.Tapered;
        }

        private async Task PickColorForComboBoxAsync(ComboBox comboBox, string fallbackHex, string title)
        {
            var colorPicker = new ColorPicker
            {
                IsAlphaEnabled = true,
                IsColorChannelTextInputVisible = true,
                IsColorSliderVisible = true,
                IsHexInputVisible = true,
                Color = ParseColor(GetSelectedColorHex(comboBox, fallbackHex)),
                MinWidth = 320
            };

            var dialog = new ContentDialog
            {
                Title = title,
                Content = colorPicker,
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            var hex = ColorToHex(colorPicker.Color);
            SetComboBoxSelectedColor(comboBox, hex);
            ApplySettingsToSelectedLayer();
            SaveCurrentEditorUiSettings();
        }

        private static void SetComboBoxSelectedColor(ComboBox comboBox, string hex)
        {
            var match = comboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is string itemHex && string.Equals(itemHex, hex, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                comboBox.SelectedItem = match;
                return;
            }

            var custom = comboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Content?.ToString(), "Custom", StringComparison.OrdinalIgnoreCase));
            if (custom is null)
            {
                custom = new ComboBoxItem { Content = "Custom", Tag = hex };
                comboBox.Items.Add(custom);
            }
            else
            {
                custom.Tag = hex;
            }

            comboBox.SelectedItem = custom;
        }

        private static string ColorToHex(Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private static string GetSelectedColorHex(ComboBox comboBox, string fallback)
        {
            if (comboBox.SelectedItem is ComboBoxItem item && item.Tag is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                return hex;
            }

            return fallback;
        }

        private string GetSelectedFont()
        {
            if (TextFontComboBox.SelectedItem is ComboBoxItem item && item.Content is string value && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return "Segoe UI";
        }

        private static string GetFontName(string? fontFamily)
        {
            return string.IsNullOrWhiteSpace(fontFamily)
                ? "Segoe UI"
                : fontFamily;
        }

        private static void ApplyTextStyleToBlock(TextBlock textBlock, TextLayer textLayer)
        {
            textBlock.FontWeight = textLayer.IsBold ? FontWeights.Bold : FontWeights.Normal;
            textBlock.FontStyle = textLayer.IsItalic ? UiFontStyle.Italic : UiFontStyle.Normal;
            textBlock.LineStackingStrategy = LineStackingStrategy.MaxHeight;
            textBlock.TextLineBounds = TextLineBounds.Full;
        }

        private static void ApplyTextStyleToEditor(TextBox textBox, bool isBold, bool isItalic)
        {
            textBox.FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal;
            textBox.FontStyle = isItalic ? UiFontStyle.Italic : UiFontStyle.Normal;
        }

        private bool IsPointOutsideInlineEditor(Point pointerPoint)
        {
            if (_inlineTextEditor is null)
            {
                return false;
            }

            var left = Canvas.GetLeft(_inlineTextEditor);
            var top = Canvas.GetTop(_inlineTextEditor);
            var right = left + _inlineTextEditor.ActualWidth;
            var bottom = top + _inlineTextEditor.ActualHeight;

            if (_inlineTextEditor.ActualWidth <= 0 || _inlineTextEditor.ActualHeight <= 0)
            {
                right = left + _inlineTextEditor.Width;
                bottom = top + 36;
            }

            return pointerPoint.X < left ||
                   pointerPoint.X > right ||
                   pointerPoint.Y < top ||
                   pointerPoint.Y > bottom;
        }

        private static EditorRect ClampRegion(EditorRect region, int width, int height)
        {
            var x = Math.Clamp(region.X, 0, Math.Max(0, width - 1));
            var y = Math.Clamp(region.Y, 0, Math.Max(0, height - 1));
            var right = Math.Clamp(region.X + region.Width, 0, width);
            var bottom = Math.Clamp(region.Y + region.Height, 0, height);
            return new EditorRect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static Color ParseColor(string colorHex)
        {
            if (string.IsNullOrWhiteSpace(colorHex))
            {
                return Colors.White;
            }

            var value = colorHex.Trim().TrimStart('#');
            if (value.Length == 6)
            {
                var rgb = Convert.ToUInt32(value, 16);
                var r = (byte)((rgb & 0xFF0000) >> 16);
                var g = (byte)((rgb & 0x00FF00) >> 8);
                var b = (byte)(rgb & 0x0000FF);
                return ColorHelper.FromArgb(255, r, g, b);
            }

            if (value.Length == 8)
            {
                var argb = Convert.ToUInt32(value, 16);
                var a = (byte)((argb & 0xFF000000) >> 24);
                var r = (byte)((argb & 0x00FF0000) >> 16);
                var g = (byte)((argb & 0x0000FF00) >> 8);
                var b = (byte)(argb & 0x000000FF);
                return ColorHelper.FromArgb(a, r, g, b);
            }

            return Colors.White;
        }

        private static Color WithAlpha(Color color, byte alpha)
        {
            return ColorHelper.FromArgb(alpha, color.R, color.G, color.B);
        }
    }
}
