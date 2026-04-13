using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
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
        private readonly WindowSnapHitTester _windowSnapHitTester;
        private readonly ImageSaveService _imageSaveService;
        private readonly DispatcherQueue _dispatcherQueue;

        public CaptureCoordinator(
            IFreezeFrameProvider freezeFrameProvider,
            WindowSnapHitTester windowSnapHitTester,
            ImageSaveService imageSaveService,
            DispatcherQueue dispatcherQueue)
        {
            _freezeFrameProvider = freezeFrameProvider;
            _windowSnapHitTester = windowSnapHitTester;
            _imageSaveService = imageSaveService;
            _dispatcherQueue = dispatcherQueue;
        }

        public async Task<CaptureSessionResult> StartSelectionAsync(Action<string> publishStatus)
        {
            if (!await OverlaySessionGate.Gate.WaitAsync(0))
            {
                return new CaptureSessionResult(0, WasCanceled: false);
            }

            var savedScreenshotCount = 0;
            try
            {
                if (!SettingsService.TryGetEffectiveSaveFolderPath(out var outputFolderPath))
                {
                    publishStatus("Capture failed: no writable save folder configured.");
                    return new CaptureSessionResult(0, WasCanceled: false);
                }

                var captureSettings = SettingsService.Load();
                FreezeFrame freezeFrame;
                try
                {
                    // GDI BitBlt + GetDIBits can stall the UI thread for large virtual desktops; run off the dispatcher.
                    freezeFrame = await Task.Run(() => _freezeFrameProvider.CaptureVirtualScreen()).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    publishStatus($"Capture failed while freezing screen ({ex.Message}).");
                    return new CaptureSessionResult(0, WasCanceled: false);
                }

                var showInstructions = captureSettings.ShowScreenshotOverlayInstructions;
                var overlay = await EnqueueAsync(() => new SelectionOverlayWindow(freezeFrame, _windowSnapHitTester, showInstructions));
                ActiveOverlayCancelService.Register(
                    _dispatcherQueue,
                    HotkeySessionKind.Screenshot,
                    () => overlay.RequestCancelFromExternal());

                while (true)
                {
                    SelectionAction action;
                    try
                    {
                        action = await EnqueueAsync(async () => await overlay.RunSelectionAsync());
                    }
                    catch (Exception ex)
                    {
                        publishStatus($"Capture failed while selecting area ({ex.Message}).");
                        await EnqueueAsync(() =>
                        {
                            overlay.Close();
                            return true;
                        });
                        return new CaptureSessionResult(savedScreenshotCount, WasCanceled: false);
                    }

                    if (action.Mode == SelectionCommitMode.Cancel || !action.Bounds.HasValue)
                    {
                        // Overlay is already closed by SelectionOverlayWindow.CompleteSelection; do not touch it here.
                        publishStatus("Capture canceled.");
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
                            captureSettings.ScreenshotQualityMode)).ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        publishStatus($"Capture failed while saving image ({ex.Message}).");
                        return new CaptureSessionResult(savedScreenshotCount, WasCanceled: false);
                    }

                    savedScreenshotCount++;
                    // Screen Tools reloads its gallery when notified; saveResult.PngBytes is still used for clipboard copy below.
                    CaptureGalleryNotifier.NotifyCaptureSaved(saveResult.OutputPath);

                    if (action.Mode == SelectionCommitMode.RightCommitContinue)
                    {
                        var filename = Path.GetFileName(saveResult.OutputPath);
                        await EnqueueAsync(() =>
                        {
                            overlay.UpdateInstructionStatus("Ready for next capture...");
                            overlay.ShowSessionToast($"Saved {filename}", saveResult.OutputPath);
                            return true;
                        });
                        publishStatus($"Saved capture (staying in capture mode): {saveResult.OutputPath}");
                        continue;
                    }

                    try
                    {
                        var filename = Path.GetFileName(saveResult.OutputPath);
                        // Single dispatcher async block: clipboard APIs require the UI thread; continuations must stay on it.
                        await EnqueueAsync(async () =>
                        {
                            overlay.UpdateInstructionStatus("Saved and copied to clipboard.");
                            overlay.ShowSessionToast($"Saved {filename}", saveResult.OutputPath);
                            await CopyBitmapToClipboardAsync(saveResult.PngBytes).ConfigureAwait(true);
                            return true;
                        });
                    }
                    catch (Exception ex)
                    {
                        publishStatus($"Saved capture but failed to copy clipboard ({DescribeException(ex)}).");
                        return new CaptureSessionResult(savedScreenshotCount, WasCanceled: false);
                    }

                    publishStatus($"Saved capture and copied to clipboard: {saveResult.OutputPath}");
                    return new CaptureSessionResult(savedScreenshotCount, WasCanceled: false);
                }
            }
            finally
            {
                ActiveOverlayCancelService.Unregister();
                OverlaySessionGate.Gate.Release();
            }
        }

        private Task<T> EnqueueAsync<T>(Func<T> work)
        {
            var completionSource = new TaskCompletionSource<T>();
            if (!_dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    completionSource.TrySetResult(work());
                }
                catch (Exception ex)
                {
                    completionSource.TrySetException(ex);
                }
            }))
            {
                completionSource.TrySetException(
                    new InvalidOperationException("Failed to enqueue work on the dispatcher."));
            }

            return completionSource.Task;
        }

        private Task<T> EnqueueAsync<T>(Func<Task<T>> work)
        {
            var completionSource = new TaskCompletionSource<T>();
            if (!_dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    completionSource.TrySetResult(await work());
                }
                catch (Exception ex)
                {
                    completionSource.TrySetException(ex);
                }
            }))
            {
                completionSource.TrySetException(
                    new InvalidOperationException("Failed to enqueue work on the dispatcher."));
            }

            return completionSource.Task;
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
