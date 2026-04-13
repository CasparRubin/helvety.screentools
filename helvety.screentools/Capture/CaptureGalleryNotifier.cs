using System;

namespace helvety.screentools.Capture
{
    /// <summary>
    /// Signals that a screenshot was saved so the General home gallery can reload immediately. The save folder
    /// <see cref="System.IO.FileSystemWatcher"/> still triggers a debounced full rescan separately.
    /// </summary>
    internal static class CaptureGalleryNotifier
    {
        internal static event Action<string>? CaptureSavedToPath;

        internal static void NotifyCaptureSaved(string outputPath)
        {
            CaptureSavedToPath?.Invoke(outputPath);
        }
    }
}
