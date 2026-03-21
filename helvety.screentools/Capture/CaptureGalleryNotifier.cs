using System;

namespace helvety.screentools.Capture
{
    /// <summary>Raises when a capture file has been written so the home gallery can update immediately (watcher uses a short debounce).</summary>
    internal static class CaptureGalleryNotifier
    {
        internal static event Action<string>? CaptureSavedToPath;

        internal static void NotifyCaptureSaved(string outputPath)
        {
            CaptureSavedToPath?.Invoke(outputPath);
        }
    }
}
