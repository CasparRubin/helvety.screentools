using helvety.screenshots.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;

namespace helvety.screenshots
{
    public sealed partial class MainWindow : Window
    {
        private const string UseDefaultSaveFolderActionTag = "use-default-save-folder";
        private const string UseDefaultHotkeyActionTag = "use-default-hotkey";
        private readonly ObservableCollection<GlobalSetupIssue> _globalIssues = new();

        public MainWindow()
        {
            InitializeComponent();
            GlobalIssuesItemsControl.ItemsSource = _globalIssues;
            SettingsService.SettingsChanged += SettingsService_SettingsChanged;
            NavigateToTag("screenshots");
            AppNavigationView.SelectedItem = AppNavigationView.MenuItems[0];
            RefreshGlobalIssues();
        }

        private void SettingsService_SettingsChanged()
        {
            DispatcherQueue.TryEnqueue(RefreshGlobalIssues);
        }

        private void AppNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer?.Tag is not string tag)
            {
                return;
            }

            NavigateToTag(tag);
            RefreshGlobalIssues();
        }

        private void NavigateToTag(string tag)
        {
            var targetPage = tag switch
            {
                "screenshots" => typeof(ScreenshotsPage),
                "settings" => typeof(SettingsPage),
                _ => typeof(ScreenshotsPage)
            };

            if (ContentFrame.CurrentSourcePageType != targetPage)
            {
                ContentFrame.Navigate(targetPage);
            }
        }

        private void RefreshGlobalIssues()
        {
            _globalIssues.Clear();
            foreach (var issue in SettingsService.GetGlobalSetupIssues())
            {
                _globalIssues.Add(issue);
            }

            GlobalIssuesItemsControl.Visibility = _globalIssues.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void GlobalIssueActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not HyperlinkButton button || button.Tag is not string tag)
            {
                return;
            }

            if (tag == UseDefaultSaveFolderActionTag)
            {
                var defaultPath = SettingsService.GetDefaultDesktopFolderPath();
                if (SettingsService.TryValidateWritableFolder(defaultPath, out _))
                {
                    SettingsService.SaveFolderPath(defaultPath);
                }
                return;
            }

            if (tag == UseDefaultHotkeyActionTag)
            {
                var defaultHotkey = SettingsService.GetDefaultHotkey();
                SettingsService.SaveHotkey(defaultHotkey.Modifiers, defaultHotkey.Sequence, defaultHotkey.Display);
                return;
            }

            foreach (var menuItem in AppNavigationView.MenuItems)
            {
                if (menuItem is NavigationViewItem item && item.Tag as string == tag)
                {
                    AppNavigationView.SelectedItem = item;
                    break;
                }
            }

            NavigateToTag(tag);
        }
    }
}
