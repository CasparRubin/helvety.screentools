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
using helvety.screentools.Capture;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace helvety.screentools.Views
{
    public sealed partial class ScreenToolsPage : Page
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

        private readonly ObservableCollection<GalleryFileItem> _imageFiles = new();
        private readonly ObservableCollection<GalleryFileItem> _otherFiles = new();
        private CancellationTokenSource? _refreshTokenSource;
        private FileSystemWatcher? _saveFolderWatcher;
        private string? _watchedFolderPath;
        private readonly DispatcherQueueTimer _watcherRefreshTimer;

        public ScreenToolsPage()
        {
            InitializeComponent();
            ImageFilesGridView.ItemsSource = _imageFiles;
            OtherFilesListView.ItemsSource = _otherFiles;
            _watcherRefreshTimer = DispatcherQueue.CreateTimer();
            _watcherRefreshTimer.Interval = TimeSpan.FromMilliseconds(120);
            _watcherRefreshTimer.IsRepeating = false;
            // Debounce clustered file-system notifications from capture writes.
            _watcherRefreshTimer.Tick += WatcherRefreshTimer_Tick;
            SettingsService.SaveFolderPathChanged += SettingsService_SaveFolderPathChanged;
            SettingsService.SettingsChanged += SettingsService_SettingsChanged;
            Unloaded += ScreenToolsPage_Unloaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            CaptureGalleryNotifier.CaptureSavedToPath -= CaptureGalleryNotifier_CaptureSavedToPath;
            CaptureGalleryNotifier.CaptureSavedToPath += CaptureGalleryNotifier_CaptureSavedToPath;
            base.OnNavigatedTo(e);
            _ = RefreshPageAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            CaptureGalleryNotifier.CaptureSavedToPath -= CaptureGalleryNotifier_CaptureSavedToPath;
            base.OnNavigatedFrom(e);
        }

        private void SettingsService_SaveFolderPathChanged()
        {
            DispatcherQueue.TryEnqueue(() => _ = RefreshPageAsync());
        }

        private void SettingsService_SettingsChanged()
        {
            DispatcherQueue.TryEnqueue(() => _ = RefreshPageAsync());
        }

        private void WatcherRefreshTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            _ = RefreshPageAsync();
        }

        private void CaptureGalleryNotifier_CaptureSavedToPath(string path)
        {
            _ = ApplyImmediateCaptureSavedAsync(path);
        }

        private async Task ApplyImmediateCaptureSavedAsync(string path)
        {
            if (!SettingsService.TryGetEffectiveSaveFolderPath(out var folderPath))
            {
                return;
            }

            if (!string.Equals(Path.GetDirectoryName(path), folderPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!IsEditableImage(Path.GetExtension(path)))
            {
                return;
            }

            for (var attempt = 0; attempt < 25; attempt++)
            {
                if (File.Exists(path))
                {
                    break;
                }

                await Task.Delay(20).ConfigureAwait(true);
            }

            if (!File.Exists(path))
            {
                return;
            }

            FileInfo file;
            try
            {
                file = new FileInfo(path);
            }
            catch
            {
                return;
            }

            if (_imageFiles.Count > 0 && string.Equals(_imageFiles[0].Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            for (var i = _imageFiles.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_imageFiles[i].Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    _imageFiles.RemoveAt(i);
                }
            }

            var item = new GalleryFileItem(
                file.FullName,
                file.Name,
                BuildFileInfoText(file),
                true,
                "🖼",
                file.LastWriteTimeUtc.Ticks,
                file.Length);
            _imageFiles.Insert(0, item);
            EmptyFolderCallout.Visibility = Visibility.Collapsed;
            GalleryScrollViewer.Visibility = Visibility.Visible;

            if (SettingsService.TryGetEffectiveHotkey(out var hk))
            {
                var hasLive = SettingsService.TryGetEffectiveLiveDrawHotkey(out var live);
                HotkeysHintText.Text = hasLive
                    ? $"Capture: {hk.Display} · Live Draw: {live.Display}"
                    : $"Capture: {hk.Display}";
                HotkeysHintText.Visibility = Visibility.Visible;
            }

            SaveFolderPathText.Text = folderPath;
            _ = LoadThumbnailAsync(item, CancellationToken.None);
        }

        private static bool GalleryItemMatchesFile(GalleryFileItem item, FileInfo file)
        {
            return item.LastWriteTimeUtcTicks == file.LastWriteTimeUtc.Ticks && item.FileLengthBytes == file.Length;
        }

        private void ScreenToolsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _refreshTokenSource?.Cancel();
            _refreshTokenSource?.Dispose();
            _refreshTokenSource = null;
            DisposeSaveFolderWatcher();
            _watcherRefreshTimer.Stop();
            _watcherRefreshTimer.Tick -= WatcherRefreshTimer_Tick;
            SettingsService.SaveFolderPathChanged -= SettingsService_SaveFolderPathChanged;
            SettingsService.SettingsChanged -= SettingsService_SettingsChanged;
            Unloaded -= ScreenToolsPage_Unloaded;
        }

        private async Task RefreshPageAsync()
        {
            var hasSaveFolder = SettingsService.TryGetEffectiveSaveFolderPath(out var folderPath);
            var hasHotkey = SettingsService.TryGetEffectiveHotkey(out var hotkey);
            var hasLiveDrawHotkey = SettingsService.TryGetEffectiveLiveDrawHotkey(out var liveHotkey);
            SaveFolderPathText.Text = hasSaveFolder
                ? folderPath
                : "No save folder selected.";

            HotkeysHintText.Visibility = Visibility.Collapsed;
            HotkeysHintText.Text = string.Empty;

            if (!hasSaveFolder)
            {
                _imageFiles.Clear();
                _otherFiles.Clear();
                DisposeSaveFolderWatcher();
                GalleryScrollViewer.Visibility = Visibility.Collapsed;
                EmptyStateMessageText.Text = "Set a save location to store captures.";
                EmptyFolderCallout.Visibility = Visibility.Visible;
                return;
            }

            if (!hasHotkey)
            {
                _imageFiles.Clear();
                _otherFiles.Clear();
                DisposeSaveFolderWatcher();
                GalleryScrollViewer.Visibility = Visibility.Collapsed;
                var liveHint = hasLiveDrawHotkey
                    ? $" You can still use Live Draw with {liveHotkey.Display} (no save folder required for that hotkey)."
                    : string.Empty;
                EmptyStateMessageText.Text = $"Set a capture hotkey to save files to this folder.{liveHint}";
                EmptyFolderCallout.Visibility = Visibility.Visible;
                return;
            }

            if (!Directory.Exists(folderPath))
            {
                _imageFiles.Clear();
                _otherFiles.Clear();
                DisposeSaveFolderWatcher();
                GalleryScrollViewer.Visibility = Visibility.Collapsed;
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
                _imageFiles.Clear();
                _otherFiles.Clear();
                GalleryScrollViewer.Visibility = Visibility.Collapsed;
                var liveLine = hasLiveDrawHotkey
                    ? $" Live Draw: {liveHotkey.Display}."
                    : string.Empty;
                EmptyStateMessageText.Text = $"Press {hotkey.Display} to create your first capture.{liveLine}";
                EmptyFolderCallout.Visibility = Visibility.Visible;
                return;
            }

            var previousImagesByPath = _imageFiles.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
            var previousOthersByPath = _otherFiles.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
            _imageFiles.Clear();
            _otherFiles.Clear();

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
                    if (previousImagesByPath.TryGetValue(file.FullName, out var reusedImage) && GalleryItemMatchesFile(reusedImage, file))
                    {
                        _imageFiles.Add(reusedImage);
                    }
                    else
                    {
                        var imageItem = new GalleryFileItem(
                            file.FullName,
                            file.Name,
                            BuildFileInfoText(file),
                            true,
                            "🖼",
                            file.LastWriteTimeUtc.Ticks,
                            file.Length);
                        _imageFiles.Add(imageItem);
                        _ = LoadThumbnailAsync(imageItem, token);
                    }
                }
                else
                {
                    if (previousOthersByPath.TryGetValue(file.FullName, out var reusedOther) && GalleryItemMatchesFile(reusedOther, file))
                    {
                        _otherFiles.Add(reusedOther);
                    }
                    else
                    {
                        _otherFiles.Add(new GalleryFileItem(
                            file.FullName,
                            file.Name,
                            BuildFileInfoText(file),
                            false,
                            "📄",
                            file.LastWriteTimeUtc.Ticks,
                            file.Length));
                    }
                }
            }

            EmptyFolderCallout.Visibility = Visibility.Collapsed;
            GalleryScrollViewer.Visibility = Visibility.Visible;

            HotkeysHintText.Text = hasLiveDrawHotkey
                ? $"Capture: {hotkey.Display} · Live Draw: {liveHotkey.Display}"
                : $"Capture: {hotkey.Display}";
            HotkeysHintText.Visibility = Visibility.Visible;
        }

        private async Task LoadThumbnailAsync(GalleryFileItem item, CancellationToken token)
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
                Debug.WriteLine($"[ScreenToolsPage] Thumbnail load failed for '{item.Path}': {ex.Message}");
            }
        }

        private Task TryAssignThumbnailOnUiThreadAsync(GalleryFileItem item, BitmapImage thumbnail, CancellationToken token)
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
                Debug.WriteLine($"[ScreenToolsPage] DispatcherQueue enqueue failed for '{item.Path}'.");
                completion.TrySetResult(null);
            }

            return completion.Task;
        }

        private static void LogPreviewFailure(string stage, StorageFile file, Exception ex)
        {
            Debug.WriteLine($"[ScreenToolsPage] {stage} preview failed for '{file.Path}': {ex.Message}");
        }

        private static void LogPreviewMiss(string stage, StorageFile file)
        {
            Debug.WriteLine($"[ScreenToolsPage] {stage} preview unavailable for '{file.Path}'.");
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
            if (e.ClickedItem is not GalleryFileItem item || !item.IsEditableImage)
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
                Debug.WriteLine($"[ScreenToolsPage] Failed to watch save folder '{folderPath}': {ex.Message}");
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

    internal sealed class GalleryFileItem : INotifyPropertyChanged
    {
        private ImageSource? _thumbnail;

        public GalleryFileItem(
            string path,
            string name,
            string fileInfoText,
            bool isEditableImage,
            string fileGlyph,
            long lastWriteTimeUtcTicks,
            long fileLengthBytes)
        {
            Path = path;
            Name = name;
            FileInfoText = fileInfoText;
            IsEditableImage = isEditableImage;
            FileGlyph = fileGlyph;
            LastWriteTimeUtcTicks = lastWriteTimeUtcTicks;
            FileLengthBytes = fileLengthBytes;
        }

        public string Path { get; }

        public string Name { get; }

        public string FileInfoText { get; }

        public bool IsEditableImage { get; }

        public string FileGlyph { get; }

        public long LastWriteTimeUtcTicks { get; }

        public long FileLengthBytes { get; }

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
