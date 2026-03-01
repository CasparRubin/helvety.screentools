using helvety.screenshots.Editor;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using Line = Microsoft.UI.Xaml.Shapes.Line;
using Polygon = Microsoft.UI.Xaml.Shapes.Polygon;
using Polyline = Microsoft.UI.Xaml.Shapes.Polyline;
using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;

namespace helvety.screenshots.Views
{
    public sealed partial class ImageEditorPage : Page
    {
        private const string DefaultTextColor = "#FFFFFFFF";
        private const string BlurOverlayColor = "#66A0A0A0";
        private const int ResizeHandleSize = 12;
        private const int MinResizableRegionSize = 8;
        private const int MinTextRegionWidth = 80;
        private const int MinTextRegionHeight = 40;

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
            West = 8
        }

        private readonly string _filePath;
        private EditorDocument? _document;
        private byte[]? _originalPixels;
        private byte[]? _workingPixels;
        private int _imageWidth;
        private int _imageHeight;
        private bool _isDraggingSelection;
        private Point _dragStartPoint;
        private Rectangle? _selectionRectangle;
        private EditorRect? _pendingCropRect;
        private bool _isBusy;
        private EditorToolType _activeTool = EditorToolType.Move;
        private Guid? _selectedLayerId;
        private EditorLayer? _dragLayer;
        private Point _lastPointerPoint;
        private TextBox? _inlineTextEditor;
        private Point _inlineTextPoint;
        private int _inlineTextWrapWidth = 260;
        private bool _isCommittingInlineText;
        private bool _isApplyingFitWidthZoom;
        private bool _hasAppliedInitialFitWidth;
        private bool _userAdjustedZoom;
        private double _lastAppliedFitWidthZoom = 1d;
        private InMemoryRandomAccessStream? _clipboardImageStream;
        private bool _isCropSelected;
        private bool _isUpdatingSelectedTextUi;
        private bool _isResizingSelection;
        private ResizeHandle _activeResizeHandle;
        private EditorRect _resizeStartBounds;
        private Guid? _resizeLayerId;
        private bool _resizeTargetIsCrop;

        public ImageEditorPage(string filePath)
        {
            _filePath = filePath;
            InitializeComponent();
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
            _clipboardImageStream?.Dispose();
            _clipboardImageStream = null;
        }

        private async void ImageEditorPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadImageAsync();
        }

        private async Task LoadImageAsync()
        {
            try
            {
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
                _hasAppliedInitialFitWidth = false;
                _userAdjustedZoom = false;
                _lastAppliedFitWidthZoom = 1d;
                _isCropSelected = false;
                _isResizingSelection = false;
                _activeResizeHandle = ResizeHandle.None;
                _resizeLayerId = null;
                _resizeTargetIsCrop = false;

                LayersListView.ItemsSource = _document.Layers;
                EditorTitleText.Text = $"Image Editor - {Path.GetFileName(_filePath)}";
                EditorSurfaceGrid.Width = _imageWidth;
                EditorSurfaceGrid.Height = _imageHeight;
                OverlayCanvas.Width = _imageWidth;
                OverlayCanvas.Height = _imageHeight;
                CropSelectionText.Text = "No crop selected.";
                UpdateCropActionVisibility();
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
            var minZoom = EditorScrollViewer.MinZoomFactor > 0 ? EditorScrollViewer.MinZoomFactor : 0.1f;
            var maxZoom = EditorScrollViewer.MaxZoomFactor > minZoom ? EditorScrollViewer.MaxZoomFactor : 10f;
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
            }
        }

        private async void Layers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            await RecomposeAsync();
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
            await RecomposeAsync();
        }

        private async Task UpdateBaseImageAsync()
        {
            if (_workingPixels is null || _imageWidth <= 0 || _imageHeight <= 0)
            {
                return;
            }

            var bitmap = new WriteableBitmap(_imageWidth, _imageHeight);
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Seek(0, SeekOrigin.Begin);
                await stream.WriteAsync(_workingPixels, 0, _workingPixels.Length);
            }

            bitmap.Invalidate();
            BaseImage.Source = bitmap;
        }

        private void SetActiveTool(EditorToolType tool)
        {
            _activeTool = tool;
            ActiveToolText.Text = tool.ToString();
            MoveToolSettingsPanel.Visibility = tool == EditorToolType.Move ? Visibility.Visible : Visibility.Collapsed;
            TextToolSettingsPanel.Visibility = tool == EditorToolType.Text ? Visibility.Visible : Visibility.Collapsed;
            BorderToolSettingsPanel.Visibility = tool == EditorToolType.Border ? Visibility.Visible : Visibility.Collapsed;
            BlurToolSettingsPanel.Visibility = tool == EditorToolType.Blur ? Visibility.Visible : Visibility.Collapsed;
            ArrowToolSettingsPanel.Visibility = tool == EditorToolType.Arrow ? Visibility.Visible : Visibility.Collapsed;
            CropToolSettingsPanel.Visibility = tool == EditorToolType.Crop ? Visibility.Visible : Visibility.Collapsed;

            UpdateToolButtonVisuals();
            UpdateSelectedTextEditorVisibility();
        }

        private void UpdateToolButtonVisuals()
        {
            var selectedBrush = new SolidColorBrush(ColorHelper.FromArgb(70, 66, 133, 244));
            var normalBrush = Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var normal)
                && normal is Brush normalThemeBrush
                ? normalThemeBrush
                : new SolidColorBrush(ColorHelper.FromArgb(255, 40, 40, 40));

            MoveToolButton.Background = _activeTool == EditorToolType.Move ? selectedBrush : normalBrush;
            TextToolButton.Background = _activeTool == EditorToolType.Text ? selectedBrush : normalBrush;
            BorderToolButton.Background = _activeTool == EditorToolType.Border ? selectedBrush : normalBrush;
            BlurToolButton.Background = _activeTool == EditorToolType.Blur ? selectedBrush : normalBrush;
            ArrowToolButton.Background = _activeTool == EditorToolType.Arrow ? selectedBrush : normalBrush;
            CropToolButton.Background = _activeTool == EditorToolType.Crop ? selectedBrush : normalBrush;
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

            if (_activeTool == EditorToolType.Text)
            {
                if (_inlineTextEditor is not null && IsPointOutsideInlineEditor(point))
                {
                    CommitInlineTextEditor();
                }

                if (_inlineTextEditor is not null)
                {
                    return;
                }

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
                    _dragLayer = _document.Layers.FirstOrDefault(layer => layer.Id == _selectedLayerId.Value);
                    _lastPointerPoint = point;
                }
                else if (!hasSelectedLayer && TrySelectCropAt(point))
                {
                    _dragLayer = null;
                }

                _ = RecomposeAsync();
                return;
            }

            BeginSelectionDrag(point);
        }

        private void OverlayCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_activeTool == EditorToolType.Move && _isResizingSelection)
            {
                var currentPoint = e.GetCurrentPoint(OverlayCanvas).Position;
                ApplyResize(currentPoint);
                _ = RecomposeAsync();
                return;
            }

            if (_activeTool == EditorToolType.Move && _dragLayer is not null)
            {
                var currentPoint = e.GetCurrentPoint(OverlayCanvas).Position;
                var dx = currentPoint.X - _lastPointerPoint.X;
                var dy = currentPoint.Y - _lastPointerPoint.Y;
                _dragLayer.MoveBy(dx, dy, _imageWidth, _imageHeight);
                _lastPointerPoint = currentPoint;
                _ = RecomposeAsync();
                return;
            }

            if (!_isDraggingSelection)
            {
                return;
            }

            var point = e.GetCurrentPoint(OverlayCanvas).Position;
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
                OverlayCanvas.ReleasePointerCaptures();
                return;
            }

            if (!_isDraggingSelection || _document is null)
            {
                _dragLayer = null;
                return;
            }

            var endPoint = e.GetCurrentPoint(OverlayCanvas).Position;
            var maybeRegion = CompleteSelectionDrag(endPoint);
            if (maybeRegion is null)
            {
                _dragLayer = null;
                return;
            }

            await HandleCompletedRegionSelectionAsync(maybeRegion.Value, endPoint);

            _dragLayer = null;
        }

        private void BeginSelectionDrag(Point point)
        {
            _isDraggingSelection = true;
            _dragStartPoint = point;
            EnsureSelectionRectangle();
            UpdateSelectionRectangle(point);
        }

        private EditorRect? CompleteSelectionDrag(Point endPoint)
        {
            _isDraggingSelection = false;
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
            await RecomposeAsync();
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
            _inlineTextEditor.KeyDown += InlineTextEditor_KeyDown;
            _inlineTextEditor.LostFocus += InlineTextEditor_LostFocus;
            Canvas.SetLeft(_inlineTextEditor, left);
            Canvas.SetTop(_inlineTextEditor, top);
            OverlayCanvas.Children.Add(_inlineTextEditor);
            _inlineTextEditor.Focus(FocusState.Programmatic);
        }

        private void AddBorderLayer(EditorRect region)
        {
            if (_document is null)
            {
                return;
            }

            var thickness = Clamp((int)Math.Round(BorderThicknessNumberBox.Value), 1, 24);
            var layer = new BorderLayer(region, thickness, GetSelectedColorHex(BorderColorComboBox, "#FFFFA500"))
            {
                CornerRadius = Clamp((int)Math.Round(BorderCornerRadiusNumberBox.Value), 0, 50)
            };
            _document.Layers.Add(layer);
            SelectLayer(layer);
        }

        private void AddBlurLayer(EditorRect region)
        {
            if (_document is null)
            {
                return;
            }

            var radius = Clamp((int)Math.Round(BlurRadiusNumberBox.Value), 1, 25);
            var layer = new BlurLayer(region, radius);
            _document.Layers.Add(layer);
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
                Clamp((int)Math.Round(ArrowThicknessNumberBox.Value), 1, 24),
                GetSelectedColorHex(ArrowColorComboBox, "#FFFFA500"),
                GetSelectedArrowHeadStyle());
            _document.Layers.Add(layer);
            SelectLayer(layer);
            return true;
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
            SelectedLayerText.Text = layer.Name;
            UpdateSelectedTextEditorVisibility();
        }

        private void ClearCropSelection()
        {
            _pendingCropRect = null;
            _isCropSelected = false;
            CropSelectionText.Text = "No crop selected.";
            UpdateCropActionVisibility();
            UpdateSelectedTextEditorVisibility();
        }

        private void SelectCropRegion(EditorRect region)
        {
            _pendingCropRect = region;
            _selectedLayerId = null;
            _isCropSelected = true;
            LayersListView.SelectedItem = null;
            SelectedLayerText.Text = "Crop selection";
            CropSelectionText.Text = $"Crop selected: {region.Width}x{region.Height}";
            UpdateCropActionVisibility();
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

            var layer = _document.Layers.FirstOrDefault(item => item.Id == id);
            if (layer is null)
            {
                return;
            }

            _document.Layers.Remove(layer);
            if (_selectedLayerId == layer.Id)
            {
                _selectedLayerId = null;
                SelectedLayerText.Text = "No layer selected";
            }

            UpdateSelectedTextEditorVisibility();
            await RecomposeAsync();
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

            var layer = _document.Layers.FirstOrDefault(item => item.Id == id);
            if (layer is null)
            {
                return;
            }

            layer.IsVisible = isVisible;
            await RecomposeAsync();
        }

        private async void LayersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LayersListView.SelectedItem is not EditorLayer layer)
            {
                _selectedLayerId = null;
                _isCropSelected = false;
                SelectedLayerText.Text = "No layer selected";
                UpdateSelectedTextEditorVisibility();
                await RecomposeAsync();
                return;
            }

            _selectedLayerId = layer.Id;
            _isCropSelected = false;
            SelectedLayerText.Text = layer.Name;
            UpdateSelectedTextEditorVisibility();
            await RecomposeAsync();
        }

        private async void LayersListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            await RecomposeAsync();
        }

        private async Task RecomposeAsync()
        {
            if (_isBusy || _document is null || _originalPixels is null)
            {
                return;
            }

            _isBusy = true;
            try
            {
                _workingPixels = (byte[])_originalPixels.Clone();
                foreach (var blurLayer in _document.Layers.OfType<BlurLayer>().Where(layer => layer.IsVisible))
                {
                    ApplyBoxBlur(_workingPixels, _imageWidth, _imageHeight, blurLayer.Region, blurLayer.Radius);
                }

                await UpdateBaseImageAsync();
                RebuildOverlayVisuals(includeAdorners: true);
            }
            finally
            {
                _isBusy = false;
            }
        }

        private void RebuildOverlayVisuals(bool includeAdorners)
        {
            if (_document is null)
            {
                return;
            }

            OverlayCanvas.Children.Clear();
            foreach (var layer in _document.Layers.Where(item => item.IsVisible))
            {
                switch (layer)
                {
                    case TextLayer textLayer:
                        DrawTextLayer(textLayer);
                        break;

                    case BorderLayer borderLayer:
                        var borderRect = new Rectangle
                        {
                            Width = borderLayer.Region.Width,
                            Height = borderLayer.Region.Height,
                            Stroke = new SolidColorBrush(ParseColor(borderLayer.ColorHex)),
                            StrokeThickness = borderLayer.Thickness,
                            RadiusX = borderLayer.CornerRadius,
                            RadiusY = borderLayer.CornerRadius
                        };
                        Canvas.SetLeft(borderRect, borderLayer.Region.X);
                        Canvas.SetTop(borderRect, borderLayer.Region.Y);
                        OverlayCanvas.Children.Add(borderRect);
                        break;
                    case ArrowLayer arrowLayer:
                        DrawArrowLayer(arrowLayer);
                        break;
                }
            }

            if (includeAdorners && _selectedLayerId.HasValue)
            {
                var selected = _document.Layers.FirstOrDefault(layer => layer.Id == _selectedLayerId.Value && layer.IsVisible);
                if (selected is not null)
                {
                    var bounds = selected.GetBounds();
                    var selectedRect = new Rectangle
                    {
                        Width = bounds.Width,
                        Height = bounds.Height,
                        Stroke = new SolidColorBrush(ColorHelper.FromArgb(255, 66, 133, 244)),
                        StrokeThickness = 2,
                        StrokeDashArray = new DoubleCollection { 4, 2 },
                        Fill = new SolidColorBrush(ColorHelper.FromArgb(20, 66, 133, 244))
                    };
                    Canvas.SetLeft(selectedRect, bounds.X);
                    Canvas.SetTop(selectedRect, bounds.Y);
                    OverlayCanvas.Children.Add(selectedRect);

                    if (selected is BorderLayer or BlurLayer)
                    {
                        DrawResizeHandles(bounds);
                    }
                }
            }

            if (includeAdorners && _pendingCropRect.HasValue && !_pendingCropRect.Value.IsEmpty)
            {
                var crop = _pendingCropRect.Value;
                var cropRect = new Rectangle
                {
                    Width = crop.Width,
                    Height = crop.Height,
                    Stroke = _isCropSelected
                        ? new SolidColorBrush(ColorHelper.FromArgb(255, 66, 133, 244))
                        : new SolidColorBrush(ColorHelper.FromArgb(200, 66, 133, 244)),
                    Fill = new SolidColorBrush(ColorHelper.FromArgb(48, 66, 133, 244)),
                    StrokeThickness = _isCropSelected ? 2 : 1.5,
                    StrokeDashArray = new DoubleCollection { 4, 3 }
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

        private void DrawTextLayer(TextLayer textLayer)
        {
            if (textLayer.HasShadow)
            {
                var shadow = new TextBlock
                {
                    Text = textLayer.Text,
                    FontSize = textLayer.FontSize,
                    FontFamily = new FontFamily(GetFontName(textLayer.FontFamily)),
                    Foreground = new SolidColorBrush(ParseColor(textLayer.ShadowColorHex)),
                    Width = Math.Max(1, textLayer.WrapWidth),
                    TextWrapping = TextWrapping.Wrap
                };
                Canvas.SetLeft(shadow, textLayer.X + textLayer.ShadowOffset);
                Canvas.SetTop(shadow, textLayer.Y + textLayer.ShadowOffset);
                OverlayCanvas.Children.Add(shadow);
            }

            if (textLayer.HasBorder)
            {
                var thickness = Math.Clamp(textLayer.BorderThickness, 1, 6);
                for (var offsetY = -thickness; offsetY <= thickness; offsetY++)
                {
                    for (var offsetX = -thickness; offsetX <= thickness; offsetX++)
                    {
                        if (offsetX == 0 && offsetY == 0)
                        {
                            continue;
                        }

                        var outline = new TextBlock
                        {
                            Text = textLayer.Text,
                            FontSize = textLayer.FontSize,
                            FontFamily = new FontFamily(GetFontName(textLayer.FontFamily)),
                            Foreground = new SolidColorBrush(ParseColor(textLayer.BorderColorHex)),
                            Width = Math.Max(1, textLayer.WrapWidth),
                            TextWrapping = TextWrapping.Wrap
                        };
                        Canvas.SetLeft(outline, textLayer.X + offsetX);
                        Canvas.SetTop(outline, textLayer.Y + offsetY);
                        OverlayCanvas.Children.Add(outline);
                    }
                }
            }

            var mainText = new TextBlock
            {
                Text = textLayer.Text,
                FontSize = textLayer.FontSize,
                FontFamily = new FontFamily(GetFontName(textLayer.FontFamily)),
                Foreground = new SolidColorBrush(ParseColor(textLayer.ColorHex)),
                Width = Math.Max(1, textLayer.WrapWidth),
                TextWrapping = TextWrapping.Wrap
            };
            Canvas.SetLeft(mainText, textLayer.X);
            Canvas.SetTop(mainText, textLayer.Y);
            OverlayCanvas.Children.Add(mainText);
        }

        private void DrawArrowLayer(ArrowLayer arrowLayer)
        {
            var strokeBrush = new SolidColorBrush(ParseColor(arrowLayer.ColorHex));
            var thickness = Math.Max(1, arrowLayer.Thickness);

            var line = new Line
            {
                X1 = arrowLayer.StartX,
                Y1 = arrowLayer.StartY,
                X2 = arrowLayer.EndX,
                Y2 = arrowLayer.EndY,
                Stroke = strokeBrush,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            OverlayCanvas.Children.Add(line);

            var dx = arrowLayer.EndX - arrowLayer.StartX;
            var dy = arrowLayer.EndY - arrowLayer.StartY;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length < 0.001)
            {
                return;
            }

            var unitX = dx / length;
            var unitY = dy / length;
            var normalX = -unitY;
            var normalY = unitX;
            var headLength = Math.Max(10.0, thickness * 3.5);
            var headWidth = Math.Max(8.0, thickness * 3.0);
            var tipX = arrowLayer.StartX;
            var tipY = arrowLayer.StartY;
            var baseX = tipX + (unitX * headLength);
            var baseY = tipY + (unitY * headLength);
            var leftX = baseX + (normalX * (headWidth / 2d));
            var leftY = baseY + (normalY * (headWidth / 2d));
            var rightX = baseX - (normalX * (headWidth / 2d));
            var rightY = baseY - (normalY * (headWidth / 2d));

            switch (arrowLayer.HeadStyle)
            {
                case ArrowHeadStyle.Open:
                    var openHead = new Polyline
                    {
                        Stroke = strokeBrush,
                        StrokeThickness = thickness,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        Points = new PointCollection
                        {
                            new Point(leftX, leftY),
                            new Point(tipX, tipY),
                            new Point(rightX, rightY)
                        }
                    };
                    OverlayCanvas.Children.Add(openHead);
                    break;
                case ArrowHeadStyle.Diamond:
                    var backX = tipX + (unitX * headLength * 1.8);
                    var backY = tipY + (unitY * headLength * 1.8);
                    var diamond = new Polygon
                    {
                        Fill = strokeBrush,
                        Stroke = strokeBrush,
                        StrokeThickness = 1,
                        Points = new PointCollection
                        {
                            new Point(tipX, tipY),
                            new Point(leftX, leftY),
                            new Point(backX, backY),
                            new Point(rightX, rightY)
                        }
                    };
                    OverlayCanvas.Children.Add(diamond);
                    break;
                default:
                    var triangle = new Polygon
                    {
                        Fill = strokeBrush,
                        Stroke = strokeBrush,
                        StrokeThickness = 1,
                        Points = new PointCollection
                        {
                            new Point(tipX, tipY),
                            new Point(leftX, leftY),
                            new Point(rightX, rightY)
                        }
                    };
                    OverlayCanvas.Children.Add(triangle);
                    break;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CommitInlineTextEditor();
                var outputPath = BuildOutputPath(_filePath, "_edited");
                var pixels = await RenderCompositePixelsAsync();
                await SavePngAsync(outputPath, pixels, _imageWidth, _imageHeight);
                InAppToastService.Show($"Saved: {outputPath}", InAppToastSeverity.Success);
            }
            catch (Exception ex)
            {
                InAppToastService.Show($"Save failed ({ex.Message}).", InAppToastSeverity.Error);
            }
        }

        private async void OverrideButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CommitInlineTextEditor();
                var pixels = await RenderCompositePixelsAsync();
                await SavePngAsync(_filePath, pixels, _imageWidth, _imageHeight);
                InAppToastService.Show($"Overridden: {_filePath}", InAppToastSeverity.Success);
            }
            catch (Exception ex)
            {
                InAppToastService.Show($"Override failed ({ex.Message}).", InAppToastSeverity.Error);
            }
        }

        private async void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CommitInlineTextEditor();
                var pixels = await RenderCompositePixelsAsync();
                _clipboardImageStream?.Dispose();
                _clipboardImageStream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, _clipboardImageStream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    (uint)_imageWidth,
                    (uint)_imageHeight,
                    96,
                    96,
                    pixels);
                await encoder.FlushAsync();
                _clipboardImageStream.Seek(0);

                var package = new DataPackage();
                package.SetBitmap(RandomAccessStreamReference.CreateFromStream(_clipboardImageStream));
                Clipboard.SetContent(package);
                Clipboard.Flush();
                InAppToastService.Show("Copied image to clipboard.", InAppToastSeverity.Success);
            }
            catch (Exception ex)
            {
                InAppToastService.Show($"Copy failed ({ex.Message}).", InAppToastSeverity.Error);
            }
        }

        private async void SaveCropButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_pendingCropRect.HasValue || _pendingCropRect.Value.IsEmpty)
            {
                InAppToastService.Show("Select a crop region first.", InAppToastSeverity.Warning);
                return;
            }

            try
            {
                CommitInlineTextEditor();
                var region = _pendingCropRect.Value;
                var fullPixels = await RenderCompositePixelsAsync();
                var cropPixels = CropPixels(fullPixels, _imageWidth, _imageHeight, region);
                var outputPath = BuildOutputPath(_filePath, "_crop");
                await SavePngAsync(outputPath, cropPixels, region.Width, region.Height);
                InAppToastService.Show($"Cropped image saved: {outputPath}", InAppToastSeverity.Success);
            }
            catch (Exception ex)
            {
                InAppToastService.Show($"Crop save failed ({ex.Message}).", InAppToastSeverity.Error);
            }
        }

        private void UpdateCropActionVisibility()
        {
            SaveCropButton.Visibility = _pendingCropRect.HasValue && !_pendingCropRect.Value.IsEmpty
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private async Task<byte[]> RenderCompositePixelsAsync()
        {
            RebuildOverlayVisuals(includeAdorners: false);
            var renderTarget = new RenderTargetBitmap();
            await renderTarget.RenderAsync(EditorSurfaceGrid, _imageWidth, _imageHeight);
            var buffer = await renderTarget.GetPixelsAsync();
            RebuildOverlayVisuals(includeAdorners: true);
            return buffer.ToArray();
        }

        private static async Task SavePngAsync(string outputPath, byte[] pixels, int width, int height)
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
            await using var fileStream = File.Create(outputPath);
            await stream.AsStreamForRead().CopyToAsync(fileStream);
            await fileStream.FlushAsync();
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

        private static void ApplyBoxBlur(byte[] pixels, int imageWidth, int imageHeight, EditorRect region, int radius)
        {
            if (radius <= 0)
            {
                return;
            }

            var clampedRegion = ClampRegion(region, imageWidth, imageHeight);
            if (clampedRegion.IsEmpty)
            {
                return;
            }

            var source = (byte[])pixels.Clone();
            var xStart = clampedRegion.X;
            var yStart = clampedRegion.Y;
            var xEnd = clampedRegion.X + clampedRegion.Width - 1;
            var yEnd = clampedRegion.Y + clampedRegion.Height - 1;

            for (var y = yStart; y <= yEnd; y++)
            {
                for (var x = xStart; x <= xEnd; x++)
                {
                    var b = 0;
                    var g = 0;
                    var r = 0;
                    var a = 0;
                    var count = 0;

                    var minY = Math.Max(yStart, y - radius);
                    var maxY = Math.Min(yEnd, y + radius);
                    var minX = Math.Max(xStart, x - radius);
                    var maxX = Math.Min(xEnd, x + radius);

                    for (var sampleY = minY; sampleY <= maxY; sampleY++)
                    {
                        for (var sampleX = minX; sampleX <= maxX; sampleX++)
                        {
                            var sampleIndex = (sampleY * imageWidth + sampleX) * 4;
                            b += source[sampleIndex];
                            g += source[sampleIndex + 1];
                            r += source[sampleIndex + 2];
                            a += source[sampleIndex + 3];
                            count++;
                        }
                    }

                    var targetIndex = (y * imageWidth + x) * 4;
                    pixels[targetIndex] = (byte)(b / count);
                    pixels[targetIndex + 1] = (byte)(g / count);
                    pixels[targetIndex + 2] = (byte)(r / count);
                    pixels[targetIndex + 3] = (byte)(a / count);
                }
            }
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
                Stroke = new SolidColorBrush(ColorHelper.FromArgb(255, 255, 214, 10)),
                Fill = new SolidColorBrush(ParseColor(BlurOverlayColor)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 4 }
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
                    Stroke = new SolidColorBrush(ColorHelper.FromArgb(255, 66, 133, 244)),
                    StrokeThickness = 2
                };
                Canvas.SetLeft(handle, centerX - (ResizeHandleSize / 2d));
                Canvas.SetTop(handle, centerY - (ResizeHandleSize / 2d));
                OverlayCanvas.Children.Add(handle);
            }
        }

        private bool TryBeginResize(Point point, PointerRoutedEventArgs e)
        {
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
            _dragLayer = null;
            OverlayCanvas.CapturePointer(e.Pointer);
            return true;
        }

        private void ApplyResize(Point point)
        {
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
                CropSelectionText.Text = $"Crop selected: {resized.Width}x{resized.Height}";
                UpdateCropActionVisibility();
                return;
            }

            if (_document is null || !_resizeLayerId.HasValue)
            {
                return;
            }

            var layer = _document.Layers.FirstOrDefault(item => item.Id == _resizeLayerId.Value);
            switch (layer)
            {
                case BorderLayer borderLayer:
                    borderLayer.Region = resized;
                    borderLayer.Name = $"Border ({resized.Width}x{resized.Height})";
                    SelectedLayerText.Text = borderLayer.Name;
                    break;
                case BlurLayer blurLayer:
                    blurLayer.Region = resized;
                    blurLayer.Name = $"Blur ({resized.Width}x{resized.Height})";
                    SelectedLayerText.Text = blurLayer.Name;
                    break;
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
                var layer = _document.Layers.FirstOrDefault(item => item.Id == _selectedLayerId.Value);
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
                .Reverse()
                .FirstOrDefault(layer => layer.ContainsPoint(point.X, point.Y));

            if (selected is null)
            {
                _selectedLayerId = null;
                LayersListView.SelectedItem = null;
                SelectedLayerText.Text = "No layer selected";
                UpdateSelectedTextEditorVisibility();
                return false;
            }

            _selectedLayerId = selected.Id;
            _isCropSelected = false;
            LayersListView.SelectedItem = selected;
            SelectedLayerText.Text = selected.Name;
            UpdateSelectedTextEditorVisibility();
            return true;
        }

        private bool TrySelectCropAt(Point point)
        {
            if (!_pendingCropRect.HasValue || _pendingCropRect.Value.IsEmpty)
            {
                _isCropSelected = false;
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
                UpdateSelectedTextEditorVisibility();
                return false;
            }

            _selectedLayerId = null;
            _isCropSelected = true;
            LayersListView.SelectedItem = null;
            SelectedLayerText.Text = "Crop selection";
            UpdateSelectedTextEditorVisibility();
            return true;
        }

        private void UpdateSelectedTextEditorVisibility()
        {
            if (_document is null || _isCropSelected || _activeTool != EditorToolType.Move || !_selectedLayerId.HasValue)
            {
                MoveSelectedTextLabel.Visibility = Visibility.Collapsed;
                MoveSelectedTextTextBox.Visibility = Visibility.Collapsed;
                return;
            }

            var layer = _document.Layers.FirstOrDefault(item => item.Id == _selectedLayerId.Value);
            if (layer is not TextLayer textLayer)
            {
                MoveSelectedTextLabel.Visibility = Visibility.Collapsed;
                MoveSelectedTextTextBox.Visibility = Visibility.Collapsed;
                return;
            }

            MoveSelectedTextLabel.Visibility = Visibility.Visible;
            MoveSelectedTextTextBox.Visibility = Visibility.Visible;
            _isUpdatingSelectedTextUi = true;
            try
            {
                if (!string.Equals(MoveSelectedTextTextBox.Text, textLayer.Text, StringComparison.Ordinal))
                {
                    MoveSelectedTextTextBox.Text = textLayer.Text;
                }
            }
            finally
            {
                _isUpdatingSelectedTextUi = false;
            }
        }

        private void MoveSelectedTextTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingSelectedTextUi || _document is null || !_selectedLayerId.HasValue)
            {
                return;
            }

            var layer = _document.Layers.FirstOrDefault(item => item.Id == _selectedLayerId.Value);
            if (layer is not TextLayer textLayer)
            {
                return;
            }

            textLayer.UpdateText(MoveSelectedTextTextBox.Text ?? string.Empty);
            SelectedLayerText.Text = textLayer.Name;
            _ = RecomposeAsync();
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
                _inlineTextEditor.KeyDown -= InlineTextEditor_KeyDown;
                _inlineTextEditor.LostFocus -= InlineTextEditor_LostFocus;
                OverlayCanvas.Children.Remove(_inlineTextEditor);
                _inlineTextEditor = null;

                if (string.IsNullOrWhiteSpace(text))
                {
                    SetActiveTool(EditorToolType.Move);
                    _ = RecomposeAsync();
                    return;
                }

                var layer = new TextLayer(
                    text,
                    _inlineTextPoint.X,
                    _inlineTextPoint.Y,
                    Clamp((int)Math.Round(TextSizeNumberBox.Value), 8, 180),
                    GetSelectedColorHex(TextColorComboBox, DefaultTextColor),
                    _inlineTextWrapWidth)
                {
                    HasBorder = TextBorderToggle.IsOn,
                    BorderColorHex = GetSelectedColorHex(TextBorderColorComboBox, "#FF000000"),
                    BorderThickness = Clamp((int)Math.Round(TextBorderThicknessNumberBox.Value), 1, 6),
                    HasShadow = TextShadowToggle.IsOn,
                    ShadowOffset = Clamp((int)Math.Round(TextShadowOffsetNumberBox.Value), 1, 12),
                    FontFamily = GetSelectedFont()
                };

                _document.Layers.Add(layer);
                SelectLayer(layer);
                SetActiveTool(EditorToolType.Move);
                _ = RecomposeAsync();
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

            _inlineTextEditor.KeyDown -= InlineTextEditor_KeyDown;
            _inlineTextEditor.LostFocus -= InlineTextEditor_LostFocus;
            OverlayCanvas.Children.Remove(_inlineTextEditor);
            _inlineTextEditor = null;
            SetActiveTool(EditorToolType.Move);
            _ = RecomposeAsync();
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
            ApplySettingsToSelectedLayer();
        }

        private void ToolSettingValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            ApplySettingsToSelectedLayer();
        }

        private void ToolSettingToggled(object sender, RoutedEventArgs e)
        {
            ApplySettingsToSelectedLayer();
        }

        private void ApplySettingsToSelectedLayer()
        {
            if (_document is null || !_selectedLayerId.HasValue)
            {
                return;
            }

            var layer = _document.Layers.FirstOrDefault(item => item.Id == _selectedLayerId.Value);
            if (layer is null)
            {
                return;
            }

            if (layer is TextLayer textLayer)
            {
                textLayer.HasBorder = TextBorderToggle.IsOn;
                textLayer.BorderColorHex = GetSelectedColorHex(TextBorderColorComboBox, "#FF000000");
                textLayer.BorderThickness = Clamp((int)Math.Round(TextBorderThicknessNumberBox.Value), 1, 6);
                textLayer.HasShadow = TextShadowToggle.IsOn;
                textLayer.ShadowOffset = Clamp((int)Math.Round(TextShadowOffsetNumberBox.Value), 1, 12);
                textLayer.FontFamily = GetSelectedFont();
            }
            else if (layer is BorderLayer borderLayer)
            {
                borderLayer.CornerRadius = Clamp((int)Math.Round(BorderCornerRadiusNumberBox.Value), 0, 50);
            }
            else if (layer is ArrowLayer arrowLayer)
            {
                arrowLayer.Thickness = Clamp((int)Math.Round(ArrowThicknessNumberBox.Value), 1, 24);
                arrowLayer.ColorHex = GetSelectedColorHex(ArrowColorComboBox, "#FFFFA500");
                arrowLayer.HeadStyle = GetSelectedArrowHeadStyle();
            }

            _ = RecomposeAsync();
        }

        private ArrowHeadStyle GetSelectedArrowHeadStyle()
        {
            if (ArrowHeadComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag &&
                Enum.TryParse<ArrowHeadStyle>(tag, true, out var style))
            {
                return style;
            }

            return ArrowHeadStyle.Triangle;
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
    }
}
