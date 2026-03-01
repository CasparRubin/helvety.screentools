using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;
using helvety.screenshots.Capture;

namespace helvety.screenshots
{
    public partial class App : Application
    {
        private Window? _window;
        private CaptureCoordinator? _captureCoordinator;
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
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (HotkeyService is not null)
            {
                HotkeyService.HotkeyPressed -= HotkeyService_HotkeyPressed;
                HotkeyService.Dispose();
                HotkeyService = null;
            }
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

            await _captureCoordinator.StartSelectionAsync(message => CaptureStatusPublished?.Invoke(message));
        }
    }
}
