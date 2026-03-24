using helvety.screentools.Views;
using helvety.screentools.Views.Settings;
using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Text;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;

namespace helvety.screentools
{
    /// <summary>
    /// Main shell: navigation to General (home), settings sections (General, Screen capture, Live Draw), and About; global issue banners; in-app toasts; tray integration.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private const string UseDefaultSaveFolderActionTag = "use-default-save-folder";
        private const string UseDefaultHotkeyActionTag = "use-default-hotkey";
        private const string UseDefaultLiveDrawHotkeyActionTag = "use-default-live-draw-hotkey";
        private const int MaxVisibleToasts = 6;
        private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(5.2);
        private static readonly TimeSpan ToastFadeOutDuration = TimeSpan.FromMilliseconds(220);
        private readonly ObservableCollection<GlobalSetupIssue> _globalIssues = new();
        private bool _allowFullExit;
        internal bool IsHiddenToTray { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
            ApplyWindowIcon();
            GlobalIssuesItemsControl.ItemsSource = _globalIssues;
            SettingsService.SettingsChanged += SettingsService_SettingsChanged;
            App.SessionStatusPublished += App_SessionStatusPublished;
            InAppToastService.ToastRequested += InAppToastService_ToastRequested;
            Closed += MainWindow_Closed;
            NavigateToTag("home");
            AppNavigationView.SelectedItem = AppNavigationView.MenuItems[0];
            RefreshGlobalIssues();
        }

        private void ApplyWindowIcon()
        {
            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "TrayIcon.ico");
                if (!File.Exists(iconPath))
                {
                    return;
                }

                var hwnd = WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);
                appWindow.SetIcon(iconPath);
            }
            catch
            {
                // Keep startup resilient if the icon cannot be applied on some environments.
            }
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (!_allowFullExit && SettingsService.Load().MinimizeToTrayOnClose)
            {
                args.Handled = true;
                HideToTray();
                return;
            }

            SettingsService.SettingsChanged -= SettingsService_SettingsChanged;
            App.SessionStatusPublished -= App_SessionStatusPublished;
            InAppToastService.ToastRequested -= InAppToastService_ToastRequested;
            Closed -= MainWindow_Closed;
        }

        internal void RestoreFromTray()
        {
            if (!IsHiddenToTray)
            {
                Activate();
                return;
            }

            WindowExtensions.Show(this);
            IsHiddenToTray = false;
            Activate();
        }

        internal void RequestFullExit()
        {
            _allowFullExit = true;
        }

        private void HideToTray()
        {
            if (IsHiddenToTray)
            {
                return;
            }

            WindowExtensions.Hide(this);
            IsHiddenToTray = true;
        }

        private void SettingsService_SettingsChanged()
        {
            DispatcherQueue.TryEnqueue(RefreshGlobalIssues);
        }

        private void App_SessionStatusPublished(string message)
        {
            DispatcherQueue.TryEnqueue(() => ShowInAppToast(message, InAppToastSeverity.Informational));
        }

        private void InAppToastService_ToastRequested(InAppToastMessage message)
        {
            DispatcherQueue.TryEnqueue(() => ShowInAppToast(message.Message, message.Severity));
        }

        private void ShowInAppToast(string message, InAppToastSeverity severity)
        {
            var card = BuildToastCard(message, severity);
            InAppToastHostPanel.Children.Insert(0, card);
            if (InAppToastHostPanel.Children.Count > MaxVisibleToasts)
            {
                InAppToastHostPanel.Children.RemoveAt(InAppToastHostPanel.Children.Count - 1);
            }

            _ = AutoDismissToastAsync(card);
        }

        private static Brush GetToastAccentBrush(InAppToastSeverity severity)
        {
            return severity switch
            {
                InAppToastSeverity.Success => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 124, 16)),
                InAppToastSeverity.Warning => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 157, 93, 0)),
                InAppToastSeverity.Error => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 196, 43, 28)),
                _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 95, 184))
            };
        }

        private static string GetToastTitle(InAppToastSeverity severity)
        {
            return severity switch
            {
                InAppToastSeverity.Success => "Success",
                InAppToastSeverity.Warning => "Warning",
                InAppToastSeverity.Error => "Error",
                _ => "Helvety Screen Tools"
            };
        }

        private static Border BuildToastCard(string message, InAppToastSeverity severity)
        {
            var titleTextBlock = new TextBlock
            {
                Text = GetToastTitle(severity),
                FontWeight = FontWeights.SemiBold
            };

            var messageTextBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.WrapWholeWords
            };

            var textStack = new StackPanel
            {
                Spacing = 4
            };
            textStack.Children.Add(titleTextBlock);
            textStack.Children.Add(messageTextBlock);

            var accentBar = new Border
            {
                Width = 4,
                CornerRadius = new CornerRadius(2),
                Background = GetToastAccentBrush(severity)
            };

            var layoutGrid = new Grid
            {
                ColumnSpacing = 10
            };
            layoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var copyButton = new Button
            {
                Content = "Copy",
                MinWidth = 64,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            };
            ToolTipService.SetToolTip(copyButton, "Copy toast text");
            copyButton.Click += (_, _) =>
            {
                var package = new DataPackage();
                package.SetText(message);
                Clipboard.SetContent(package);
                Clipboard.Flush();
            };

            Grid.SetColumn(accentBar, 0);
            Grid.SetColumn(textStack, 1);
            Grid.SetColumn(copyButton, 2);
            layoutGrid.Children.Add(accentBar);
            layoutGrid.Children.Add(textStack);
            layoutGrid.Children.Add(copyButton);

            var toastCard = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(242, 20, 20, 20)),
                BorderBrush = TryGetThemeBrush("CardStrokeColorDefaultBrush", new SolidColorBrush(Windows.UI.Color.FromArgb(255, 70, 70, 70))),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                Opacity = 1,
                Child = layoutGrid,
                Transitions = new TransitionCollection
                {
                    new EntranceThemeTransition
                    {
                        FromVerticalOffset = -20
                    }
                }
            };

            return toastCard;
        }

        private static Brush TryGetThemeBrush(string key, Brush fallback)
        {
            if (Application.Current.Resources.TryGetValue(key, out var resource) && resource is Brush brush)
            {
                return brush;
            }

            return fallback;
        }

        private async Task AutoDismissToastAsync(Border toastCard)
        {
            await Task.Delay(ToastDuration);
            if (!DispatcherQueue.TryEnqueue(() => FadeAndRemoveToast(toastCard)))
            {
                return;
            }
        }

        private void FadeAndRemoveToast(Border toastCard)
        {
            if (!InAppToastHostPanel.Children.Contains(toastCard))
            {
                return;
            }

            var fadeOutAnimation = new DoubleAnimation
            {
                To = 0,
                Duration = new Duration(ToastFadeOutDuration),
                EnableDependentAnimation = true
            };

            var storyboard = new Storyboard();
            Storyboard.SetTarget(fadeOutAnimation, toastCard);
            Storyboard.SetTargetProperty(fadeOutAnimation, "Opacity");
            storyboard.Children.Add(fadeOutAnimation);
            storyboard.Completed += (_, _) =>
            {
                InAppToastHostPanel.Children.Remove(toastCard);
            };
            storyboard.Begin();
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

        private static NavigationViewItem? FindNavigationViewItemByTag(IList<object> items, string tag)
        {
            var searchTag = tag == "settings" ? "general" : tag;
            foreach (var o in items)
            {
                if (o is not NavigationViewItem nvi)
                {
                    continue;
                }

                if (nvi.Tag as string == searchTag)
                {
                    return nvi;
                }

                if (nvi.MenuItems.Count > 0)
                {
                    var nested = FindNavigationViewItemByTag(nvi.MenuItems, tag);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private void NavigateToTag(string tag)
        {
            var normalized = tag == "settings" ? "general" : tag;
            var targetPage = normalized switch
            {
                "home" => typeof(ScreenToolsPage),
                "about" => typeof(AboutPage),
                "general" => typeof(GeneralSettingsPage),
                "capture" => typeof(CaptureHotkeySettingsPage),
                "livedraw" => typeof(LiveDrawSettingsPage),
                _ => typeof(ScreenToolsPage)
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
                if (!SettingsService.TryEnsureDefaultDesktopFolder(out var defaultPath))
                {
                    ShowInAppToast("Could not create default save folder.", InAppToastSeverity.Error);
                    return;
                }

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

            if (tag == UseDefaultLiveDrawHotkeyActionTag)
            {
                var defaultLiveDraw = SettingsService.GetDefaultLiveDrawHotkey();
                SettingsService.SaveLiveDrawHotkey(defaultLiveDraw.Modifiers, defaultLiveDraw.Sequence, defaultLiveDraw.Display);
                return;
            }

            var navItem = FindNavigationViewItemByTag(AppNavigationView.MenuItems, tag);
            if (navItem is not null)
            {
                AppNavigationView.SelectedItem = navItem;
            }

            NavigateToTag(tag);
        }
    }
}
