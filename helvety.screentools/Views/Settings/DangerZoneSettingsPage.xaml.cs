using helvety.screentools;
using Microsoft.UI.Xaml.Controls;
using System;

namespace helvety.screentools.Views.Settings
{
    public sealed partial class DangerZoneSettingsPage : Page
    {
        public DangerZoneSettingsPage()
        {
            InitializeComponent();
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
