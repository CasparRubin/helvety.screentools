using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Streams;

namespace helvety.screentools.Capture
{
    internal sealed class CaptureCoordinator
    {
        private readonly IFreezeFrameProvider _freezeFrameProvider;
        private readonly ImageSaveService _imageSaveService;
        private readonly DispatcherQueue _dispatcherQueue;

        public CaptureCoordinator(
            IFreezeFrameProvider freezeFrameProvider,
            ImageSaveService imageSaveService,
            DispatcherQueue dispatcherQueue)
        {
            _freezeFrameProvider = freezeFrameProvider;
            _imageSaveService = imageSaveService;
            _dispatcherQueue = dispatcherQueue;
        }

        /// <summary>
        /// Runs one screenshot session. Must be called on the UI thread (hotkey sessions are started from the
        /// dispatcher); CPU-heavy freeze and save work runs on the thread pool and resumes back on the UI thread.
        /// </summary>
        public async Task<CaptureSessionResult> StartSelectionAsync(Action<string> publishStatus)
        {
            if (!await OverlaySessionGate.Gate.WaitAsync(0))
            {
                return new CaptureSessionResult(0, WasCanceled: false);
            }

            var savedScreenshotCount = 0;
            SelectionOverlayWindow? overlay = null;
            try
            {
                if (!SettingsService.TryGetEffectiveSaveFolderPath(out var outputFolderPath))
                {
                    publishStatus("Screenshot failed: no writable save folder configured.");
                    return new CaptureSessionResult(0, WasCanceled: false);
                }

                var captureSettings = SettingsService.Load();
                FreezeFrame freezeFrame;
                try
                {
                    // GDI BitBlt + GetDIBits can stall the UI thread for large virtual desktops; run off the dispatcher.
                    freezeFrame = await Task.Run(_freezeFrameProvider.CaptureVirtualScreen);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Screenshot freeze failed.", ex);
                    publishStatus($"Screenshot failed while freezing screen ({ex.Message}).");
                    return new CaptureSessionResult(0, WasCanceled: false);
                }

                overlay = new SelectionOverlayWindow(freezeFrame, captureSettings.ShowScreenshotOverlayInstructions);
                var activeOverlay = overlay;
                ActiveOverlayCancelService.Register(_dispatcherQueue, HotkeySessionKind.Screenshot, activeOverlay.RequestCancelFromExternal);
                // Release Escape / repeat-hotkey routing as soon as the overlay is gone, not after the save finishes.
                activeOverlay.Closed += (_, _) => ActiveOverlayCancelService.Unregister();

                while (true)
                {
                    var action = await activeOverlay.RunSelectionAsync();
                    if (action.Mode == SelectionCommitMode.Cancel || !action.Bounds.HasValue)
                    {
                        publishStatus("Screenshot canceled.");
                        return new CaptureSessionResult(savedScreenshotCount, WasCanceled: true);
                    }

                    SavedSelectionResult saveResult;
                    try
                    {
                        // Crop + quality modes do large CPU work before the first await; keep the UI thread free.
                        saveResult = await Task.Run(() => _imageSaveService.SaveSelectionAsync(
                            freezeFrame,
                            action.Bounds.Value,
                            outputFolderPath,
                            captureSettings.ScreenshotQualityMode));
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("Screenshot save failed.", ex);
                        publishStatus($"Screenshot failed while saving image ({ex.Message}).");
                        return new CaptureSessionResult(savedScreenshotCount, WasCanceled: false);
                    }

                    savedScreenshotCount++;
                    // Screen Tools reloads its gallery when notified; saveResult.PngBytes is still used for clipboard copy below.
                    CaptureGalleryNotifier.NotifyCaptureSaved(saveResult.OutputPath);
                    var filename = Path.GetFileName(saveResult.OutputPath);

                    if (action.Mode == SelectionCommitMode.RightCommitContinue)
                    {
                        if (activeOverlay.IsClosed)
                        {
                            // Escape was pressed while the save was running.
                            publishStatus($"Saved screenshot: {saveResult.OutputPath}");
                            return new CaptureSessionResult(savedScreenshotCount, WasCanceled: false);
                        }

                        activeOverlay.UpdateInstructionStatus("Ready for next screenshot...");
                        activeOverlay.ShowSessionToast($"Saved {filename}", saveResult.OutputPath);
                        publishStatus($"Saved screenshot (staying in screenshot mode): {saveResult.OutputPath}");
                        continue;
                    }

                    try
                    {
                        // Clipboard APIs require the UI thread, which this method runs on.
                        await CopyBitmapToClipboardAsync(saveResult.PngBytes);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn("Screenshot clipboard copy failed.", ex);
                        publishStatus($"Saved screenshot but failed to copy clipboard ({DescribeException(ex)}).");
                        return new CaptureSessionResult(savedScreenshotCount, WasCanceled: false);
                    }

                    publishStatus($"Saved screenshot and copied to clipboard: {saveResult.OutputPath}");
                    return new CaptureSessionResult(savedScreenshotCount, WasCanceled: false);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Screenshot session failed.", ex);
                publishStatus($"Screenshot failed ({ex.Message}).");
                return new CaptureSessionResult(savedScreenshotCount, WasCanceled: false);
            }
            finally
            {
                // Every exit path (including save failures in right-click "continue" mode) must remove the topmost overlay.
                overlay?.CloseIfOpen();
                ActiveOverlayCancelService.Unregister();
                OverlaySessionGate.Gate.Release();
            }
        }

        private static async Task CopyBitmapToClipboardAsync(byte[] pngBytes)
        {
            Exception? last = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var stream = new InMemoryRandomAccessStream();
                    await stream.WriteAsync(pngBytes.AsBuffer()).AsTask().ConfigureAwait(true);
                    stream.Seek(0);

                    var dataPackage = new DataPackage
                    {
                        RequestedOperation = DataPackageOperation.Copy
                    };
                    dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                    Clipboard.SetContent(dataPackage);
                    Clipboard.Flush();
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (attempt == 0)
                    {
                        await Task.Delay(50).ConfigureAwait(true);
                    }
                }
            }

            throw last ?? new InvalidOperationException("Clipboard copy failed.");
        }

        private static string DescribeException(Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(ex.Message))
            {
                return ex.Message;
            }

            if (ex is COMException com && com.HResult != 0)
            {
                return $"HRESULT 0x{com.HResult:X8}";
            }

            return ex.GetType().Name;
        }

    }

    internal readonly record struct CaptureSessionResult(int SavedScreenshotCount, bool WasCanceled);
}
