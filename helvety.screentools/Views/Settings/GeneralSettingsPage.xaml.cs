using helvety.screentools;
using helvety.screentools.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace helvety.screentools.Views.Settings
{
    public sealed partial class GeneralSettingsPage : Page
    {
        private bool _isUpdatingMinimizeToTraySelection;
        private bool _isUpdatingEditorPerformanceModeSelection;
        private bool _isUpdatingBorderIntensitySelection;
        private bool _isUpdatingRunAtSignInSelection;

        public GeneralSettingsPage()
        {
            InitializeComponent();
            SettingsService.SettingsChanged += SettingsService_SettingsChanged;
            Unloaded += (_, _) => SettingsService.SettingsChanged -= SettingsService_SettingsChanged;
            Loaded += GeneralSettingsPage_Loaded;
        }

        private void GeneralSettingsPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            InitializeMinimizeToTraySelection();
            InitializeEditorPerformanceModeSelection();
            InitializeBorderIntensitySelection();
            _ = RefreshRunAtSignInToggleAsync();
        }

        private void SettingsService_SettingsChanged()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                InitializeMinimizeToTraySelection();
                InitializeEditorPerformanceModeSelection();
                InitializeBorderIntensitySelection();
                _ = RefreshRunAtSignInToggleAsync();
            });
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            InitializeMinimizeToTraySelection();
            InitializeEditorPerformanceModeSelection();
            InitializeBorderIntensitySelection();
            _ = RefreshRunAtSignInToggleAsync();
        }

        private async Task RefreshRunAtSignInToggleAsync()
        {
            if (!StartupLaunchService.IsSupported)
            {
                RunAtSignInSection.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                return;
            }

            RunAtSignInSection.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            var state = await StartupLaunchService.GetStateAsync();
            if (state is null)
            {
                return;
            }

            _isUpdatingRunAtSignInSelection = true;
            try
            {
                RunAtSignInToggle.IsOn = state == StartupTaskState.Enabled;
            }
            finally
            {
                _isUpdatingRunAtSignInSelection = false;
            }
        }

        private void InitializeMinimizeToTraySelection()
        {
            var settings = SettingsService.Load();
            _isUpdatingMinimizeToTraySelection = true;
            try
            {
                MinimizeToTrayToggle.IsOn = settings.MinimizeToTrayOnClose;
            }
            finally
            {
                _isUpdatingMinimizeToTraySelection = false;
            }
        }

        private void InitializeEditorPerformanceModeSelection()
        {
            var settings = SettingsService.LoadEditorUiSettings();
            _isUpdatingEditorPerformanceModeSelection = true;
            try
            {
                EditorPerformanceModeToggle.IsOn = settings.PerformanceModeEnabled;
            }
            finally
            {
                _isUpdatingEditorPerformanceModeSelection = false;
            }
        }

        private void MinimizeToTrayToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (_isUpdatingMinimizeToTraySelection)
            {
                return;
            }

            SettingsService.SaveMinimizeToTrayOnClose(MinimizeToTrayToggle.IsOn);
        }

        private void EditorPerformanceModeToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (_isUpdatingEditorPerformanceModeSelection)
            {
                return;
            }

            SettingsService.SaveEditorPerformanceModeEnabled(EditorPerformanceModeToggle.IsOn);
        }

        private async void RunAtSignInToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (_isUpdatingRunAtSignInSelection || !StartupLaunchService.IsSupported)
            {
                return;
            }

            if (RunAtSignInToggle.IsOn)
            {
                var state = await StartupLaunchService.RequestEnableAsync();
                if (state != StartupTaskState.Enabled)
                {
                    _isUpdatingRunAtSignInSelection = true;
                    try
                    {
                        RunAtSignInToggle.IsOn = false;
                    }
                    finally
                    {
                        _isUpdatingRunAtSignInSelection = false;
                    }

                    InAppToastService.Show(
                        "Startup was not enabled. You can turn it on in Windows Settings → Apps → Startup.",
                        InAppToastSeverity.Warning);
                }
            }
            else
            {
                await StartupLaunchService.DisableAsync();
            }
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

        private async void ResetSettingsButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var confirmationDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Reset all settings to defaults?",
                Content = "This clears all saved app settings and restores defaults. Files on disk (captures and exports) are not deleted.",
                PrimaryButtonText = "Reset",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var dialogResult = await confirmationDialog.ShowAsync();
            if (dialogResult != ContentDialogResult.Primary)
            {
                return;
            }

            SettingsService.ResetAllSettingsToDefaults();
            InAppToastService.Show("All settings were reset to defaults.", InAppToastSeverity.Success);
        }
    }
}
