using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using helvety.screentools;
using helvety.screentools.Capture;
using helvety.screentools.Editor;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Canvas = Microsoft.UI.Xaml.Controls.Canvas;
using Image = Microsoft.UI.Xaml.Controls.Image;

namespace helvety.screentools.Views
{
    /// <summary>
    /// Home page (nav label "General", page title "Helvety Screen Tools"): lists files from the configured save folder with thumbnails and metadata.
    /// Left-click on an image opens the editor; right-click copies that image to the clipboard.
    /// Refreshes enumerate the folder on a background thread, then apply updates under a short lock. After a capture, the list reloads the same way as on navigation.
    /// The page intentionally stays minimal and focuses on browsing saved files.
    /// </summary>
    public sealed partial class ScreenToolsPage : Page
    {
        private static readonly string[] EditableImageExtensions =
        {
            ".png"
        };

        private readonly ObservableCollection<GalleryFileItem> _imageFiles = new();
        /// <summary>Cancellation only on page unload so in-flight thumbnail loads are not dropped when the gallery refreshes.</summary>
        private CancellationTokenSource? _thumbnailLoadLifetimeCts;
        /// <summary>Serializes gallery updates so <see cref="_imageFiles"/> is not modified concurrently.</summary>
        private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);

        /// <summary>Limits concurrent UI-thread thumbnail composites (editable PNG + vectors).</summary>
        private static readonly SemaphoreSlim ThumbnailCompositeSemaphore = new(2, 2);
        private FileSystemWatcher? _saveFolderWatcher;
        private string? _watchedFolderPath;
        private readonly DispatcherQueueTimer _watcherRefreshTimer;

        public ScreenToolsPage()
        {
            InitializeComponent();
            ImageFilesGridView.ItemsSource = _imageFiles;
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

            DispatcherQueue.TryEnqueue(() => _ = ReloadGalleryAfterCaptureAsync());
        }

        private async Task ReloadGalleryAfterCaptureAsync()
        {
            try
            {
                await Task.Delay(80).ConfigureAwait(true);
                await RefreshPageAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ScreenToolsPage] Post-capture gallery reload failed: {ex.Message}");
            }
        }

        private void EnsureThumbnailLoadLifetime()
        {
            if (_thumbnailLoadLifetimeCts is null)
            {
                _thumbnailLoadLifetimeCts = new CancellationTokenSource();
            }
        }

        private static bool GalleryItemMatchesFile(GalleryFileItem item, FileInfo file)
        {
            return item.LastWriteTimeUtcTicks == file.LastWriteTimeUtc.Ticks && item.FileLengthBytes == file.Length;
        }

        private void ScreenToolsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _thumbnailLoadLifetimeCts?.Cancel();
            _thumbnailLoadLifetimeCts?.Dispose();
            _thumbnailLoadLifetimeCts = null;
            DisposeSaveFolderWatcher();
            _watcherRefreshTimer.Stop();
            _watcherRefreshTimer.Tick -= WatcherRefreshTimer_Tick;
            SettingsService.SaveFolderPathChanged -= SettingsService_SaveFolderPathChanged;
            SettingsService.SettingsChanged -= SettingsService_SettingsChanged;
            Unloaded -= ScreenToolsPage_Unloaded;
        }

        private async Task RefreshPageAsync()
        {
            try
            {
                var plan = await BuildGalleryRefreshPlanAsync().ConfigureAwait(true);
                await _refreshSemaphore.WaitAsync().ConfigureAwait(true);
                try
                {
                    ApplyGalleryRefreshPlan(plan);
                }
                finally
                {
                    _refreshSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                // Fire-and-forget refreshes are triggered from navigation and timers; make sure refresh exceptions
                // never crash the app (IO issues like permissions/unavailable folders are expected).
                Debug.WriteLine($"[ScreenToolsPage] Gallery refresh failed: {ex}");
            }
        }

        private async Task<GalleryRefreshPlan> BuildGalleryRefreshPlanAsync()
        {
            var hasSaveFolder = SettingsService.TryGetEffectiveSaveFolderPath(out var folderPath);
            var hasHotkey = SettingsService.TryGetEffectiveHotkey(out var hotkey);
            var hasLiveDrawHotkey = SettingsService.TryGetEffectiveLiveDrawHotkey(out var liveHotkey);
            var emptyHotkey = new HotkeySettings(0, Array.Empty<uint>(), string.Empty);

            if (!hasSaveFolder)
            {
                return new GalleryRefreshPlan(GalleryRefreshKind.NoSaveFolder, folderPath, emptyHotkey, hasLiveDrawHotkey, liveHotkey, null);
            }

            if (!hasHotkey)
            {
                return new GalleryRefreshPlan(GalleryRefreshKind.NoHotkey, folderPath, hotkey, hasLiveDrawHotkey, liveHotkey, null);
            }

            if (!Directory.Exists(folderPath))
            {
                return new GalleryRefreshPlan(GalleryRefreshKind.FolderMissing, folderPath, hotkey, hasLiveDrawHotkey, liveHotkey, null);
            }

            var allFiles = await Task.Run(() => Directory.EnumerateFiles(folderPath)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray()).ConfigureAwait(false);

            if (allFiles.Length == 0)
            {
                return new GalleryRefreshPlan(GalleryRefreshKind.EmptyFolder, folderPath, hotkey, hasLiveDrawHotkey, liveHotkey, allFiles);
            }

            return new GalleryRefreshPlan(GalleryRefreshKind.Populated, folderPath, hotkey, hasLiveDrawHotkey, liveHotkey, allFiles);
        }

        private void ApplyGalleryRefreshPlan(GalleryRefreshPlan plan)
        {
            var captureBindingText = SettingsService.TryGetEffectiveHotkey(out var captureHotkey) &&
                                     !string.IsNullOrWhiteSpace(captureHotkey.Display)
                ? captureHotkey.Display
                : "not set";
            var liveDrawBindingText = SettingsService.TryGetEffectiveLiveDrawHotkey(out var liveDrawHotkey) &&
                                      !string.IsNullOrWhiteSpace(liveDrawHotkey.Display)
                ? liveDrawHotkey.Display
                : "not set";
            AppHeroDescriptionText.Text =
                $"Capture your screen with smart snap selection ({captureBindingText}), draw live on your desktop in fullscreen ({liveDrawBindingText}), and edit saved images with crop, blur, highlight, text, borders, and arrows in the built-in editor.";

            if (plan.Kind == GalleryRefreshKind.NoSaveFolder)
            {
                _imageFiles.Clear();
                DisposeSaveFolderWatcher();
                GalleryScrollViewer.Visibility = Visibility.Collapsed;
                EmptyStateMessageText.Text = "Set a save location to store captures.";
                EmptyFolderCallout.Visibility = Visibility.Visible;
                return;
            }

            if (plan.Kind == GalleryRefreshKind.NoHotkey)
            {
                _imageFiles.Clear();
                DisposeSaveFolderWatcher();
                GalleryScrollViewer.Visibility = Visibility.Collapsed;
                var liveHint = plan.HasLiveDrawHotkey
                    ? $" You can still use Live Draw with {plan.LiveHotkey.Display} (no save folder required for that hotkey)."
                    : string.Empty;
                EmptyStateMessageText.Text = $"Set a capture hotkey to save files to this folder.{liveHint}";
                EmptyFolderCallout.Visibility = Visibility.Visible;
                return;
            }

            if (plan.Kind == GalleryRefreshKind.FolderMissing)
            {
                _imageFiles.Clear();
                DisposeSaveFolderWatcher();
                GalleryScrollViewer.Visibility = Visibility.Collapsed;
                EmptyStateMessageText.Text = "Save folder is missing. Reconfigure it in Settings.";
                EmptyFolderCallout.Visibility = Visibility.Visible;
                return;
            }

            EnsureSaveFolderWatcher(plan.FolderPath!);
            var allFiles = plan.Files ?? Array.Empty<FileInfo>();

            if (plan.Kind == GalleryRefreshKind.EmptyFolder || allFiles.Length == 0)
            {
                _imageFiles.Clear();
                GalleryScrollViewer.Visibility = Visibility.Collapsed;
                var liveLine = plan.HasLiveDrawHotkey
                    ? $" Live Draw: {plan.LiveHotkey.Display}."
                    : string.Empty;
                EmptyStateMessageText.Text = $"Press {plan.Hotkey.Display} to create your first capture.{liveLine}";
                EmptyFolderCallout.Visibility = Visibility.Visible;
                return;
            }

            var previousImagesByPath = _imageFiles.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
            _imageFiles.Clear();

            EnsureThumbnailLoadLifetime();
            var token = _thumbnailLoadLifetimeCts!.Token;

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
                            BuildImageGalleryDateLine(file),
                            BuildImageGallerySizeLine(file),
                            true,
                            "🖼",
                            file.LastWriteTimeUtc.Ticks,
                            file.Length);
                        _imageFiles.Add(imageItem);
                        _ = LoadThumbnailAsync(imageItem, token);
                    }
                }
            }

            EmptyFolderCallout.Visibility = Visibility.Collapsed;
            GalleryScrollViewer.Visibility = Visibility.Visible;

        }

        private enum GalleryRefreshKind
        {
            NoSaveFolder,
            NoHotkey,
            FolderMissing,
            EmptyFolder,
            Populated
        }

        private readonly struct GalleryRefreshPlan
        {
            public GalleryRefreshPlan(
                GalleryRefreshKind kind,
                string? folderPath,
                HotkeySettings hotkey,
                bool hasLiveDrawHotkey,
                HotkeySettings liveHotkey,
                FileInfo[]? files)
            {
                Kind = kind;
                FolderPath = folderPath;
                Hotkey = hotkey;
                HasLiveDrawHotkey = hasLiveDrawHotkey;
                LiveHotkey = liveHotkey;
                Files = files;
            }

            public GalleryRefreshKind Kind { get; }
            public string? FolderPath { get; }
            public HotkeySettings Hotkey { get; }
            public bool HasLiveDrawHotkey { get; }
            public HotkeySettings LiveHotkey { get; }
            public FileInfo[]? Files { get; }
        }

        private async Task LoadThumbnailAsync(GalleryFileItem item, CancellationToken token)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(item.Path);
                ImageSource? thumbnail;
                if (string.Equals(Path.GetExtension(item.Path), ".png", StringComparison.OrdinalIgnoreCase))
                {
                    thumbnail = await TryLoadEditablePngThumbnailWithVectorsAsync(file, token).ConfigureAwait(false);
                    if (thumbnail is not null && !token.IsCancellationRequested)
                    {
                        await TryAssignThumbnailOnUiThreadAsync(item, thumbnail, token);
                        return;
                    }
                }

                thumbnail = await TryLoadPreviewAsync(file, token).ConfigureAwait(false);
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

        private Task TryAssignThumbnailOnUiThreadAsync(GalleryFileItem item, ImageSource thumbnail, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            var path = item.Path;
            var completion = new TaskCompletionSource<object?>();
            var queued = DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (token.IsCancellationRequested)
                    {
                        completion.TrySetResult(null);
                        return;
                    }

                    var target = FindGalleryImageItemByPath(path);
                    if (target is not null)
                    {
                        target.Thumbnail = thumbnail;
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
                Debug.WriteLine($"[ScreenToolsPage] DispatcherQueue enqueue failed for '{path}'.");
                completion.TrySetResult(null);
            }

            return completion.Task;
        }

        private GalleryFileItem? FindGalleryImageItemByPath(string path)
        {
            foreach (var entry in _imageFiles)
            {
                if (string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
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

        private async Task<ImageSource?> TryLoadEditablePngThumbnailWithVectorsAsync(StorageFile file, CancellationToken token)
        {
            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(file.Path, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ScreenToolsPage] PNG read for thumbnail composite failed: {ex.Message}");
                return null;
            }

            if (token.IsCancellationRequested)
            {
                return null;
            }

            using var ras = new InMemoryRandomAccessStream();
            await ras.WriteAsync(bytes.AsBuffer());
            ras.Seek(0);
            BitmapDecoder decoder;
            try
            {
                decoder = await BitmapDecoder.CreateAsync(ras);
            }
            catch
            {
                return null;
            }

            var pw = (int)decoder.OrientedPixelWidth;
            var ph = (int)decoder.OrientedPixelHeight;
            if (!GalleryEditablePngThumbnailComposer.TryGetThumbnailCompositePlan(
                    bytes,
                    file.Path,
                    pw,
                    ph,
                    out var document,
                    out _,
                    out var scale,
                    out var sw,
                    out var sh))
            {
                return null;
            }

            var scaledPixels = await TryDecodeScaledPngBgraAsync(bytes, sw, sh, token).ConfigureAwait(false);
            if (scaledPixels is null || token.IsCancellationRequested)
            {
                return null;
            }

            var layers = GalleryEditablePngThumbnailComposer.ScaleVectorLayersInDrawOrder(document.Layers, scale);

            await ThumbnailCompositeSemaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await ComposeEditableThumbnailOnUiThreadAsync(scaledPixels, sw, sh, layers, token).ConfigureAwait(false);
            }
            finally
            {
                ThumbnailCompositeSemaphore.Release();
            }
        }

        private static async Task<byte[]?> TryDecodeScaledPngBgraAsync(byte[] pngBytes, int scaledWidth, int scaledHeight, CancellationToken token)
        {
            using var ras = new InMemoryRandomAccessStream();
            await ras.WriteAsync(pngBytes.AsBuffer());
            ras.Seek(0);
            BitmapDecoder decoder;
            try
            {
                decoder = await BitmapDecoder.CreateAsync(ras);
            }
            catch
            {
                return null;
            }

            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, scaledWidth),
                ScaledHeight = (uint)Math.Max(1, scaledHeight),
                InterpolationMode = BitmapInterpolationMode.Fant
            };

            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            if (token.IsCancellationRequested)
            {
                return null;
            }

            return pixelData.DetachPixelData();
        }

        private Task<ImageSource?> ComposeEditableThumbnailOnUiThreadAsync(
            byte[] scaledBgra,
            int sw,
            int sh,
            IReadOnlyList<EditorLayer> layersBottomToTop,
            CancellationToken token)
        {
            var tcs = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!DispatcherQueue.TryEnqueue(() => _ = ComposeEditableThumbnailCoreAsync(tcs, scaledBgra, sw, sh, layersBottomToTop, token)))
            {
                tcs.TrySetResult(null);
            }

            return tcs.Task;
        }

        private async Task ComposeEditableThumbnailCoreAsync(
            TaskCompletionSource<ImageSource?> tcs,
            byte[] scaledBgra,
            int sw,
            int sh,
            IReadOnlyList<EditorLayer> layersBottomToTop,
            CancellationToken token)
        {
            try
            {
                if (token.IsCancellationRequested)
                {
                    tcs.TrySetResult(null);
                    return;
                }

                var expectedLength = checked(sw * sh * 4);
                if (scaledBgra.Length < expectedLength)
                {
                    tcs.TrySetResult(null);
                    return;
                }

                ThumbnailCompositionHost.Children.Clear();

                var surface = new Grid
                {
                    Width = sw,
                    Height = sh
                };

                var writeable = new WriteableBitmap(sw, sh);
                using (var stream = writeable.PixelBuffer.AsStream())
                {
                    await stream.WriteAsync(scaledBgra, 0, expectedLength).ConfigureAwait(true);
                }

                writeable.Invalidate();

                var baseImage = new Image
                {
                    Source = writeable,
                    Width = sw,
                    Height = sh,
                    Stretch = Stretch.None
                };

                var overlay = new Canvas
                {
                    Width = sw,
                    Height = sh,
                    IsHitTestVisible = false
                };

                surface.Children.Add(baseImage);
                surface.Children.Add(overlay);

                ThumbnailCompositionHost.Children.Add(surface);

                EditorVectorOverlayRenderer.DrawVectorLayersBottomToTop(layersBottomToTop, overlay, suppressExpensiveEffects: true);

                ThumbnailCompositionHost.Measure(new Size(sw, sh));
                ThumbnailCompositionHost.Arrange(new Rect(0, 0, sw, sh));
                surface.Measure(new Size(sw, sh));
                surface.Arrange(new Rect(0, 0, sw, sh));
                ThumbnailCompositionHost.UpdateLayout();

                var rtb = new RenderTargetBitmap();
                await rtb.RenderAsync(surface, sw, sh);
                var buffer = await rtb.GetPixelsAsync();

                ThumbnailCompositionHost.Children.Clear();

                using var outputBitmap = SoftwareBitmap.CreateCopyFromBuffer(
                    buffer,
                    BitmapPixelFormat.Bgra8,
                    sw,
                    sh,
                    BitmapAlphaMode.Premultiplied);

                var source = new SoftwareBitmapSource();
                await source.SetBitmapAsync(outputBitmap);
                tcs.TrySetResult(source);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ScreenToolsPage] Thumbnail vector composite failed: {ex.Message}");
                ThumbnailCompositionHost.Children.Clear();
                tcs.TrySetResult(null);
            }
        }

        /// <summary>Decode order: scaled PNG from disk, BitmapImage decode, shell thumbnail.</summary>
        private static async Task<ImageSource?> TryLoadPreviewAsync(StorageFile file, CancellationToken token)
        {
            if (string.Equals(Path.GetExtension(file.Path), ".png", StringComparison.OrdinalIgnoreCase))
            {
                var scaled = await TryLoadScaledPngPreviewFromFileAsync(file, token).ConfigureAwait(false);
                if (scaled is not null)
                {
                    return scaled;
                }
            }

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

        private static async Task<ImageSource?> TryLoadScaledPngPreviewFromFileAsync(StorageFile file, CancellationToken token)
        {
            IRandomAccessStream? stream = null;
            try
            {
                stream = await RetryOnceAsync(() => file.OpenReadAsync().AsTask());
                if (stream is null)
                {
                    return null;
                }

                var decoder = await BitmapDecoder.CreateAsync(stream);
                return await DecodeBitmapDecoderToSoftwareBitmapSourceAsync(decoder, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogPreviewFailure("Scaled PNG decode", file, ex);
                return null;
            }
            finally
            {
                stream?.Dispose();
            }
        }

        private static async Task<SoftwareBitmapSource?> DecodeBitmapDecoderToSoftwareBitmapSourceAsync(BitmapDecoder decoder, CancellationToken token)
        {
            var transform = new BitmapTransform();
            var w = decoder.OrientedPixelWidth;
            var h = decoder.OrientedPixelHeight;
            const uint maxW = 520;
            if (w > maxW)
            {
                var ratio = (double)maxW / w;
                transform.ScaledWidth = maxW;
                transform.ScaledHeight = (uint)Math.Max(1, Math.Round(h * ratio));
            }

            var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            if (token.IsCancellationRequested)
            {
                return null;
            }

            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(softwareBitmap);
            return source;
        }

        private void ImageFilesGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not GalleryFileItem item || !item.IsEditableImage)
            {
                return;
            }

            Editor.ImageEditorLauncher.OpenEditor(item.Path);
        }

        private async void ImageFilesGridView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (FindGalleryFileItemFromEventSource(e.OriginalSource) is not { } item || !item.IsEditableImage)
            {
                return;
            }

            e.Handled = true;
            await CopyGalleryImageToClipboardAsync(item.Path);
        }

        private static GalleryFileItem? FindGalleryFileItemFromEventSource(object? source)
        {
            for (var current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is GridViewItem container && container.Content is GalleryFileItem item)
                {
                    return item;
                }
            }

            return null;
        }

        private static async Task CopyGalleryImageToClipboardAsync(string path)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                var buffer = await FileIO.ReadBufferAsync(file);
                var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(buffer);
                stream.Seek(0);

                var dataPackage = new DataPackage
                {
                    RequestedOperation = DataPackageOperation.Copy
                };
                dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                Clipboard.SetContent(dataPackage);
                Clipboard.Flush();

                InAppToastService.Show("Copied image to clipboard.", InAppToastSeverity.Success);
            }
            catch (Exception ex)
            {
                InAppToastService.Show($"Copy failed ({ex.Message}).", InAppToastSeverity.Error);
            }
        }

        private static bool IsEditableImage(string extension)
        {
            return EditableImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
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

        private static string BuildImageGalleryDateLine(FileInfo file)
        {
            var dateText = file.LastWriteTime.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            var relativeText = FormatRelativeAgo(file.LastWriteTime);
            return $"{dateText} ({relativeText} ago)";
        }

        private static string BuildImageGallerySizeLine(FileInfo file) => FormatBytes(file.Length);

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
            string imageDateLine,
            string imageSizeLine,
            bool isEditableImage,
            string fileGlyph,
            long lastWriteTimeUtcTicks,
            long fileLengthBytes)
        {
            Path = path;
            Name = name;
            FileInfoText = fileInfoText;
            ImageDateLine = imageDateLine;
            ImageSizeLine = imageSizeLine;
            IsEditableImage = isEditableImage;
            FileGlyph = fileGlyph;
            LastWriteTimeUtcTicks = lastWriteTimeUtcTicks;
            FileLengthBytes = fileLengthBytes;
        }

        public string Path { get; }

        public string Name { get; }

        public string FileInfoText { get; }

        public string ImageDateLine { get; }

        public string ImageSizeLine { get; }

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
