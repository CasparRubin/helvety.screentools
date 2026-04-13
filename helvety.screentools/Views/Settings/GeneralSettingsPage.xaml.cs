using helvety.screentools;
using helvety.screentools.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using Windows.ApplicationModel;

namespace helvety.screentools.Views.Settings
{
    public sealed partial class GeneralSettingsPage : Page
    {
        private bool _isUpdatingMinimizeToTraySelection;
        private bool _isUpdatingEditorPerformanceModeSelection;
        private bool _isUpdatingBorderIntensitySelection;
        private bool _isUpdatingStartWithWindowsSelection;
        private bool _isUpdatingGlobalHotkeyListenersSelection;

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
            InitializeGlobalHotkeyListenersSelection();
            InitializeEditorPerformanceModeSelection();
            InitializeBorderIntensitySelection();
            InitializeStartWithWindowsToggle();
        }

        private void SettingsService_SettingsChanged()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                InitializeMinimizeToTraySelection();
                InitializeGlobalHotkeyListenersSelection();
                InitializeEditorPerformanceModeSelection();
                InitializeBorderIntensitySelection();
                InitializeStartWithWindowsToggle();
            });
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            InitializeMinimizeToTraySelection();
            InitializeGlobalHotkeyListenersSelection();
            InitializeEditorPerformanceModeSelection();
            InitializeBorderIntensitySelection();
            InitializeStartWithWindowsToggle();
        }

        private void InitializeGlobalHotkeyListenersSelection()
        {
            var settings = SettingsService.Load();
            _isUpdatingGlobalHotkeyListenersSelection = true;
            try
            {
                GlobalHotkeyListenersToggle.IsOn = settings.GlobalHotkeyListenersEnabled;
            }
            finally
            {
                _isUpdatingGlobalHotkeyListenersSelection = false;
            }
        }

        private void InitializeStartWithWindowsToggle()
        {
            if (!StartupLaunchService.IsSupported)
            {
                WindowsStartupSection.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                return;
            }

            WindowsStartupSection.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            var settings = SettingsService.Load();
            _isUpdatingStartWithWindowsSelection = true;
            try
            {
                StartWithWindowsToggle.IsOn = settings.RunAtWindowsStartup;
            }
            finally
            {
                _isUpdatingStartWithWindowsSelection = false;
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

        private void GlobalHotkeyListenersToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (_isUpdatingGlobalHotkeyListenersSelection)
            {
                return;
            }

            SettingsService.SaveGlobalHotkeyListenersEnabled(GlobalHotkeyListenersToggle.IsOn);
        }

        private void EditorPerformanceModeToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (_isUpdatingEditorPerformanceModeSelection)
            {
                return;
            }

            SettingsService.SaveEditorPerformanceModeEnabled(EditorPerformanceModeToggle.IsOn);
        }

        private async void StartWithWindowsToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (_isUpdatingStartWithWindowsSelection || !StartupLaunchService.IsSupported)
            {
                return;
            }

            if (StartWithWindowsToggle.IsOn)
            {
                SettingsService.SaveRunAtWindowsStartup(true);
                var state = await StartupLaunchService.RequestEnableAsync();
                if (state != StartupTaskState.Enabled)
                {
                    SettingsService.SaveRunAtWindowsStartup(false);
                    _isUpdatingStartWithWindowsSelection = true;
                    try
                    {
                        StartWithWindowsToggle.IsOn = false;
                    }
                    finally
                    {
                        _isUpdatingStartWithWindowsSelection = false;
                    }

                    InAppToastService.Show(
                        "Startup was not enabled. You can turn it on in Windows Settings → Apps → Startup.",
                        InAppToastSeverity.Warning);
                }
            }
            else
            {
                SettingsService.SaveRunAtWindowsStartup(false);
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
                Content = "This clears all saved app settings and restores defaults (including Start with Windows and global hotkey listeners). Files on disk (screenshots and exports) are not deleted.",
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
            if (StartupLaunchService.IsSupported && SettingsService.Load().RunAtWindowsStartup)
            {
                _ = StartupLaunchService.RequestEnableAsync();
            }

            InAppToastService.Show("All settings were reset to defaults.", InAppToastSeverity.Success);
        }
    }
}
