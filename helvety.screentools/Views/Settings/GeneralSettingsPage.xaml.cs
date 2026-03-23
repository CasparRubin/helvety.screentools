using helvety.screentools;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace helvety.screentools.Views.Settings
{
    public sealed partial class GeneralSettingsPage : Page
    {
        private string _saveFolderPath = string.Empty;
        private bool _hasValidSaveFolder;

        public GeneralSettingsPage()
        {
            InitializeComponent();
            SettingsService.SaveFolderPathChanged += SettingsService_SaveFolderPathChanged;
            Unloaded += GeneralSettingsPage_Unloaded;

            if (SettingsService.TryGetEffectiveSaveFolderPath(out var effectiveSaveFolderPath))
            {
                _saveFolderPath = effectiveSaveFolderPath;
            }
            else
            {
                var settings = SettingsService.Load();
                _saveFolderPath = settings.IsSaveFolderCleared
                    ? string.Empty
                    : settings.SaveFolderPath ?? string.Empty;
            }

            UpdateSaveFolderState();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            RefreshSaveFolderState();
        }

        private void GeneralSettingsPage_Unloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            SettingsService.SaveFolderPathChanged -= SettingsService_SaveFolderPathChanged;
            Unloaded -= GeneralSettingsPage_Unloaded;
        }

        private void SettingsService_SaveFolderPathChanged()
        {
            DispatcherQueue.TryEnqueue(RefreshSaveFolderState);
        }

        private void RefreshSaveFolderState()
        {
            if (SettingsService.TryGetEffectiveSaveFolderPath(out var effectiveSaveFolderPath))
            {
                _saveFolderPath = effectiveSaveFolderPath;
            }
            else
            {
                var settings = SettingsService.Load();
                _saveFolderPath = settings.IsSaveFolderCleared
                    ? string.Empty
                    : settings.SaveFolderPath ?? string.Empty;
            }

            UpdateSaveFolderState();
        }

        private void UpdateSaveFolderState()
        {
            if (string.IsNullOrWhiteSpace(_saveFolderPath))
            {
                _hasValidSaveFolder = false;
                SaveFolderText.Text = "Save Folder: (none)";
                SaveFolderStatusText.Text = "No save location set.";
                RemoveSaveFolderButton.IsEnabled = false;
                return;
            }

            if (SettingsService.TryValidateWritableFolder(_saveFolderPath, out var validationError))
            {
                _hasValidSaveFolder = true;
                SettingsService.SaveFolderPath(_saveFolderPath);
                SaveFolderText.Text = $"Save Folder: {_saveFolderPath}";
                SaveFolderStatusText.Text = string.Empty;
                RemoveSaveFolderButton.IsEnabled = true;
                return;
            }

            _hasValidSaveFolder = false;
            SaveFolderText.Text = $"Save Folder: {_saveFolderPath}";
            SaveFolderStatusText.Text = $"Choose a writable folder ({validationError}).";
            RemoveSaveFolderButton.IsEnabled = true;
        }

        private async void ChooseSaveFolderButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ChooseSaveFolderButton.IsEnabled = false;
            UseDefaultSaveFolderButton.IsEnabled = false;
            RemoveSaveFolderButton.IsEnabled = false;
            SaveFolderStatusText.Text = "Choosing folder...";

            try
            {
                var folderPicker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.Desktop
                };
                folderPicker.FileTypeFilter.Add("*");

                if (App.MainAppWindow is null)
                {
                    SaveFolderStatusText.Text = "Unable to open folder picker.";
                    return;
                }

                var windowHandle = WindowNative.GetWindowHandle(App.MainAppWindow);
                InitializeWithWindow.Initialize(folderPicker, windowHandle);

                var selectedFolder = await folderPicker.PickSingleFolderAsync();
                if (selectedFolder is null)
                {
                    SaveFolderStatusText.Text = _hasValidSaveFolder
                        ? string.Empty
                        : "Choose a writable folder.";
                    return;
                }

                var candidatePath = selectedFolder.Path;
                if (!SettingsService.TryValidateWritableFolder(candidatePath, out var validationError))
                {
                    SaveFolderStatusText.Text = _hasValidSaveFolder
                        ? $"Folder not writable ({validationError})."
                        : $"Choose a writable folder ({validationError}).";
                    return;
                }

                _saveFolderPath = candidatePath;
                SettingsService.SaveFolderPath(_saveFolderPath);
                UpdateSaveFolderState();
            }
            catch (Exception ex)
            {
                SaveFolderStatusText.Text = $"Could not set folder ({ex.Message}).";
            }
            finally
            {
                ChooseSaveFolderButton.IsEnabled = true;
                UseDefaultSaveFolderButton.IsEnabled = true;
                RemoveSaveFolderButton.IsEnabled = !string.IsNullOrWhiteSpace(_saveFolderPath);
            }
        }

        private void UseDefaultSaveFolderButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (!SettingsService.TryEnsureDefaultDesktopFolder(out var defaultPath))
            {
                SaveFolderStatusText.Text = "Could not create default folder.";
                return;
            }

            if (!SettingsService.TryValidateWritableFolder(defaultPath, out var validationError))
            {
                SaveFolderStatusText.Text = $"Default folder not writable ({validationError}).";
                return;
            }

            _saveFolderPath = defaultPath;
            SettingsService.SaveFolderPath(_saveFolderPath);
            UpdateSaveFolderState();
        }

        private void RemoveSaveFolderButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            SettingsService.ClearSaveFolderPath();
            _saveFolderPath = string.Empty;
            UpdateSaveFolderState();
        }
    }
}
