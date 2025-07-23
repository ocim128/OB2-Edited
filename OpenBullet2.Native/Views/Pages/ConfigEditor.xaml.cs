using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.ViewModels;
using OpenBullet2.Native.Views.Pages.Shared;
using RuriLib.Models.Configs;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace OpenBullet2.Native.Views.Pages
{
    /// <summary>
    /// Interaction logic for ConfigEditor.xaml
    /// </summary>
    public partial class ConfigEditor : Page
    {
        private readonly MainWindow mainWindow;
        private readonly ConfigEditorViewModel vm;
        private readonly Debugger debugger;
        private ConfigStacker stackerPage;
        private ConfigLoliCode loliCodePage;
        private ConfigCSharpCode cSharpPage;
        private ConfigLoliScript loliScriptPage;
        private readonly DispatcherTimer autoSaveTimer;
        private readonly OpenBulletSettingsService obSettingsService;

        public ConfigEditor()
        {
            mainWindow = SP.GetService<MainWindow>();
            vm = new ConfigEditorViewModel();
            DataContext = vm;
            obSettingsService = SP.GetService<OpenBulletSettingsService>();

            InitializeComponent();

            editorFrame.Navigated += (_, _) => UpdateButtonsVisibility();

            // Create debugger only (essential for initial load)
            debugger = new();
            debuggerFrame.Content = debugger;

            // Lazy load other pages on demand
            stackerPage = null;
            loliCodePage = null;
            cSharpPage = null;
            loliScriptPage = null;

            // Set up auto-save timer with optimized interval
            autoSaveTimer = new DispatcherTimer();
            autoSaveTimer.Interval = TimeSpan.FromSeconds(Math.Max(5, obSettingsService.Settings.GeneralSettings.AutoSaveInterval));
            autoSaveTimer.Tick += async (_, _) => await AutoSave();
            autoSaveTimer.Start();
        }

        public void NavigateTo(ConfigEditorSection section)
        {
            switch (section)
            {
                case ConfigEditorSection.Stacker:
                    stackerPage ??= new ConfigStacker();
                    stackerPage.UpdateViewModel();
                    editorFrame.Content = stackerPage;
                    break;

                case ConfigEditorSection.LoliCode:
                    loliCodePage ??= new ConfigLoliCode();
                    loliCodePage.UpdateViewModel();
                    editorFrame.Content = loliCodePage;
                    break;

                case ConfigEditorSection.CSharp:
                    cSharpPage ??= new ConfigCSharpCode();
                    cSharpPage.UpdateViewModel();
                    editorFrame.Content = cSharpPage;
                    break;

                case ConfigEditorSection.LoliScript:
                    loliScriptPage ??= new ConfigLoliScript();
                    loliScriptPage.UpdateViewModel();
                    editorFrame.Content = loliScriptPage;
                    break;
            }
        }

        private void UpdateButtonsVisibility()
        {
            var isStackOrLoliCode = vm.Config.Mode == ConfigMode.Stack || vm.Config.Mode == ConfigMode.LoliCode;
            var currentContent = editorFrame.Content;

            stackerButton.Visibility = isStackOrLoliCode && currentContent != stackerPage
                ? Visibility.Visible : Visibility.Collapsed;

            loliCodeButton.Visibility = isStackOrLoliCode && currentContent != loliCodePage
                ? Visibility.Visible : Visibility.Collapsed;

            cSharpButton.Visibility = isStackOrLoliCode && currentContent != cSharpPage
                ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Call this when changing page via the dropdown menu otherwise it
        /// will not save the content of the LoliCode editor.
        /// </summary>
        public void OnPageChanged()
        {
            if (editorFrame.Content == loliCodePage)
            {
                loliCodePage.OnPageChanged();
            }
        }

        private void OpenStacker(object sender, RoutedEventArgs e) => mainWindow.NavigateTo(MainWindowPage.ConfigStacker);
        private void OpenLoliCode(object sender, RoutedEventArgs e) => mainWindow.NavigateTo(MainWindowPage.ConfigLoliCode);
        private void OpenCSharpCode(object sender, RoutedEventArgs e) => mainWindow.NavigateTo(MainWindowPage.ConfigCSharpCode);

        public async void Save(object sender, RoutedEventArgs e)
        {
            try
            {
                await vm.Save();
                Alert.Success("Success", $"{vm.Config.Metadata.Name} was saved successfully!");
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async Task AutoSave()
        {
            if (vm.Config?.HasUnsavedChanges() == true)
            {
                try
                {
                    await vm.Save();
                }
                catch (Exception ex)
                {
                    Alert.Exception(ex);
                }
            }
        }
    }

    public enum ConfigEditorSection
    {
        Stacker,
        LoliCode,
        CSharp,
        LoliScript
    }

    public class ConfigEditorViewModel : ViewModelBase
    {
        private readonly IConfigRepository configRepo;
        private readonly ConfigService configService;
        public Config Config => configService.SelectedConfig;

        public ConfigEditorViewModel()
        {
            configRepo = SP.GetService<IConfigRepository>();
            configService = SP.GetService<ConfigService>();
        }

        public async Task Save()
        {
            await configRepo.SaveAsync(Config);
            Config.UpdateHashes();
        }
    }
}
