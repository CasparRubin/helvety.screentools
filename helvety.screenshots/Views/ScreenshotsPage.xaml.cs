using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace helvety.screenshots.Views
{
    public sealed partial class ScreenshotsPage : Page
    {
        private static readonly string[] EditableImageExtensions =
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff"
        };

        private readonly ObservableCollection<ScreenshotFileItem> _imageFiles = new();
        private readonly ObservableCollection<ScreenshotFileItem> _otherFiles = new();
        private CancellationTokenSource? _refreshTokenSource;

        public ScreenshotsPage()
        {
            InitializeComponent();
            ImageFilesGridView.ItemsSource = _imageFiles;
            OtherFilesListView.ItemsSource = _otherFiles;
            SettingsService.SaveFolderPathChanged += SettingsService_SaveFolderPathChanged;
            Unloaded += ScreenshotsPage_Unloaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = RefreshPageAsync();
        }

        private void SettingsService_SaveFolderPathChanged()
        {
            DispatcherQueue.TryEnqueue(() => _ = RefreshPageAsync());
        }

        private void ScreenshotsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _refreshTokenSource?.Cancel();
            _refreshTokenSource?.Dispose();
            _refreshTokenSource = null;
            SettingsService.SaveFolderPathChanged -= SettingsService_SaveFolderPathChanged;
            Unloaded -= ScreenshotsPage_Unloaded;
        }

        private async Task RefreshPageAsync()
        {
            var hasSaveFolder = SettingsService.TryGetEffectiveSaveFolderPath(out var folderPath);
            var hasHotkey = SettingsService.TryGetEffectiveHotkey(out var hotkey);
            _imageFiles.Clear();
            _otherFiles.Clear();
            GalleryScrollViewer.Visibility = Visibility.Collapsed;
            SaveFolderPathText.Text = hasSaveFolder
                ? folderPath
                : "No save folder selected.";

            if (!hasSaveFolder)
            {
                EmptyStateMessageText.Text = "Set a save location to enable screenshots.";
                EmptyFolderCallout.Visibility = Visibility.Visible;
                return;
            }

            if (!hasHotkey)
            {
                EmptyStateMessageText.Text = "Set a key-binding to enable screenshots.";
                EmptyFolderCallout.Visibility = Visibility.Visible;
                return;
            }

            if (!Directory.Exists(folderPath))
            {
                EmptyStateMessageText.Text = "Save folder is missing. Reconfigure it in Settings.";
                EmptyFolderCallout.Visibility = Visibility.Visible;
                return;
            }

            var allFiles = Directory.EnumerateFiles(folderPath)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();

            if (allFiles.Length == 0)
            {
                EmptyStateMessageText.Text = $"Press {hotkey.Display} to create your first screenshot.";
                EmptyFolderCallout.Visibility = Visibility.Visible;
                return;
            }

            _refreshTokenSource?.Cancel();
            _refreshTokenSource?.Dispose();
            _refreshTokenSource = new CancellationTokenSource();
            var token = _refreshTokenSource.Token;

            foreach (var file in allFiles)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (IsEditableImage(file.Extension))
                {
                    var imageItem = new ScreenshotFileItem(file.FullName, file.Name, BuildFileInfoText(file), true, "🖼");
                    _imageFiles.Add(imageItem);
                    _ = LoadThumbnailAsync(imageItem, token);
                }
                else
                {
                    _otherFiles.Add(new ScreenshotFileItem(file.FullName, file.Name, BuildFileInfoText(file), false, "📄"));
                }
            }

            EmptyFolderCallout.Visibility = Visibility.Collapsed;
            GalleryScrollViewer.Visibility = Visibility.Visible;
        }

        private async Task LoadThumbnailAsync(ScreenshotFileItem item, CancellationToken token)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(item.Path);
                var thumbnail = await TryLoadPreviewAsync(file, token);
                if (thumbnail is null || token.IsCancellationRequested)
                {
                    return;
                }

                item.Thumbnail = thumbnail;
            }
            catch
            {
                // Skip thumbnail if decode is not supported.
            }
        }

        private static async Task<BitmapImage?> TryLoadPreviewAsync(StorageFile file, CancellationToken token)
        {
            // Prefer direct decode for reliable previews in arbitrary folders.
            try
            {
                using var imageStream = await file.OpenReadAsync();
                if (token.IsCancellationRequested)
                {
                    return null;
                }

                var image = new BitmapImage
                {
                    DecodePixelWidth = 520
                };
                await image.SetSourceAsync(imageStream);
                return image;
            }
            catch
            {
                // Fallback to shell thumbnail provider when direct decode fails.
            }

            try
            {
                using var thumbnailStream = await file.GetThumbnailAsync(ThumbnailMode.PicturesView, 520, ThumbnailOptions.UseCurrentScale);
                if (thumbnailStream is null || thumbnailStream.Size == 0 || token.IsCancellationRequested)
                {
                    return null;
                }

                var thumbnail = new BitmapImage
                {
                    DecodePixelWidth = 520
                };
                await thumbnail.SetSourceAsync(thumbnailStream);
                return thumbnail;
            }
            catch
            {
                return null;
            }
        }

        private void ImageFilesGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not ScreenshotFileItem item || !item.IsEditableImage)
            {
                return;
            }

            Editor.ImageEditorLauncher.OpenEditor(item.Path);
        }

        private static bool IsEditableImage(string extension)
        {
            return EditableImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        private static string BuildFileInfoText(FileInfo file)
        {
            var extension = string.IsNullOrWhiteSpace(file.Extension)
                ? "No extension"
                : file.Extension.ToLowerInvariant();
            var sizeText = FormatBytes(file.Length);
            return $"{extension} • {sizeText} • {file.LastWriteTime:g}";
        }

        private static string FormatBytes(long value)
        {
            if (value < 1024)
            {
                return $"{value} B";
            }

            var sizes = new[] { "KB", "MB", "GB", "TB" };
            double length = value;
            var order = -1;
            while (length >= 1024 && order < sizes.Length - 1)
            {
                order++;
                length /= 1024;
            }

            return string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1}", length, sizes[order]);
        }
    }

    internal sealed class ScreenshotFileItem : DependencyObject
    {
        public ScreenshotFileItem(string path, string name, string fileInfoText, bool isEditableImage, string fileGlyph)
        {
            Path = path;
            Name = name;
            FileInfoText = fileInfoText;
            IsEditableImage = isEditableImage;
            FileGlyph = fileGlyph;
        }

        public string Path { get; }

        public string Name { get; }

        public string FileInfoText { get; }

        public bool IsEditableImage { get; }

        public string FileGlyph { get; }

        public BitmapImage? Thumbnail
        {
            get => (BitmapImage?)GetValue(ThumbnailProperty);
            set => SetValue(ThumbnailProperty, value);
        }

        public static readonly DependencyProperty ThumbnailProperty =
            DependencyProperty.Register(nameof(Thumbnail), typeof(BitmapImage), typeof(ScreenshotFileItem), new PropertyMetadata(null));
    }
}
