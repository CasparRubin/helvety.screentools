using Microsoft.UI.Xaml;
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
        private TrayIconService? _trayIconService;
        internal static Window? MainAppWindow { get; private set; }
        internal static HotkeyService? HotkeyService { get; private set; }
        internal static event Action<string>? CaptureStatusPublished;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            SettingsService.InitializeSaveFolderOnStartup();

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

            HotkeyService = new HotkeyService();
            HotkeyService.HotkeyPressed += HotkeyService_HotkeyPressed;
            HotkeyService.Start();
            _window.Closed += MainWindow_Closed;
            _trayIconService = new TrayIconService(
                openMainWindow: RestoreMainWindowFromTray,
                exitApplication: ExitApplication);
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

        private void HotkeyService_HotkeyPressed(string hotkeyDisplay)
        {
            MainAppWindow?.DispatcherQueue.TryEnqueue(() => _ = RunCaptureAsync(hotkeyDisplay));
        }

        private async Task RunCaptureAsync(string hotkeyDisplay)
        {
            CaptureStatusPublished?.Invoke($"Hotkey {hotkeyDisplay} pressed.");
            if (_captureCoordinator is null)
            {
                CaptureStatusPublished?.Invoke("Capture failed: capture coordinator is not initialized.");
                return;
            }

            var restoreWindowAfterCapture = _window?.IsHiddenToTray == true;
            var sessionResult = await _captureCoordinator.StartSelectionAsync(message => CaptureStatusPublished?.Invoke(message));
            if (restoreWindowAfterCapture && sessionResult.SavedScreenshotCount > 0)
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
