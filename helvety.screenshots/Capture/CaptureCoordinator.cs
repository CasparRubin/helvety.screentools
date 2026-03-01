using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Streams;

namespace helvety.screenshots.Capture
{
    internal sealed class CaptureCoordinator
    {
        private readonly IFreezeFrameProvider _freezeFrameProvider;
        private readonly WindowSnapHitTester _windowSnapHitTester;
        private readonly ImageSaveService _imageSaveService;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly SemaphoreSlim _captureGate = new(1, 1);

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

        public async Task StartSelectionAsync(Action<string> publishStatus)
        {
            if (!await _captureGate.WaitAsync(0))
            {
                return;
            }

            try
            {
                if (!SettingsService.TryGetEffectiveSaveFolderPath(out var outputFolderPath))
                {
                    publishStatus("Capture failed: no writable save folder configured.");
                    return;
                }

                var captureSettings = SettingsService.Load();
                FreezeFrame freezeFrame;
                try
                {
                    freezeFrame = _freezeFrameProvider.CaptureVirtualScreen();
                }
                catch (Exception ex)
                {
                    publishStatus($"Capture failed while freezing screen ({ex.Message}).");
                    return;
                }

                var showInstructions = captureSettings.ShowScreenshotOverlayInstructions;
                var overlay = await EnqueueAsync(() => new SelectionOverlayWindow(freezeFrame, _windowSnapHitTester, showInstructions));

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
                        return;
                    }

                    if (action.Mode == SelectionCommitMode.Cancel || !action.Bounds.HasValue)
                    {
                        await EnqueueAsync(() =>
                        {
                            overlay.UpdateInstructionStatus("Capture canceled.");
                            overlay.ShowSessionToast("Capture canceled", "No screenshot was saved.");
                            return true;
                        });
                        publishStatus("Capture canceled.");
                        return;
                    }

                    SavedSelectionResult saveResult;
                    try
                    {
                        saveResult = await _imageSaveService.SaveSelectionAsync(
                            freezeFrame,
                            action.Bounds.Value,
                            outputFolderPath);
                    }
                    catch (Exception ex)
                    {
                        publishStatus($"Capture failed while saving image ({ex.Message}).");
                        return;
                    }

                    if (action.Mode == SelectionCommitMode.RightCommitContinue)
                    {
                        var filename = Path.GetFileName(saveResult.OutputPath);
                        await EnqueueAsync(() =>
                        {
                            overlay.UpdateInstructionStatus("Ready for next capture...");
                            overlay.ShowSessionToast($"Saved {filename}", saveResult.OutputPath);
                            return true;
                        });
                        publishStatus($"Saved screenshot (staying in capture mode): {saveResult.OutputPath}");
                        continue;
                    }

                    try
                    {
                        var filename = Path.GetFileName(saveResult.OutputPath);
                        await EnqueueAsync(() =>
                        {
                            overlay.UpdateInstructionStatus("Saved and copied to clipboard.");
                            overlay.ShowSessionToast($"Saved {filename}", saveResult.OutputPath);
                            return true;
                        });
                        await CopyBitmapToClipboardAsync(saveResult.PngBytes);
                    }
                    catch (Exception ex)
                    {
                        publishStatus($"Saved screenshot but failed to copy clipboard ({ex.Message}).");
                        return;
                    }

                    publishStatus($"Saved screenshot and copied to clipboard: {saveResult.OutputPath}");
                    return;
                }
            }
            finally
            {
                _captureGate.Release();
            }
        }

        private Task<T> EnqueueAsync<T>(Func<T> work)
        {
            var completionSource = new TaskCompletionSource<T>();
            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    completionSource.TrySetResult(work());
                }
                catch (Exception ex)
                {
                    completionSource.TrySetException(ex);
                }
            });

            return completionSource.Task;
        }

        private Task<T> EnqueueAsync<T>(Func<Task<T>> work)
        {
            var completionSource = new TaskCompletionSource<T>();
            _dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    completionSource.TrySetResult(await work());
                }
                catch (Exception ex)
                {
                    completionSource.TrySetException(ex);
                }
            });

            return completionSource.Task;
        }

        private static async Task CopyBitmapToClipboardAsync(byte[] pngBytes)
        {
            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(pngBytes.AsBuffer());
            stream.Seek(0);

            var dataPackage = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Copy
            };
            dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
        }

    }
}
