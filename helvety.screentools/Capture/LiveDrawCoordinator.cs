using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace helvety.screentools.Capture
{
    internal sealed class LiveDrawCoordinator
    {
        private readonly DispatcherQueue _dispatcherQueue;

        internal LiveDrawCoordinator(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue;
        }

        /// <summary>
        /// On Windows 10 build 19041+, uses a placeholder <see cref="FreezeFrame"/> (no pre-window BitBlt);
        /// <see cref="LiveDrawOverlayWindow.PrepareVisibleSessionAsync"/> applies the first real GDI capture after
        /// the HWND can use capture exclusion. On older Windows, captures the full screen before the overlay exists.
        /// </summary>
        internal async Task RunLiveDrawAsync(Action<string> publishStatus)
        {
            if (!await OverlaySessionGate.Gate.WaitAsync(0))
            {
                publishStatus("Another overlay is already active.");
                return;
            }

            try
            {
                FreezeFrame freezeFrame;
                try
                {
                    if (LiveDrawPlatformSupport.IsLiveDesktopRefreshSupported)
                    {
                        var bounds = VirtualScreenBounds.Get();
                        var stride = bounds.Width * 4;
                        freezeFrame = new FreezeFrame(bounds, stride, new byte[stride * bounds.Height]);
                    }
                    else
                    {
                        freezeFrame = await Task.Run(() => new GdiFreezeFrameProvider().CaptureVirtualScreen())
                            .ConfigureAwait(true);
                    }
                }
                catch (Exception ex)
                {
                    publishStatus($"Live Draw failed: {ex.Message}");
                    return;
                }

                var overlay = await EnqueueAsync(() => new LiveDrawOverlayWindow(freezeFrame));
                await EnqueueAsync(async () =>
                {
                    await overlay.PrepareVisibleSessionAsync();
                    await overlay.RunSessionAsync();
                });
            }
            finally
            {
                OverlaySessionGate.Gate.Release();
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
    }
}
