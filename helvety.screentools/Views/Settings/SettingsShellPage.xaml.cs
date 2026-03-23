using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace helvety.screentools.Views.Settings
{
    /// <summary>
    /// Nested <see cref="NavigationView"/> hosting module pages (General, Screen capture, Live Draw, Capture mode, App behavior, Danger zone).
    /// </summary>
    public sealed partial class SettingsShellPage : Page
    {
        private bool _isFirstLoad = true;

        public SettingsShellPage()
        {
            InitializeComponent();
            Loaded += SettingsShellPage_Loaded;
        }

        private void SettingsShellPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (!_isFirstLoad)
            {
                return;
            }

            _isFirstLoad = false;
            if (SettingsNav.MenuItems.Count > 0 && SettingsNav.SelectedItem is null)
            {
                SettingsNav.SelectedItem = SettingsNav.MenuItems[0];
            }

            NavigateToTag("general");
        }

        private void SettingsNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer?.Tag is not string tag)
            {
                return;
            }

            NavigateToTag(tag);
        }

        private void NavigateToTag(string tag)
        {
            var pageType = tag switch
            {
                "general" => typeof(GeneralSettingsPage),
                "capture" => typeof(CaptureHotkeySettingsPage),
                "livedraw" => typeof(LiveDrawSettingsPage),
                "capturemode" => typeof(CaptureModeSettingsPage),
                "appbehavior" => typeof(AppBehaviorSettingsPage),
                "danger" => typeof(DangerZoneSettingsPage),
                _ => typeof(GeneralSettingsPage)
            };

            if (SettingsFrame.CurrentSourcePageType != pageType)
            {
                SettingsFrame.Navigate(pageType);
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (SettingsNav.SelectedItem is null && SettingsNav.MenuItems.Count > 0)
            {
                SettingsNav.SelectedItem = SettingsNav.MenuItems[0];
            }
        }
    }
}
