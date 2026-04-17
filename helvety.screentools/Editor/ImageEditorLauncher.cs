using helvety.screentools.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.IO;
using WinRT.Interop;

namespace helvety.screentools.Editor
{
    internal static class ImageEditorLauncher
    {
        private static readonly Dictionary<string, Window> OpenWindows = new(StringComparer.OrdinalIgnoreCase);

        internal static void OpenEditor(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                InAppToastService.Show("Image file does not exist.", InAppToastSeverity.Error);
                return;
            }

            if (!string.Equals(Path.GetExtension(filePath), ".png", StringComparison.OrdinalIgnoreCase))
            {
                InAppToastService.Show("Only PNG files can be edited.", InAppToastSeverity.Warning);
                return;
            }

            if (OpenWindows.TryGetValue(filePath, out var existingWindow))
            {
                existingWindow.Activate();
                return;
            }

            try
            {
                var window = new Window
                {
                    Title = $"Editor - {Path.GetFileName(filePath)}",
                    Content = new ImageEditorPage(filePath)
                };

                window.Closed += (_, _) =>
                {
                    OpenWindows.Remove(filePath);
                };

                OpenWindows[filePath] = window;
                window.Activate();
                TryMaximizeWindow(window);
            }
            catch (Exception ex)
            {
                InAppToastService.Show($"Could not open editor ({ex.Message}).", InAppToastSeverity.Error);
                OpenWindows.Remove(filePath);
            }
        }

        internal static void CloseEditor(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            if (!OpenWindows.TryGetValue(filePath, out var window))
            {
                return;
            }

            try
            {
                window.Close();
            }
            catch
            {
                // If close fails, keep current editor window open.
            }
        }

        private static void TryMaximizeWindow(Window window)
        {
            try
            {
                var windowHandle = WindowNative.GetWindowHandle(window);
                if (windowHandle == IntPtr.Zero)
                {
                    return;
                }

                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
                var appWindow = AppWindow.GetFromWindowId(windowId);
                if (appWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Maximize();
                }
            }
            catch
            {
                // If maximize is unavailable, keep default window behavior.
            }
        }
    }
}
