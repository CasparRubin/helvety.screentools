using helvety.screentools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace helvety.screentools.Views.Settings
{
    public sealed partial class AnimationsSettingsPage : Page
    {
        private bool _isUpdatingBorderIntensitySelection;

        public AnimationsSettingsPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;
            InitializeBorderIntensitySelection();
            SettingsService.SettingsChanged += SettingsService_SettingsChanged;
            Unloaded += AnimationsSettingsPage_Unloaded;
        }

        private void AnimationsSettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            SettingsService.SettingsChanged -= SettingsService_SettingsChanged;
            Unloaded -= AnimationsSettingsPage_Unloaded;
        }

        private void SettingsService_SettingsChanged()
        {
            DispatcherQueue.TryEnqueue(InitializeBorderIntensitySelection);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            InitializeBorderIntensitySelection();
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
    }
}
