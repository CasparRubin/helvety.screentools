using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Windows.Graphics;

namespace helvety.screentools.Capture
{
    internal sealed class LiveDrawCoordinator
    {
        private readonly DispatcherQueue _dispatcherQueue;

        internal LiveDrawCoordinator(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue;
        }

        internal async Task RunLiveDrawAsync(Action<string> publishStatus)
        {
            if (!await OverlaySessionGate.Gate.WaitAsync(0))
            {
                publishStatus("Another overlay is already active.");
                return;
            }

            try
            {
                RectInt32 bounds;
                try
                {
                    bounds = VirtualScreenBounds.Get();
                }
                catch (Exception ex)
                {
                    publishStatus($"Live Draw failed: {ex.Message}");
                    return;
                }

                await EnqueueVoidAsync(async () =>
                {
                    var content = new LiveDrawOverlayContent(bounds);
                    LiveDrawNativeHost? host = null;
                    void OnCloseRequested()
                    {
                        host?.Close();
                    }

                    try
                    {
                        host = new LiveDrawNativeHost();
                        content.CloseRequested += OnCloseRequested;
                        host.ShowAndHost(bounds, content, () => content.RequestExitFromNative());
                        await content.PrepareVisibleSessionAsync().ConfigureAwait(true);
                        await content.RunSessionAsync().ConfigureAwait(true);
                    }
                    finally
                    {
                        content.CloseRequested -= OnCloseRequested;
                        host?.Dispose();
                    }
                }).ConfigureAwait(true);
            }
            finally
            {
                OverlaySessionGate.Gate.Release();
            }
        }

        private Task EnqueueVoidAsync(Func<Task> work)
        {
            var completionSource = new TaskCompletionSource();
            _dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await work().ConfigureAwait(true);
                    completionSource.TrySetResult();
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
