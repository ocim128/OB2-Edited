using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.ViewModels;
using OpenBullet2.Native.Views.Pages.Shared;
using RuriLib.Models.Configs;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using OpenBullet2.Native.Infrastructure.DependencyInjection;

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
        private readonly DispatcherTimer autoSaveTimer;
        private readonly OpenBulletSettingsService obSettingsService;

        // Public property to allow external access to the stacker controls panel
        public DockPanel GetStackerControlsPanel() => StackerControlsPanel;

        // Public method to check if current content is stacker page
        public bool IsStackerPageActive() => editorFrame.Content == stackerPage;

        // Public method to toggle editor frame visibility for stacker content
        public void SetEditorFrameVisibility(bool isVisible)
        {
            editorFrame.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public ConfigEditor()
        {
            mainWindow = ServiceLocator.GetService<MainWindow>();
            vm = new ConfigEditorViewModel();
            DataContext = vm;
            obSettingsService = ServiceLocator.GetService<OpenBulletSettingsService>();

            InitializeComponent();

            editorFrame.Navigated += (_, _) => UpdateButtonsVisibility();

            // Create debugger only (essential for initial load)
            debugger = new();
            debuggerFrame.Content = debugger;
            
            // Set up GridSplitter event handlers for better performance
            SetupGridSplitterEvents();

            // Lazy load other pages on demand
            stackerPage = null;
            loliCodePage = null;
            cSharpPage = null;

            // Set up auto-save timer with optimized interval
            autoSaveTimer = new DispatcherTimer();
            autoSaveTimer.Interval = TimeSpan.FromSeconds(Math.Max(5, obSettingsService.Settings.GeneralSettings.AutoSaveInterval));
            autoSaveTimer.Tick += async (_, _) => await AutoSave();
            autoSaveTimer.Start();
        }

        // Public method to update UI when config is loaded
        public void UpdateUI()
        {
            UpdateButtonsVisibility();
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


            }
        }

        private void UpdateButtonsVisibility()
        {
            var isStackOrLoliCode = vm.Config.Mode == ConfigMode.Stack || vm.Config.Mode == ConfigMode.LoliCode;
            var currentContent = editorFrame.Content;

            // Show the entire panel when we have a valid config mode
            StackerControlsPanel.Visibility = isStackOrLoliCode ? Visibility.Visible : Visibility.Collapsed;

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
        
        /// <summary>
        /// Sets up GridSplitter event handlers to optimize debugger performance during resize operations.
        /// </summary>
        private void SetupGridSplitterEvents()
        {
            // Find the GridSplitter in the visual tree
            var gridSplitter = FindVisualChild<System.Windows.Controls.GridSplitter>(this);
            if (gridSplitter != null)
            {
                gridSplitter.DragStarted += OnGridSplitterDragStarted;
                gridSplitter.DragCompleted += OnGridSplitterDragCompleted;
            }
        }
        
        /// <summary>
        /// Handles GridSplitter drag start to suspend debugger updates.
        /// </summary>
        private void OnGridSplitterDragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            if (debugger != null)
            {
                // Notify debugger that resizing has started
                debugger.SetResizing(true);
            }
        }
        
        /// <summary>
        /// Handles GridSplitter drag completion to resume debugger updates.
        /// </summary>
        private void OnGridSplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (debugger != null)
            {
                // Notify debugger that resizing has completed
                debugger.SetResizing(false);
            }
        }
        
        /// <summary>
        /// Helper method to find a child of a specific type in the visual tree.
        /// </summary>
        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;
                
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }
    }

    public enum ConfigEditorSection
    {
        Stacker,
        LoliCode,
        CSharp
    }

    public class ConfigEditorViewModel : OpenBullet2.Native.ViewModels.Infrastructure.ViewModelBase
    {
        private readonly IConfigRepository configRepo;
        private readonly ConfigService configService;
        public Config Config => configService.SelectedConfig;

        public ConfigEditorViewModel()
        {
            configRepo = ServiceLocator.GetService<IConfigRepository>();
            configService = ServiceLocator.GetService<ConfigService>();
        }

        public async Task Save()
        {
            await configRepo.SaveAsync(Config);
            Config.UpdateHashes();
        }
    }
}
