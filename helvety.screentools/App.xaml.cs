using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
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
                Debug.WriteLine($"Unhandled UI exception: {e.Exception}");
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
                Debug.WriteLine($"Save folder init failed: {ex}");
            }

            _window = new MainWindow();
            MainAppWindow = _window;
            _window.Activate();

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
                Debug.WriteLine($"Tray icon unavailable: {ex}");
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
            if (kind == HotkeySessionKind.Screenshot)
            {
                await RunCaptureAsync();
                return;
            }

            await RunLiveDrawAsync();
        }

        private async Task RunCaptureAsync()
        {
            if (_captureCoordinator is null)
            {
                SessionStatusPublished?.Invoke("Screenshot capture failed: capture coordinator is not initialized.");
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

            var restoreWindow = _window?.IsHiddenToTray == true;
            await _liveDrawCoordinator.RunLiveDrawAsync(message => SessionStatusPublished?.Invoke(message));
            if (restoreWindow)
            {
                _window?.DispatcherQueue.TryEnqueue(RestoreMainWindowFromTray);
            }
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
    }
}
