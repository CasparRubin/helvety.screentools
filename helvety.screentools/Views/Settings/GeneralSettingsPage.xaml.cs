using helvety.screentools;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace helvety.screentools.Views.Settings
{
    public sealed partial class GeneralSettingsPage : Page
    {
        private bool _isUpdatingMinimizeToTraySelection;
        private bool _isUpdatingEditorPerformanceModeSelection;

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
        }

        private void SettingsService_SettingsChanged()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                InitializeMinimizeToTraySelection();
                InitializeEditorPerformanceModeSelection();
            });
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            InitializeMinimizeToTraySelection();
            InitializeEditorPerformanceModeSelection();
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
    }
}
