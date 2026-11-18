using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Threading.Tasks;
using helvety.screentools.Capture;
using helvety.screentools.Services;

namespace helvety.screentools
{
    public partial class App : Application
    {
        private MainWindow? _window;
        private CaptureCoordinator? _captureCoordinator;
        private LiveDrawCoordinator? _liveDrawCoordinator;
        private TrayIconService? _trayIconService;
        internal static Window? MainAppWindow { get; private set; }
        internal static HotkeyService? HotkeyService { get; private set; }
        internal static event Action<string>? SessionStatusPublished;

        public App()
        {
            InitializeComponent();
            UnhandledException += (_, e) =>
            {
                // A tray-resident tool should survive a stray UI-thread exception (e.g. from an async void handler).
                AppLog.Error("Unhandled UI exception.", e.Exception);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                AppLog.Error("Unhandled exception (terminating).", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                AppLog.Error("Unobserved task exception.", e.Exception);
                e.SetObserved();
            };
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                SettingsService.InitializeSaveFolderOnStartup();
            }
            catch (Exception ex)
            {
                AppLog.Warn("Save folder init failed.", ex);
            }

            _window = new MainWindow();
            MainAppWindow = _window;
            _window.DispatcherQueue.TryEnqueue(() => _ = ApplyStartupLaunchPreferenceAsync());

            if (IsActivatedByStartupTask())
            {
                _window.MarkHiddenToTray();
            }
            else
            {
                _window.Activate();
            }

            var freezeFrameProvider = new GdiFreezeFrameProvider();
            var windowSnapHitTester = new WindowSnapHitTester();
            var imageSaveService = new ImageSaveService();
            _captureCoordinator = new CaptureCoordinator(
                freezeFrameProvider,
                windowSnapHitTester,
                imageSaveService,
                _window.DispatcherQueue);

            _liveDrawCoordinator = new LiveDrawCoordinator(_window.DispatcherQueue);

            HotkeyService = new HotkeyService();
            HotkeyService.HotkeyPressed += HotkeyService_HotkeyPressed;
            HotkeyService.Start();
            _window.Closed += MainWindow_Closed;
            try
            {
                _trayIconService = new TrayIconService(
                    openMainWindow: RestoreMainWindowFromTray,
                    exitApplication: ExitApplication);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Tray icon unavailable.", ex);
            }
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (args.Handled)
            {
                return;
            }

            if (HotkeyService is not null)
            {
                HotkeyService.HotkeyPressed -= HotkeyService_HotkeyPressed;
                HotkeyService.Dispose();
                HotkeyService = null;
            }

            _trayIconService?.Dispose();
            _trayIconService = null;
        }

        private void HotkeyService_HotkeyPressed(HotkeySessionKind kind, string hotkeyDisplay)
        {
            MainAppWindow?.DispatcherQueue.TryEnqueue(() => _ = RunHotkeySessionAsync(kind, hotkeyDisplay));
        }

        private async Task RunHotkeySessionAsync(HotkeySessionKind kind, string hotkeyDisplay)
        {
            var sessionLabel = kind switch
            {
                HotkeySessionKind.Screenshot => "Screenshot",
                HotkeySessionKind.LiveDraw => "Live Draw",
                _ => kind.ToString()
            };
            SessionStatusPublished?.Invoke($"{sessionLabel} hotkey {hotkeyDisplay} pressed.");
            try
            {
                if (kind == HotkeySessionKind.Screenshot)
                {
                    await RunCaptureAsync();
                }
                else
                {
                    await RunLiveDrawAsync();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error($"{sessionLabel} session failed.", ex);
                SessionStatusPublished?.Invoke($"{sessionLabel} failed ({ex.Message}).");
            }
        }

        private async Task RunCaptureAsync()
        {
            if (_captureCoordinator is null)
            {
                SessionStatusPublished?.Invoke("Screenshot failed: screenshot coordinator is not initialized.");
                return;
            }

            var restoreWindowAfterCapture = _window?.IsHiddenToTray == true;
            var sessionResult = await _captureCoordinator.StartSelectionAsync(message => SessionStatusPublished?.Invoke(message));
            if (restoreWindowAfterCapture && sessionResult.SavedScreenshotCount > 0)
            {
                _window?.DispatcherQueue.TryEnqueue(RestoreMainWindowFromTray);
            }
        }

        private async Task RunLiveDrawAsync()
        {
            if (_liveDrawCoordinator is null)
            {
                SessionStatusPublished?.Invoke("Live Draw failed: coordinator is not initialized.");
                return;
            }

            await _liveDrawCoordinator.RunLiveDrawAsync(message => SessionStatusPublished?.Invoke(message));
        }

        private void RestoreMainWindowFromTray()
        {
            _window?.RestoreFromTray();
        }

        private void ExitApplication()
        {
            _window?.DispatcherQueue.TryEnqueue(() =>
            {
                _window?.RequestFullExit();
                _window?.Close();
                Exit();
            });
        }

        private static async Task ApplyStartupLaunchPreferenceAsync()
        {
            if (!StartupLaunchService.IsSupported)
            {
                return;
            }

            try
            {
                if (SettingsService.Load().RunAtWindowsStartup)
                {
                    await StartupLaunchService.RequestEnableAsync();
                }
                else
                {
                    await StartupLaunchService.DisableAsync();
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Startup launch preference apply failed.", ex);
            }
        }

        private static bool IsActivatedByStartupTask()
        {
            try
            {
                var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                return activatedArgs.Kind == ExtendedActivationKind.StartupTask;
            }
            catch
            {
                return false;
            }
        }
    }
}
