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
    /// Layer-based PNG editor in a dedicated <see cref="Microsoft.UI.Xaml.Window"/> (see <see cref="Editor.ImageEditorLauncher"/>).
    /// Tools: Canvas (select, move, resize), Text, Border, Blur, Highlight, Arrow, and Crop.
    /// Pixel layers (blur/highlight) compose through <see cref="Editor.EditorComposePipeline"/> (Win2D GPU per-region ROI with CPU fallback);
    /// text, border, and arrow layers render as XAML vector visuals reused via <see cref="Editor.VectorCanvasHost"/>.
    /// Text: click or drag to place; the placement frame can be moved (border band) and resized (handles, including edge strips) while editing; double-click re-edits an existing text layer.
    /// Pointer drags defer full recomposes until release (blur/highlight do not live-preview during drag); arrow preview updates overlay adorners only.
    /// Optional performance mode skips expensive vector effects during drag, resize, and arrow preview.
    /// Delete and Backspace remove the selected layer or crop region (not while inline text editing has focus).
    /// Mouse wheel over the canvas adjusts the selected layer's primary size; Ctrl+wheel zooms the viewport.
    /// Toolbar defaults persist without refreshing the main app window.
    /// </summary>
    public sealed partial class ImageEditorPage : Page
    {
        private const string DefaultPrimaryColor = "#FFD81B60";
        private const string DefaultTextColor = DefaultPrimaryColor;
        private const string DefaultBorderColor = "#FFFFFFFF";
        private const string DefaultSharedShadowColor = "#66000000";
        private const int DefaultSharedShadowOffset = 2;
        private const string RegionSelectionPreviewFillColor = "#66A0A0A0";
        private const int ResizeHandleSize = 12;
        private const int MinResizableRegionSize = 8;
        private const int MinTextRegionWidth = 80;
        private const int MinTextRegionHeight = 40;
        private const int DefaultTextWrapWidth = 380;
        private const int DefaultTextRegionHeight = 100;
        private const int TextClickDragThresholdPx = 8;
        private const int TextLayerDoubleClickMs = 500;
        private const double TextLayerDoubleClickDistancePx = 16;
        private const int InlineTextChromePadding = 10;
        private const int MaxHighlightDimPercent = 80;
        private const int DefaultRegionCornerRadius = 8;
        private const int MaxRegionCornerRadius = 24;
        private const int MaxPrimaryThickness = 24;
        private const int InteractiveOverlayThrottleMs = 8;
        private const double CanvasZoomMinimum = 0.1d;
        private const double CanvasZoomMaximum = 8d;
        private const double CanvasZoomSliderStep = 0.05d;
        private const double CanvasZoomButtonStep = 0.10d;
        private const int WheelResizeDetent = 120;
        private const int WheelResizeMaxStepsPerEvent = 5;
        private const int MaxBlurRadius = 25;

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
        private EditorToolType _activeTool = EditorToolType.Canvas;
        private Guid? _selectedLayerId;
        private EditorLayer? _dragLayer;
        private Point _lastPointerPoint;
        private TextBox? _inlineTextEditor;
        private Guid? _editingTextLayerId;
        private Point _inlineTextPoint;
        private int _inlineTextWrapWidth = DefaultTextWrapWidth;
        private EditorRect? _inlineTextRegion;
        private bool _isDraggingInlineTextRegion;
        private Point _inlineTextRegionDragStart;
        private EditorRect _inlineTextRegionAtDragStart;
        private bool _resizeInlineTextRegion;
        private Guid? _pendingTextLayerDoubleClickId;
        private Point _pendingTextLayerDoubleClickPoint;
        private long _pendingTextLayerDoubleClickTicks;
        private bool _isCommittingInlineText;
        private bool _isApplyingFitWidthZoom;
        private bool _isSyncingCanvasZoomControls;
        private bool _hasAppliedInitialFitWidth;
        /// <summary>When true, viewport resize no longer auto-applies fit-width zoom until <see cref="CanvasZoomResetButton_Click"/> or a new image is opened.</summary>
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
        private EditorToolType _settingsTool = EditorToolType.Canvas;
        private int _highlightDimPercent = 35;
        private bool _blurInvertMode;
        private bool _highlightInvertMode;
        private int _regionCornerRadius = DefaultRegionCornerRadius;
        private bool _performanceModeEnabled;
        private bool _gpuEffectsEnabled = true;
        private bool _gpuEffectWarningShown;
        private bool _deferPixelRecomposeUntilPointerRelease;
        private bool _deferVectorRecomposeUntilPointerRelease;
        private bool _suspendLayerChangeRecompose;
        private bool _forceVectorFullRebuild;
        private bool _recomposeQueued;
        private bool _includeAdornersForQueuedRecompose;
        private bool _includePixelEffectsForQueuedRecompose;
        private long _lastInteractiveOverlayTicks;
        private readonly Dictionary<Guid, EditorLayer> _layersById = new();
        private readonly EditorComposePipeline _composePipeline = new();
        private readonly VectorCanvasHost _vectorCanvasHost = new();
        private readonly Stopwatch _composeStopwatch = new();
        private int _composeSampleCount;
        private long _composeSampleTotalMs;
        private KeyboardAccelerator? _deleteSelectionAccelerator;
        private KeyboardAccelerator? _backspaceSelectionAccelerator;
        private bool _deleteSelectionInProgress;

        public ImageEditorPage(string filePath)
        {
            _filePath = filePath;
            InitializeComponent();
            RegisterEditorKeyboardAccelerators();
            UpdateExportButtonLabels();
            ConfigureCanvasZoomControlBounds();
            SyncCanvasZoomControlsFromScrollViewer();
            _isInitializingUi = false;
            Loaded += ImageEditorPage_Loaded;
            Unloaded += ImageEditorPage_Unloaded;
            SettingsService.EditorUiSettingsChanged += SettingsService_EditorUiSettingsChanged;
            EditorScrollViewer.SizeChanged += EditorScrollViewer_SizeChanged;
            EditorScrollViewer.ViewChanged += EditorScrollViewer_ViewChanged;
        }

        private void ImageEditorPage_Unloaded(object sender, RoutedEventArgs e)
        {
            UnregisterEditorKeyboardAccelerators();
            CommitInlineTextEditor();
            if (_document is not null)
            {
                _document.Layers.CollectionChanged -= Layers_CollectionChanged;
            }

            _vectorCanvasHost.Clear(LayersCanvas);

            SettingsService.EditorUiSettingsChanged -= SettingsService_EditorUiSettingsChanged;
            Loaded -= ImageEditorPage_Loaded;
            Unloaded -= ImageEditorPage_Unloaded;
            EditorScrollViewer.SizeChanged -= EditorScrollViewer_SizeChanged;
            EditorScrollViewer.ViewChanged -= EditorScrollViewer_ViewChanged;
        }

        private async void ImageEditorPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadImageAsync();
        }

        /// <summary>Syncs performance mode and GPU preference when Settings → General changes or after reset all.</summary>
        private void SettingsService_EditorUiSettingsChanged()
        {
            var settings = SettingsService.LoadEditorUiSettings();
            _performanceModeEnabled = settings.PerformanceModeEnabled;
            _gpuEffectsEnabled = settings.GpuEffectsEnabled;
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
                SetActiveTool(EditorToolType.Canvas);
                UpdateSelectedTextEditorVisibility();
                _vectorCanvasHost.Clear(LayersCanvas);
                _forceVectorFullRebuild = true;
                await UpdateBaseImageAsync(_composePipeline.BeginPixelCompose());
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
            SyncCanvasZoomControlsFromScrollViewer();

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

        /// <summary>Zoom factor that fits <see cref="_imageWidth"/> to the scroll viewer viewport width, clamped to allowed range.</summary>
        private bool TryGetFitWidthZoom(out double fitZoom)
        {
            fitZoom = 1d;
            if (_imageWidth <= 0)
            {
                return false;
            }

            var viewportWidth = EditorScrollViewer.ViewportWidth;
            if (viewportWidth <= 0)
            {
                viewportWidth = EditorScrollViewer.ActualWidth;
            }

            if (viewportWidth <= 0)
            {
                return false;
            }

            var targetZoom = viewportWidth / _imageWidth;
            var (minZoom, maxZoom) = GetEffectiveZoomRange();
            fitZoom = Math.Clamp(targetZoom, minZoom, maxZoom);
            return true;
        }

        /// <summary>Sets scroll viewer zoom so the image spans the viewport width; updates Canvas tool Size controls.</summary>
        private void ApplyFitWidthZoom()
        {
            if (!TryGetFitWidthZoom(out var clampedZoom))
            {
                return;
            }

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
                SyncCanvasZoomControlsFromScrollViewer();
            }
        }

        private (double MinZoom, double MaxZoom) GetEffectiveZoomRange()
        {
            var minZoomFromViewer = EditorScrollViewer.MinZoomFactor > 0
                ? (double)EditorScrollViewer.MinZoomFactor
                : CanvasZoomMinimum;
            var maxZoomFromViewer = EditorScrollViewer.MaxZoomFactor > minZoomFromViewer
                ? (double)EditorScrollViewer.MaxZoomFactor
                : CanvasZoomMaximum;
            var effectiveMin = Math.Clamp(CanvasZoomMinimum, minZoomFromViewer, maxZoomFromViewer);
            var effectiveMax = Math.Clamp(CanvasZoomMaximum, effectiveMin, maxZoomFromViewer);
            return (effectiveMin, effectiveMax);
        }

        private void ConfigureCanvasZoomControlBounds()
        {
            var (minZoom, maxZoom) = GetEffectiveZoomRange();
            CanvasZoomSlider.Minimum = minZoom;
            CanvasZoomSlider.Maximum = maxZoom;
            CanvasZoomSlider.StepFrequency = CanvasZoomSliderStep;
        }

        private void SyncCanvasZoomControlsFromScrollViewer()
        {
            ConfigureCanvasZoomControlBounds();
            var (minZoom, maxZoom) = GetEffectiveZoomRange();
            var currentZoom = Math.Clamp(
                EditorScrollViewer.ZoomFactor > 0 ? (double)EditorScrollViewer.ZoomFactor : 1d,
                minZoom,
                maxZoom);

            _isSyncingCanvasZoomControls = true;
            try
            {
                if (Math.Abs(CanvasZoomSlider.Value - currentZoom) > 0.0001d)
                {
                    CanvasZoomSlider.Value = currentZoom;
                }

                CanvasZoomValueText.Text = $"{Math.Round(currentZoom * 100d):0}%";

                var isAtFitWidth = TryGetFitWidthZoom(out var fitZoom)
                    && Math.Abs(currentZoom - fitZoom) <= 0.01d;
                CanvasZoomResetButton.IsEnabled = _userAdjustedZoom || !isAtFitWidth;
            }
            finally
            {
                _isSyncingCanvasZoomControls = false;
            }
        }

        private void ApplyEditorZoom(double requestedZoom, bool markAsUserAdjusted)
        {
            var (minZoom, maxZoom) = GetEffectiveZoomRange();
            var clampedZoom = Math.Clamp(requestedZoom, minZoom, maxZoom);
            var currentZoom = EditorScrollViewer.ZoomFactor > 0 ? (double)EditorScrollViewer.ZoomFactor : 1d;
            if (Math.Abs(currentZoom - clampedZoom) < 0.0001d)
            {
                SyncCanvasZoomControlsFromScrollViewer();
                return;
            }

            EditorScrollViewer.ChangeView(horizontalOffset: null, verticalOffset: null, zoomFactor: (float)clampedZoom, disableAnimation: true);
            if (markAsUserAdjusted && _hasAppliedInitialFitWidth)
            {
                _userAdjustedZoom = true;
            }

            SyncCanvasZoomControlsFromScrollViewer();
        }

        private void CanvasZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            var currentZoom = EditorScrollViewer.ZoomFactor > 0 ? (double)EditorScrollViewer.ZoomFactor : 1d;
            ApplyEditorZoom(currentZoom - CanvasZoomButtonStep, markAsUserAdjusted: true);
        }

        private void CanvasZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            var currentZoom = EditorScrollViewer.ZoomFactor > 0 ? (double)EditorScrollViewer.ZoomFactor : 1d;
            ApplyEditorZoom(currentZoom + CanvasZoomButtonStep, markAsUserAdjusted: true);
        }

        private void CanvasZoomResetButton_Click(object sender, RoutedEventArgs e)
        {
            _userAdjustedZoom = false;
            ApplyFitWidthZoom();
        }

        private void CanvasZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isInitializingUi || _isSyncingCanvasZoomControls)
            {
                return;
            }

            ApplyEditorZoom(e.NewValue, markAsUserAdjusted: true);
        }

        private void Layers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildLayerIndex();
            if (_suspendLayerChangeRecompose)
            {
                return;
            }

            _forceVectorFullRebuild = true;
            QueueRecompose(includeAdorners: true, includePixelEffects: true);
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
            CanvasToolSettingsPanel.Visibility = _settingsTool == EditorToolType.Canvas ? Visibility.Visible : Visibility.Collapsed;
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
            if (_activeTool != EditorToolType.Canvas)
            {
                return _activeTool;
            }

            if (_isCropSelected && _pendingCropRect.HasValue && !_pendingCropRect.Value.IsEmpty)
            {
                return EditorToolType.Crop;
            }

            if (!TryGetSelectedLayer(out var selectedLayer) || selectedLayer is null)
            {
                return EditorToolType.Canvas;
            }

            return selectedLayer.LayerType switch
            {
                EditorLayerType.Text => EditorToolType.Text,
                EditorLayerType.Border => EditorToolType.Border,
                EditorLayerType.Blur => EditorToolType.Blur,
                EditorLayerType.Arrow => EditorToolType.Arrow,
                EditorLayerType.Highlight => EditorToolType.Highlight,
                _ => EditorToolType.Canvas
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
            var highlightedTool = _activeTool == EditorToolType.Canvas ? _settingsTool : _activeTool;
            CanvasToolButton.Background = CreateToolButtonBackground(highlightedTool == EditorToolType.Canvas);
            TextToolButton.Background = CreateToolButtonBackground(highlightedTool == EditorToolType.Text);
            BorderToolButton.Background = CreateToolButtonBackground(highlightedTool == EditorToolType.Border);
            BlurToolButton.Background = CreateToolButtonBackground(highlightedTool == EditorToolType.Blur);
            HighlightToolButton.Background = CreateToolButtonBackground(highlightedTool == EditorToolType.Highlight);
            ArrowToolButton.Background = CreateToolButtonBackground(highlightedTool == EditorToolType.Arrow);
            CropToolButton.Background = CreateToolButtonBackground(highlightedTool == EditorToolType.Crop);
        }

        private static Brush CreateToolButtonBackground(bool isSelected)
        {
            if (isSelected)
            {
                return EditorXamlResourceCache.ToolSelectedBackground;
            }

            return Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var normal)
                && normal is Brush normalThemeBrush
                ? normalThemeBrush
                : EditorXamlResourceCache.GetBrush("#FF282828");
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
                if (TryBeginInlineTextRegionResize(point, e))
                {
                    return;
                }

                if (TryBeginInlineTextRegionMove(point, e))
                {
                    return;
                }

                if (IsPointOutsideInlineEditor(point))
                {
                    CommitInlineTextEditor();
                }

                return;
            }

            OverlayCanvas.Focus(FocusState.Pointer);

            if (_activeTool == EditorToolType.Text)
            {
                if (TryHandleTextLayerDoubleClick(point))
                {
                    return;
                }

                if (TryGetTopmostTextLayerAt(point, out _))
                {
                    SelectTopMostLayerAt(point);
                    QueueOverlayOnlyRebuild(includeAdorners: true);
                    return;
                }

                ClearTextLayerDoubleClickState();
                BeginSelectionDrag(point);
                return;
            }

            if (_activeTool == EditorToolType.Canvas)
            {
                if (TryHandleTextLayerDoubleClick(point))
                {
                    _dragLayer = null;
                    return;
                }

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
            EditorSurfaceGrid_DoubleTapped(sender, e);
        }

        private void EditorSurfaceGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
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

            if (TryBeginInlineTextEditAt(point))
            {
                e.Handled = true;
            }
        }

        private void OverlayCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (!CanAdjustSelectedLayerSizeByWheel(e))
            {
                return;
            }

            var delta = e.GetCurrentPoint(OverlayCanvas).Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            var steps = (int)Math.Round(delta / (double)WheelResizeDetent);
            if (steps == 0)
            {
                steps = delta > 0 ? 1 : -1;
            }

            steps = Clamp(steps, -WheelResizeMaxStepsPerEvent, WheelResizeMaxStepsPerEvent);
            if (TryAdjustSelectedLayerSizeByWheel(steps, out var valueChanged) && valueChanged)
            {
                e.Handled = true;
            }
        }

        private bool CanAdjustSelectedLayerSizeByWheel(PointerRoutedEventArgs e)
        {
            if (_document is null || !_selectedLayerId.HasValue)
            {
                return false;
            }

            if ((e.KeyModifiers & VirtualKeyModifiers.Control) != 0)
            {
                return false;
            }

            if (_inlineTextEditor is not null &&
                _inlineTextEditor.Visibility == Visibility.Visible &&
                _inlineTextEditor.FocusState != FocusState.Unfocused)
            {
                return false;
            }

            if (_isDraggingSelection || _isResizingSelection || _isPreviewingArrow)
            {
                return false;
            }

            return true;
        }

        private bool TryAdjustSelectedLayerSizeByWheel(int step, out bool valueChanged)
        {
            valueChanged = false;
            if (step == 0 || _document is null || !_selectedLayerId.HasValue)
            {
                return false;
            }

            var layer = TryGetLayerById(_selectedLayerId.Value);
            if (layer is null)
            {
                return false;
            }

            _isSyncingToolSettings = true;
            try
            {
                switch (layer)
                {
                    case TextLayer:
                        {
                            var current = (int)Math.Round(TextSizeNumberBox.Value);
                            var next = Clamp(current + step, 8, 180);
                            if (next == current)
                            {
                                return true;
                            }

                            TextSizeNumberBox.Value = next;
                            valueChanged = true;
                            break;
                        }

                    case BorderLayer:
                        {
                            var current = (int)Math.Round(BorderThicknessNumberBox.Value);
                            var next = Clamp(current + step, 1, MaxPrimaryThickness);
                            if (next == current)
                            {
                                return true;
                            }

                            BorderThicknessNumberBox.Value = next;
                            valueChanged = true;
                            break;
                        }

                    case BlurLayer:
                        {
                            var current = (int)Math.Round(BlurRadiusNumberBox.Value);
                            var next = Clamp(current + step, 1, MaxBlurRadius);
                            if (next == current)
                            {
                                return true;
                            }

                            BlurRadiusNumberBox.Value = next;
                            valueChanged = true;
                            break;
                        }

                    case ArrowLayer:
                        {
                            var current = (int)Math.Round(ArrowThicknessNumberBox.Value);
                            var next = Clamp(current + step, 1, MaxPrimaryThickness);
                            if (next == current)
                            {
                                return true;
                            }

                            ArrowThicknessNumberBox.Value = next;
                            valueChanged = true;
                            break;
                        }

                    case HighlightLayer:
                        {
                            var next = Clamp(_highlightDimPercent + step, 0, MaxHighlightDimPercent);
                            if (next == _highlightDimPercent)
                            {
                                return true;
                            }

                            _highlightDimPercent = next;
                            HighlightDimSlider.Value = _highlightDimPercent;
                            HighlightDimValueText.Text = $"{_highlightDimPercent}%";
                            valueChanged = true;
                            break;
                        }

                    default:
                        return false;
                }
            }
            finally
            {
                _isSyncingToolSettings = false;
            }

            if (layer is HighlightLayer)
            {
                QueueRecompose(includeAdorners: true, includePixelEffects: true);
                SaveCurrentEditorUiSettings();
                return true;
            }

            ApplySettingsToSelectedLayer();
            UpdateAllSettingValueTexts();
            SaveCurrentEditorUiSettings();
            return true;
        }

        private void OverlayCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isDraggingInlineTextRegion && _inlineTextRegion.HasValue)
            {
                var currentPoint = e.GetCurrentPoint(OverlayCanvas).Position;
                var dx = currentPoint.X - _inlineTextRegionDragStart.X;
                var dy = currentPoint.Y - _inlineTextRegionDragStart.Y;
                var moved = _inlineTextRegionAtDragStart with
                {
                    X = Clamp((int)Math.Round(_inlineTextRegionAtDragStart.X + dx), 0, Math.Max(0, _imageWidth - _inlineTextRegionAtDragStart.Width)),
                    Y = Clamp((int)Math.Round(_inlineTextRegionAtDragStart.Y + dy), 0, Math.Max(0, _imageHeight - _inlineTextRegionAtDragStart.Height))
                };
                _inlineTextRegion = moved;
                SyncInlineTextEditorLayout();
                RefreshInlineTextOverlayChrome();
                return;
            }

            var plan = EditorPointerController.PlanPointerMoved(
                _activeTool,
                _isResizingSelection,
                _isDraggingSelection,
                _isPreviewingArrow,
                _dragLayer);

            switch (plan.Kind)
            {
                case EditorPointerController.PointerMoveKind.ResizeSelection:
                    {
                        var currentPoint = e.GetCurrentPoint(OverlayCanvas).Position;
                        if (_resizeInlineTextRegion)
                        {
                            ApplyInlineTextRegionResize(currentPoint);
                            RefreshInlineTextOverlayChrome();
                            return;
                        }

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
                        }
                        else
                        {
                            _deferVectorRecomposeUntilPointerRelease = true;
                        }

                        QueueOverlayOnlyRebuild(includeAdorners: true);
                        return;
                    }

                case EditorPointerController.PointerMoveKind.DragLayer:
                    {
                        var currentPoint = e.GetCurrentPoint(OverlayCanvas).Position;
                        var dx = currentPoint.X - _lastPointerPoint.X;
                        var dy = currentPoint.Y - _lastPointerPoint.Y;
                        _dragLayer!.MoveBy(dx, dy, _imageWidth, _imageHeight);
                        _lastPointerPoint = currentPoint;
                        if (plan.DeferPixelRecompose)
                        {
                            _deferPixelRecomposeUntilPointerRelease = true;
                        }
                        else if (plan.DeferVectorRecompose)
                        {
                            _deferVectorRecomposeUntilPointerRelease = true;
                        }

                        QueueOverlayOnlyRebuild(includeAdorners: true);
                        return;
                    }

                case EditorPointerController.PointerMoveKind.ArrowPreview:
                    {
                        var point = e.GetCurrentPoint(OverlayCanvas).Position;
                        _arrowPreviewEndPoint = ClampPointToImage(point);
                        QueueArrowPreviewOnlyRebuild();
                        return;
                    }

                case EditorPointerController.PointerMoveKind.RegionSelection:
                    {
                        var point = e.GetCurrentPoint(OverlayCanvas).Position;
                        UpdateSelectionRectangle(point);
                        return;
                    }

                default:
                    return;
            }
        }

        private async void OverlayCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isDraggingInlineTextRegion)
            {
                _isDraggingInlineTextRegion = false;
                OverlayCanvas.ReleasePointerCaptures();
                RefreshInlineTextOverlayChrome();
                return;
            }

            if (_isResizingSelection)
            {
                _isResizingSelection = false;
                _activeResizeHandle = ResizeHandle.None;
                _resizeLayerId = null;
                _resizeTargetIsCrop = false;
                _resizeStartTextFontSize = 0;
                var wasInlineTextResize = _resizeInlineTextRegion;
                _resizeInlineTextRegion = false;
                OverlayCanvas.ReleasePointerCaptures();
                if (wasInlineTextResize)
                {
                    RefreshInlineTextOverlayChrome();
                    return;
                }
                if (_deferPixelRecomposeUntilPointerRelease)
                {
                    _deferPixelRecomposeUntilPointerRelease = false;
                    await RecomposeAsync(includeAdorners: true, includePixelEffects: true);
                }
                else if (_deferVectorRecomposeUntilPointerRelease)
                {
                    _deferVectorRecomposeUntilPointerRelease = false;
                    await RecomposeAsync(includeAdorners: true, includePixelEffects: false);
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
                else if (_deferVectorRecomposeUntilPointerRelease)
                {
                    _deferVectorRecomposeUntilPointerRelease = false;
                    await RecomposeAsync(includeAdorners: true, includePixelEffects: false);
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
                if (AddArrowLayer(_dragStartPoint, endPoint))
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
            else if (_deferVectorRecomposeUntilPointerRelease)
            {
                _deferVectorRecomposeUntilPointerRelease = false;
                await RecomposeAsync(includeAdorners: true, includePixelEffects: false);
            }

            _dragLayer = null;
        }

        private void BeginSelectionDrag(Point point)
        {
            ClearTextLayerDoubleClickState();
            _isDraggingSelection = true;
            _dragStartPoint = point;
            if (_activeTool == EditorToolType.Arrow)
            {
                _isPreviewingArrow = true;
                _arrowPreviewEndPoint = point;
                RemoveSelectionRectangle();
                RebuildArrowPreviewOnly(includeAdorners: true);
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
            if (_activeTool == EditorToolType.Text)
            {
                if (region.Width < TextClickDragThresholdPx && region.Height < TextClickDragThresholdPx)
                {
                    return BuildDefaultTextRegionAt(_dragStartPoint);
                }

                return region;
            }

            return region.IsEmpty ? null : region;
        }

        private EditorRect BuildDefaultTextRegionAt(Point point)
        {
            var width = Math.Min(DefaultTextWrapWidth, Math.Max(MinTextRegionWidth, _imageWidth));
            var height = Math.Min(DefaultTextRegionHeight, Math.Max(MinTextRegionHeight, _imageHeight));
            var left = Clamp((int)Math.Floor(point.X), 0, Math.Max(0, _imageWidth - width));
            var top = Clamp((int)Math.Floor(point.Y), 0, Math.Max(0, _imageHeight - height));
            width = Math.Min(width, _imageWidth - left);
            height = Math.Min(height, _imageHeight - top);
            return new EditorRect(left, top, Math.Max(MinTextRegionWidth, width), Math.Max(MinTextRegionHeight, height));
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

            SetActiveTool(EditorToolType.Canvas);
            await RecomposeAsync(includeAdorners: true, includePixelEffects: true);
        }

        private void BeginInlineTextEntry(EditorRect region)
        {
            if (_document is null)
            {
                return;
            }

            ClearTextLayerDoubleClickState();
            _editingTextLayerId = null;
            _inlineTextRegion = ClampRegion(region, _imageWidth, _imageHeight);
            _inlineTextEditor = new TextBox
            {
                MinHeight = MinTextRegionHeight,
                PlaceholderText = "Type here — drag handles or border to move/resize",
                FontSize = Clamp((int)Math.Round(TextSizeNumberBox.Value), 8, 180),
                FontFamily = new FontFamily(GetSelectedFont()),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap
            };
            ApplyTextStyleToEditor(_inlineTextEditor, TextBoldToggle.IsOn, TextItalicToggle.IsOn);
            _inlineTextEditor.KeyDown += InlineTextEditor_KeyDown;
            _inlineTextEditor.LostFocus += InlineTextEditor_LostFocus;
            OverlayCanvas.Children.Add(_inlineTextEditor);
            SyncInlineTextEditorLayout();
            RefreshInlineTextOverlayChrome();
            _inlineTextEditor.Focus(FocusState.Programmatic);
        }

        private void BeginInlineTextEntryForLayer(TextLayer textLayer)
        {
            if (_document is null || _inlineTextEditor is not null)
            {
                return;
            }

            ClearTextLayerDoubleClickState();
            SelectLayer(textLayer);
            _editingTextLayerId = textLayer.Id;
            var bounds = textLayer.GetBounds();
            _inlineTextRegion = new EditorRect(
                bounds.X,
                bounds.Y,
                Math.Max(MinTextRegionWidth, bounds.Width),
                Math.Max(MinTextRegionHeight, bounds.Height + 12));
            _inlineTextEditor = new TextBox
            {
                MinHeight = MinTextRegionHeight,
                FontSize = textLayer.FontSize,
                FontFamily = new FontFamily(GetFontName(textLayer.FontFamily)),
                Text = textLayer.Text,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap
            };
            ApplyTextStyleToEditor(_inlineTextEditor, textLayer.IsBold, textLayer.IsItalic);
            _inlineTextEditor.KeyDown += InlineTextEditor_KeyDown;
            _inlineTextEditor.LostFocus += InlineTextEditor_LostFocus;
            OverlayCanvas.Children.Add(_inlineTextEditor);
            SyncInlineTextEditorLayout();
            RefreshInlineTextOverlayChrome();
            _inlineTextEditor.Focus(FocusState.Programmatic);
            _inlineTextEditor.SelectAll();
        }

        private void InsertNewLayer(EditorLayer layer)
        {
            if (_document is null)
            {
                return;
            }

            _suspendLayerChangeRecompose = true;
            try
            {
                _document.Layers.Insert(0, layer);
                _forceVectorFullRebuild = true;
            }
            finally
            {
                _suspendLayerChangeRecompose = false;
            }

            SelectLayer(layer);
        }

        private void AddBorderLayer(EditorRect region)
        {
            if (_document is null)
            {
                return;
            }

            var layer = new BorderLayer(region, 1, DefaultPrimaryColor);
            ApplyBorderStyleFromUi(layer);
            InsertNewLayer(layer);
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
            InsertNewLayer(layer);
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
            InsertNewLayer(layer);
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
            InsertNewLayer(layer);
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

            await RemoveLayerAsync(layer);
        }

        /// <summary>Removes a layer from the document and refreshes the canvas; used by the layers panel button and Delete/Backspace.</summary>
        private async Task RemoveLayerAsync(EditorLayer layer)
        {
            if (_document is null)
            {
                return;
            }

            _document.Layers.Remove(layer);
            if (_selectedLayerId == layer.Id)
            {
                _selectedLayerId = null;
            }

            LayersListView.SelectedItem = null;
            UpdateDisplayedToolContext();
            UpdateSelectedTextEditorVisibility();
            await RecomposeAsync(includeAdorners: true, includePixelEffects: LayerAffectsPixels(layer));
        }

        private void RegisterEditorKeyboardAccelerators()
        {
            _deleteSelectionAccelerator = new KeyboardAccelerator { Key = VirtualKey.Delete };
            _deleteSelectionAccelerator.Invoked += EditorDeleteSelectionAccelerator_Invoked;
            KeyboardAccelerators.Add(_deleteSelectionAccelerator);

            _backspaceSelectionAccelerator = new KeyboardAccelerator { Key = VirtualKey.Back };
            _backspaceSelectionAccelerator.Invoked += EditorDeleteSelectionAccelerator_Invoked;
            KeyboardAccelerators.Add(_backspaceSelectionAccelerator);
        }

        private void UnregisterEditorKeyboardAccelerators()
        {
            if (_deleteSelectionAccelerator is not null)
            {
                _deleteSelectionAccelerator.Invoked -= EditorDeleteSelectionAccelerator_Invoked;
                KeyboardAccelerators.Remove(_deleteSelectionAccelerator);
                _deleteSelectionAccelerator = null;
            }

            if (_backspaceSelectionAccelerator is not null)
            {
                _backspaceSelectionAccelerator.Invoked -= EditorDeleteSelectionAccelerator_Invoked;
                KeyboardAccelerators.Remove(_backspaceSelectionAccelerator);
                _backspaceSelectionAccelerator = null;
            }
        }

        private void EditorDeleteSelectionAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (!CanProcessDeleteSelectionKey())
            {
                return;
            }

            args.Handled = true;
            _ = ProcessDeleteSelectionKeyAsync();
        }

        private async void ImageEditorPage_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Delete && e.Key != VirtualKey.Back)
            {
                return;
            }

            if (!CanProcessDeleteSelectionKey())
            {
                return;
            }

            if (await ProcessDeleteSelectionKeyAsync())
            {
                e.Handled = true;
            }
        }

        private bool CanProcessDeleteSelectionKey()
        {
            if (_document is null)
            {
                return false;
            }

            if (_inlineTextEditor is not null &&
                _inlineTextEditor.Visibility == Visibility.Visible &&
                _inlineTextEditor.FocusState != FocusState.Unfocused)
            {
                return false;
            }

            return true;
        }

        private async Task<bool> ProcessDeleteSelectionKeyAsync()
        {
            if (_deleteSelectionInProgress || !CanProcessDeleteSelectionKey())
            {
                return false;
            }

            _deleteSelectionInProgress = true;
            try
            {
                if (_selectedLayerId.HasValue)
                {
                    var layer = TryGetLayerById(_selectedLayerId.Value);
                    if (layer is not null)
                    {
                        await RemoveLayerAsync(layer);
                        return true;
                    }

                    _selectedLayerId = null;
                    LayersListView.SelectedItem = null;
                }

                if (_isCropSelected && _pendingCropRect.HasValue)
                {
                    ClearCropSelection();
                    await RecomposeAsync(includeAdorners: true, includePixelEffects: false);
                    return true;
                }

                return false;
            }
            finally
            {
                _deleteSelectionInProgress = false;
            }
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
            OverlayCanvas.Focus(FocusState.Programmatic);
            await RecomposeAsync(includeAdorners: true, includePixelEffects: false);
        }

        private async void LayersListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            await RecomposeAsync(includeAdorners: true, includePixelEffects: true);
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
                Fill = new SolidColorBrush(ParseColor(RegionSelectionPreviewFillColor)),
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
            var left = bounds.X;
            var top = bounds.Y;
            var right = bounds.X + bounds.Width;
            var bottom = bounds.Y + bounds.Height;
            var half = ResizeHandleSize / 2d;
            var edgeThickness = ResizeHandleSize;
            var cornerInset = ResizeHandleSize * 1.5d;
            var innerBarWidth = Math.Max(0, bounds.Width - (cornerInset * 2));
            var innerBarHeight = Math.Max(0, bounds.Height - (cornerInset * 2));

            if (new Rect(left - half, top - half, edgeThickness, edgeThickness).Contains(point))
            {
                return ResizeHandle.NorthWest;
            }

            if (new Rect(right - half, top - half, edgeThickness, edgeThickness).Contains(point))
            {
                return ResizeHandle.NorthEast;
            }

            if (new Rect(left - half, bottom - half, edgeThickness, edgeThickness).Contains(point))
            {
                return ResizeHandle.SouthWest;
            }

            if (new Rect(right - half, bottom - half, edgeThickness, edgeThickness).Contains(point))
            {
                return ResizeHandle.SouthEast;
            }

            if (new Rect(left + cornerInset, top - half, innerBarWidth, edgeThickness).Contains(point))
            {
                return ResizeHandle.North;
            }

            if (new Rect(left + cornerInset, bottom - half, innerBarWidth, edgeThickness).Contains(point))
            {
                return ResizeHandle.South;
            }

            if (new Rect(left - half, top + cornerInset, edgeThickness, innerBarHeight).Contains(point))
            {
                return ResizeHandle.West;
            }

            if (new Rect(right - half, top + cornerInset, edgeThickness, innerBarHeight).Contains(point))
            {
                return ResizeHandle.East;
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

        private void ClearTextLayerDoubleClickState()
        {
            _pendingTextLayerDoubleClickId = null;
            _pendingTextLayerDoubleClickTicks = 0;
        }

        private bool TryGetTopmostTextLayerAt(Point point, out TextLayer? textLayer)
        {
            textLayer = null;
            if (_document is null)
            {
                return false;
            }

            var hit = _document.Layers
                .Where(layer => layer.IsVisible && layer is TextLayer)
                .Cast<TextLayer>()
                .FirstOrDefault(layer => layer.ContainsPoint(point.X, point.Y));
            if (hit is null)
            {
                return false;
            }

            textLayer = hit;
            return true;
        }

        private bool TryBeginInlineTextEditAt(Point point)
        {
            if (_document is null || _inlineTextEditor is not null)
            {
                return false;
            }

            if (!TryGetTopmostTextLayerAt(point, out var textLayer) || textLayer is null)
            {
                return false;
            }

            BeginInlineTextEntryForLayer(textLayer);
            return true;
        }

        private bool TryHandleTextLayerDoubleClick(Point point)
        {
            if (!TryGetTopmostTextLayerAt(point, out var textLayer) || textLayer is null)
            {
                ClearTextLayerDoubleClickState();
                return false;
            }

            var now = Environment.TickCount64;
            if (_pendingTextLayerDoubleClickId == textLayer.Id &&
                now - _pendingTextLayerDoubleClickTicks <= TextLayerDoubleClickMs &&
                Distance(point, _pendingTextLayerDoubleClickPoint) <= TextLayerDoubleClickDistancePx)
            {
                return TryBeginInlineTextEditAt(point);
            }

            SelectTopMostLayerAt(point);
            _pendingTextLayerDoubleClickId = textLayer.Id;
            _pendingTextLayerDoubleClickPoint = point;
            _pendingTextLayerDoubleClickTicks = now;
            return false;
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
            if (_document is null || _isCropSelected || _activeTool != EditorToolType.Canvas || !_selectedLayerId.HasValue)
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
                _inlineTextRegion = null;

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

                    SetActiveTool(EditorToolType.Canvas);
                    QueueRecompose(includeAdorners: true, includePixelEffects: false);
                    return;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    SetActiveTool(EditorToolType.Canvas);
                    QueueRecompose(includeAdorners: true, includePixelEffects: false);
                    return;
                }

                var placement = _inlineTextRegion ?? new EditorRect(
                    (int)Math.Round(_inlineTextPoint.X),
                    (int)Math.Round(_inlineTextPoint.Y),
                    _inlineTextWrapWidth,
                    MinTextRegionHeight);
                var layer = new TextLayer(
                    text,
                    placement.X + InlineTextChromePadding,
                    placement.Y + InlineTextChromePadding,
                    Clamp((int)Math.Round(TextSizeNumberBox.Value), 8, 180),
                    DefaultTextColor,
                    Math.Max(MinTextRegionWidth, placement.Width - (InlineTextChromePadding * 2)));
                ApplyTextStyleFromUi(layer);

                InsertNewLayer(layer);
                SetActiveTool(EditorToolType.Canvas);
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
            _inlineTextRegion = null;
            SetActiveTool(EditorToolType.Canvas);
            QueueRecompose(includeAdorners: true, includePixelEffects: false);
            _isCommittingInlineText = false;
        }

        private void InlineTextEditor_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isResizingSelection || _isDraggingInlineTextRegion)
            {
                return;
            }

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

        /// <summary>
        /// Loads persisted toolbar defaults (including performance mode and GPU preference) when an image opens.
        /// Live updates from Settings → General use <see cref="SettingsService_EditorUiSettingsChanged"/>.
        /// </summary>
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

        /// <summary>Persists toolbar defaults without raising <see cref="SettingsService.SettingsChanged"/> (avoids main-window focus steal).</summary>
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

            SettingsService.SaveEditorUiSettings(settings, notifyAppSettingsChanged: false);
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

        private static void ApplyTextStyleToEditor(TextBox textBox, bool isBold, bool isItalic)
        {
            textBox.FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal;
            textBox.FontStyle = isItalic ? UiFontStyle.Italic : UiFontStyle.Normal;
        }

        private void SyncInlineTextEditorLayout()
        {
            if (_inlineTextEditor is null || !_inlineTextRegion.HasValue)
            {
                return;
            }

            var region = _inlineTextRegion.Value;
            var innerWidth = Math.Max(MinTextRegionWidth, region.Width - (InlineTextChromePadding * 2));
            var innerHeight = Math.Max(MinTextRegionHeight, region.Height - (InlineTextChromePadding * 2));
            _inlineTextPoint = new Point(region.X + InlineTextChromePadding, region.Y + InlineTextChromePadding);
            _inlineTextWrapWidth = innerWidth;
            _inlineTextEditor.Width = innerWidth;
            _inlineTextEditor.Height = innerHeight;
            Canvas.SetLeft(_inlineTextEditor, _inlineTextPoint.X);
            Canvas.SetTop(_inlineTextEditor, _inlineTextPoint.Y);
        }

        private bool TryBeginInlineTextRegionResize(Point point, PointerRoutedEventArgs e)
        {
            if (_inlineTextEditor is null || !_inlineTextRegion.HasValue)
            {
                return false;
            }

            var region = _inlineTextRegion.Value;
            var handle = GetResizeHandleAt(point, region);
            if (handle == ResizeHandle.None)
            {
                return false;
            }

            _isResizingSelection = true;
            _resizeInlineTextRegion = true;
            _activeResizeHandle = handle;
            _resizeStartBounds = region;
            _resizeLayerId = null;
            _resizeTargetIsCrop = false;
            _resizeStartTextFontSize = 0;
            _dragLayer = null;
            OverlayCanvas.CapturePointer(e.Pointer);
            return true;
        }

        private bool TryBeginInlineTextRegionMove(Point point, PointerRoutedEventArgs e)
        {
            if (_inlineTextEditor is null || !_inlineTextRegion.HasValue || !IsPointInInlineTextMoveBand(point))
            {
                return false;
            }

            _isDraggingInlineTextRegion = true;
            _inlineTextRegionDragStart = point;
            _inlineTextRegionAtDragStart = _inlineTextRegion.Value;
            OverlayCanvas.CapturePointer(e.Pointer);
            return true;
        }

        private bool IsPointInInlineTextMoveBand(Point point)
        {
            if (!_inlineTextRegion.HasValue)
            {
                return false;
            }

            var region = _inlineTextRegion.Value;
            if (GetResizeHandleAt(point, region) != ResizeHandle.None)
            {
                return false;
            }

            var insideOuter = point.X >= region.X &&
                              point.X <= region.X + region.Width &&
                              point.Y >= region.Y &&
                              point.Y <= region.Y + region.Height;
            if (!insideOuter)
            {
                return false;
            }

            var innerLeft = region.X + InlineTextChromePadding;
            var innerTop = region.Y + InlineTextChromePadding;
            var innerRight = innerLeft + Math.Max(0, region.Width - (InlineTextChromePadding * 2));
            var innerBottom = innerTop + Math.Max(0, region.Height - (InlineTextChromePadding * 2));
            var insideInner = point.X > innerLeft &&
                              point.X < innerRight &&
                              point.Y > innerTop &&
                              point.Y < innerBottom;
            return !insideInner;
        }

        private void ApplyInlineTextRegionResize(Point point)
        {
            if (!_inlineTextRegion.HasValue)
            {
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

            _inlineTextRegion = ClampRegion(
                new EditorRect(
                    left,
                    top,
                    Math.Max(MinResizableRegionSize, right - left),
                    Math.Max(MinResizableRegionSize, bottom - top)),
                _imageWidth,
                _imageHeight);
            SyncInlineTextEditorLayout();
        }

        private bool IsPointOutsideInlineEditor(Point pointerPoint)
        {
            if (_inlineTextEditor is null)
            {
                return false;
            }

            if (_inlineTextRegion.HasValue)
            {
                var region = _inlineTextRegion.Value;
                return pointerPoint.X < region.X ||
                       pointerPoint.X > region.X + region.Width ||
                       pointerPoint.Y < region.Y ||
                       pointerPoint.Y > region.Y + region.Height;
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
