using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace helvety.screentools.Views
{
    public sealed partial class ScreenshotsPage : Page
    {
        // Core Windows-native decoders reliably cover these formats.
        private static readonly string[] CommonImageExtensions =
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"
        };

        // Optional codec-based formats (for example WebP extension from Store).
        private static readonly string[] OptionalImageExtensions =
        {
            ".webp"
        };

        private readonly ObservableCollection<ScreenshotFileItem> _imageFiles = new();
        private readonly ObservableCollection<ScreenshotFileItem> _otherFiles = new();
        private CancellationTokenSource? _refreshTokenSource;
        private FileSystemWatcher? _saveFolderWatcher;
        private string? _watchedFolderPath;
        private readonly DispatcherQueueTimer _watcherRefreshTimer;

        public ScreenshotsPage()
        {
            InitializeComponent();
            ImageFilesGridView.ItemsSource = _imageFiles;
            OtherFilesListView.ItemsSource = _otherFiles;
            _watcherRefreshTimer = DispatcherQueue.CreateTimer();
            _watcherRefreshTimer.Interval = TimeSpan.FromMilliseconds(250);
            _watcherRefreshTimer.IsRepeating = false;
            // Debounce clustered file-system notifications from screenshot writes.
            _watcherRefreshTimer.Tick += WatcherRefreshTimer_Tick;
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

        private void WatcherRefreshTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            _ = RefreshPageAsync();
        }

        private void ScreenshotsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _refreshTokenSource?.Cancel();
            _refreshTokenSource?.Dispose();
            _refreshTokenSource = null;
            DisposeSaveFolderWatcher();
            _watcherRefreshTimer.Stop();
            _watcherRefreshTimer.Tick -= WatcherRefreshTimer_Tick;
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
                DisposeSaveFolderWatcher();
                EmptyStateMessageText.Text = "Set a save location to enable screenshots.";
                EmptyFolderCallout.Visibility = Visibility.Visible;
                return;
            }

            if (!hasHotkey)
            {
                DisposeSaveFolderWatcher();
                EmptyStateMessageText.Text = "Set a key-binding to enable screenshots.";
                EmptyFolderCallout.Visibility = Visibility.Visible;
                return;
            }

            if (!Directory.Exists(folderPath))
            {
                DisposeSaveFolderWatcher();
                EmptyStateMessageText.Text = "Save folder is missing. Reconfigure it in Settings.";
                EmptyFolderCallout.Visibility = Visibility.Visible;
                return;
            }

            EnsureSaveFolderWatcher(folderPath);
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

                await TryAssignThumbnailOnUiThreadAsync(item, thumbnail, token);
            }
            catch (OperationCanceledException)
            {
                // Refresh was canceled; ignore.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ScreenshotsPage] Thumbnail load failed for '{item.Path}': {ex.Message}");
            }
        }

        private Task TryAssignThumbnailOnUiThreadAsync(ScreenshotFileItem item, BitmapImage thumbnail, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource<object?>();
            var queued = DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (!token.IsCancellationRequested)
                    {
                        item.Thumbnail = thumbnail;
                    }

                    completion.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });

            if (!queued)
            {
                Debug.WriteLine($"[ScreenshotsPage] DispatcherQueue enqueue failed for '{item.Path}'.");
                completion.TrySetResult(null);
            }

            return completion.Task;
        }

        private static void LogPreviewFailure(string stage, StorageFile file, Exception ex)
        {
            Debug.WriteLine($"[ScreenshotsPage] {stage} preview failed for '{file.Path}': {ex.Message}");
        }

        private static void LogPreviewMiss(string stage, StorageFile file)
        {
            Debug.WriteLine($"[ScreenshotsPage] {stage} preview unavailable for '{file.Path}'.");
        }

        private static bool IsFileLocked(IOException ex)
        {
            const int sharingViolation = unchecked((int)0x80070020);
            const int lockViolation = unchecked((int)0x80070021);
            return ex.HResult == sharingViolation || ex.HResult == lockViolation;
        }

        private static async Task<T?> RetryOnceAsync<T>(Func<Task<T>> action) where T : class
        {
            try
            {
                return await action();
            }
            catch (IOException ioEx) when (IsFileLocked(ioEx))
            {
                await Task.Delay(80);
                return await action();
            }
            catch
            {
                return null;
            }
        }

        private static async Task<BitmapImage?> TryLoadPreviewAsync(StorageFile file, CancellationToken token)
        {
            // Prefer direct decode for reliable previews in arbitrary folders.
            try
            {
                var imageStream = await RetryOnceAsync(() => file.OpenReadAsync().AsTask());
                if (imageStream is null)
                {
                    LogPreviewMiss("Direct decode", file);
                    return null;
                }

                using (imageStream)
                {
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
            }
            catch (Exception ex)
            {
                LogPreviewFailure("Direct decode", file, ex);
            }

            try
            {
                var thumbnailStream = await RetryOnceAsync(() =>
                    file.GetThumbnailAsync(ThumbnailMode.PicturesView, 520, ThumbnailOptions.UseCurrentScale).AsTask());

                if (thumbnailStream is null)
                {
                    LogPreviewMiss("Shell thumbnail", file);
                    return null;
                }

                using (thumbnailStream)
                {
                    if (thumbnailStream.Size == 0 || token.IsCancellationRequested)
                    {
                        LogPreviewMiss("Shell thumbnail", file);
                        return null;
                    }

                    var thumbnail = new BitmapImage
                    {
                        DecodePixelWidth = 520
                    };
                    await thumbnail.SetSourceAsync(thumbnailStream);
                    return thumbnail;
                }
            }
            catch (Exception ex)
            {
                LogPreviewFailure("Shell thumbnail", file, ex);
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
            return CommonImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
                || OptionalImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        private void EnsureSaveFolderWatcher(string folderPath)
        {
            if (_saveFolderWatcher is not null
                && string.Equals(_watchedFolderPath, folderPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DisposeSaveFolderWatcher();
            try
            {
                _saveFolderWatcher = new FileSystemWatcher(folderPath)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size
                };
                _saveFolderWatcher.Created += SaveFolderWatcher_Changed;
                _saveFolderWatcher.Changed += SaveFolderWatcher_Changed;
                _saveFolderWatcher.Deleted += SaveFolderWatcher_Changed;
                _saveFolderWatcher.Renamed += SaveFolderWatcher_Renamed;
                _saveFolderWatcher.EnableRaisingEvents = true;
                _watchedFolderPath = folderPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ScreenshotsPage] Failed to watch save folder '{folderPath}': {ex.Message}");
                DisposeSaveFolderWatcher();
            }
        }

        private void DisposeSaveFolderWatcher()
        {
            if (_saveFolderWatcher is not null)
            {
                _saveFolderWatcher.Created -= SaveFolderWatcher_Changed;
                _saveFolderWatcher.Changed -= SaveFolderWatcher_Changed;
                _saveFolderWatcher.Deleted -= SaveFolderWatcher_Changed;
                _saveFolderWatcher.Renamed -= SaveFolderWatcher_Renamed;
                _saveFolderWatcher.Dispose();
                _saveFolderWatcher = null;
            }

            _watchedFolderPath = null;
        }

        private void SaveFolderWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            QueueRefreshFromWatcherEvent(e.FullPath);
        }

        private void SaveFolderWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            QueueRefreshFromWatcherEvent(e.OldFullPath);
            QueueRefreshFromWatcherEvent(e.FullPath);
        }

        private void QueueRefreshFromWatcherEvent(string fullPath)
        {
            var extension = Path.GetExtension(fullPath);
            if (string.IsNullOrWhiteSpace(extension) || !IsEditableImage(extension))
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                _watcherRefreshTimer.Stop();
                _watcherRefreshTimer.Start();
            });
        }

        private static string BuildFileInfoText(FileInfo file)
        {
            var extension = string.IsNullOrWhiteSpace(file.Extension)
                ? "No extension"
                : file.Extension.ToLowerInvariant();
            var sizeText = FormatBytes(file.Length);
            var dateText = file.LastWriteTime.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            var relativeText = FormatRelativeAgo(file.LastWriteTime);
            return $"{extension} • {sizeText} • {dateText} - {relativeText} ago";
        }

        private static string FormatRelativeAgo(DateTime timestamp)
        {
            var elapsed = DateTime.Now - timestamp;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            if (elapsed.TotalSeconds < 60)
            {
                var seconds = Math.Max(1, (int)Math.Floor(elapsed.TotalSeconds));
                return $"{seconds} second{(seconds == 1 ? string.Empty : "s")}";
            }

            if (elapsed.TotalMinutes < 60)
            {
                var minutes = Math.Max(1, (int)Math.Floor(elapsed.TotalMinutes));
                return $"{minutes} minute{(minutes == 1 ? string.Empty : "s")}";
            }

            if (elapsed.TotalHours < 24)
            {
                var hours = Math.Max(1, (int)Math.Floor(elapsed.TotalHours));
                return $"{hours} hour{(hours == 1 ? string.Empty : "s")}";
            }

            var days = Math.Max(1, (int)Math.Floor(elapsed.TotalDays));
            return $"{days} day{(days == 1 ? string.Empty : "s")}";
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

    internal sealed class ScreenshotFileItem : INotifyPropertyChanged
    {
        private ImageSource? _thumbnail;

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

        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                if (ReferenceEquals(_thumbnail, value))
                {
                    return;
                }

                _thumbnail = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
