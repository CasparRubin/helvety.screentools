using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;

namespace helvety.screenshots.Views
{
    public sealed partial class ScreenshotsPage : Page
    {
        public ScreenshotsPage()
        {
            InitializeComponent();
            SettingsService.SaveFolderPathChanged += SettingsService_SaveFolderPathChanged;
            Unloaded += ScreenshotsPage_Unloaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            RefreshEmptyStateMessage();
        }

        private void SettingsService_SaveFolderPathChanged()
        {
            DispatcherQueue.TryEnqueue(RefreshEmptyStateMessage);
        }

        private void ScreenshotsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            SettingsService.SaveFolderPathChanged -= SettingsService_SaveFolderPathChanged;
            Unloaded -= ScreenshotsPage_Unloaded;
        }

        private void RefreshEmptyStateMessage()
        {
            var hasSaveFolder = SettingsService.TryGetEffectiveSaveFolderPath(out var folderPath);
            var hasHotkey = SettingsService.TryGetEffectiveHotkey(out var hotkey);

            if (!hasSaveFolder)
            {
                EmptyStateMessageText.Text = "Set a save location to enable screenshots.";
                EmptyFolderCallout.Visibility = Visibility.Visible;
                return;
            }

            if (!hasHotkey)
            {
                EmptyStateMessageText.Text = "Set a key-binding to enable screenshots.";
                EmptyFolderCallout.Visibility = Visibility.Visible;
                return;
            }

            EmptyStateMessageText.Text = $"Press {hotkey.Display} to create your first screenshot.";

            var isEmptyFolder = Directory.Exists(folderPath) &&
                                !Directory.EnumerateFileSystemEntries(folderPath).Any();
            EmptyFolderCallout.Visibility = isEmptyFolder
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }
}
