using System;
using System.Diagnostics;
using System.IO;

namespace helvety.screentools
{
    /// <summary>
    /// Minimal diagnostics log for Release builds (Debug.WriteLine is compiled out there).
    /// Writes to %LOCALAPPDATA%\Helvety\ScreenTools\Logs\app.log and keeps one rolled-over file.
    /// Packaged installs get this path redirected into the package's private LocalCache by Windows.
    /// </summary>
    internal static class AppLog
    {
        private const long MaxLogBytes = 1024 * 1024;
        private static readonly object Sync = new();
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Helvety",
            "ScreenTools",
            "Logs");

        internal static string LogFilePath { get; } = Path.Combine(LogDirectory, "app.log");

        internal static void Info(string message) => Write("INFO", message, null);

        internal static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

        internal static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

        private static void Write(string level, string message, Exception? exception)
        {
            var line = exception is null
                ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}"
                : $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}{exception}";
            Debug.WriteLine(line);

            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(LogDirectory);
                    var file = new FileInfo(LogFilePath);
                    if (file.Exists && file.Length > MaxLogBytes)
                    {
                        File.Move(LogFilePath, Path.Combine(LogDirectory, "app.old.log"), overwrite: true);
                    }

                    File.AppendAllText(LogFilePath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }
}
