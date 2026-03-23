using helvety.screentools;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace helvety.screentools.Views.Settings
{
    public sealed partial class CaptureModeSettingsPage : Page
    {
        private bool _isUpdatingBorderIntensitySelection;
        private bool _isUpdatingScreenshotQualitySelection;
        private bool _isUpdatingOverlayInstructionSelection;

        public CaptureModeSettingsPage()
        {
            InitializeComponent();
            SettingsService.SettingsChanged += SettingsService_SettingsChanged;
            Unloaded += (_, _) => SettingsService.SettingsChanged -= SettingsService_SettingsChanged;
            Loaded += CaptureModeSettingsPage_Loaded;
        }

        private void CaptureModeSettingsPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            InitializeBorderIntensitySelection();
            InitializeScreenshotQualitySelection();
            InitializeOverlayInstructionSelection();
        }

        private void SettingsService_SettingsChanged()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                InitializeBorderIntensitySelection();
                InitializeScreenshotQualitySelection();
                InitializeOverlayInstructionSelection();
            });
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            InitializeBorderIntensitySelection();
            InitializeScreenshotQualitySelection();
            InitializeOverlayInstructionSelection();
        }

        private void InitializeBorderIntensitySelection()
        {
            var settings = SettingsService.Load();
            _isUpdatingBorderIntensitySelection = true;
            try
            {
                BorderIntensityComboBox.SelectedIndex = settings.ScreenshotBorderIntensity switch
                {
                    ScreenshotBorderIntensity.Subtle => 0,
                    ScreenshotBorderIntensity.Bold => 2,
                    _ => 1
                };
            }
            finally
            {
                _isUpdatingBorderIntensitySelection = false;
            }
        }

        private void InitializeOverlayInstructionSelection()
        {
            var settings = SettingsService.Load();
            _isUpdatingOverlayInstructionSelection = true;
            try
            {
                ShowOverlayInstructionsToggle.IsOn = settings.ShowScreenshotOverlayInstructions;
            }
            finally
            {
                _isUpdatingOverlayInstructionSelection = false;
            }
        }

        private void InitializeScreenshotQualitySelection()
        {
            var settings = SettingsService.Load();
            _isUpdatingScreenshotQualitySelection = true;
            try
            {
                ScreenshotQualityModeComboBox.SelectedIndex = settings.ScreenshotQualityMode switch
                {
                    ScreenshotQualityMode.Optimized => 1,
                    ScreenshotQualityMode.Heavy => 2,
                    _ => 0
                };
            }
            finally
            {
                _isUpdatingScreenshotQualitySelection = false;
            }
        }

        private void BorderIntensityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingBorderIntensitySelection)
            {
                return;
            }

            var selectedIntensity = BorderIntensityComboBox.SelectedIndex switch
            {
                0 => ScreenshotBorderIntensity.Subtle,
                2 => ScreenshotBorderIntensity.Bold,
                _ => ScreenshotBorderIntensity.Balanced
            };

            SettingsService.SaveScreenshotBorderIntensity(selectedIntensity);
        }

        private void ShowOverlayInstructionsToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (_isUpdatingOverlayInstructionSelection)
            {
                return;
            }

            SettingsService.SaveShowScreenshotOverlayInstructions(ShowOverlayInstructionsToggle.IsOn);
        }

        private void ScreenshotQualityModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingScreenshotQualitySelection)
            {
                return;
            }

            var selectedQualityMode = ScreenshotQualityModeComboBox.SelectedIndex switch
            {
                1 => ScreenshotQualityMode.Optimized,
                2 => ScreenshotQualityMode.Heavy,
                _ => ScreenshotQualityMode.Fast
            };

            SettingsService.SaveScreenshotQualityMode(selectedQualityMode);
        }
    }
}
