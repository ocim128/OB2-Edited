using OpenBullet2.Core.Models.Settings;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.DTOs;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.ViewModels;
using OpenBullet2.Native.Views.Dialogs;
using RuriLib.Models.Configs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using RuriLib.Helpers;

namespace OpenBullet2.Native.Views.Pages
{
    /// <summary>
    /// Interaction logic for Configs.xaml
    /// </summary>
    public partial class Configs : Page
    {
        private readonly OpenBulletSettingsService obSettingsService;
        private readonly ConfigService configService;
        private readonly ConfigsViewModel vm;
        private readonly VolatileSettingsService volatileSettings;
        private GridViewColumnHeader listViewSortCol;
        private SortAdorner listViewSortAdorner;
        private Point dragStartPoint;

        private IEnumerable<ConfigViewModel> SelectedConfigs => configsListView.SelectedItems.Cast<ConfigViewModel>();

        private ConfigViewModel HoveredItem => (ConfigViewModel)configsListView.SelectedItem;
        
        private string ListViewSortBy
        {
            get => volatileSettings.ListViewSorting["configs"].By;
            set => volatileSettings.ListViewSorting["configs"].By = value;
        }
        
        private ListSortDirection ListViewSortDir
        {
            get => volatileSettings.ListViewSorting["configs"].Direction;
            set => volatileSettings.ListViewSorting["configs"].Direction = value;
        }

        public Configs()
        {
            obSettingsService = SP.GetService<OpenBulletSettingsService>();
            configService = SP.GetService<ConfigService>();
            volatileSettings = SP.GetService<VolatileSettingsService>();
            vm = SP.GetService<ViewModelsService>().Configs;
            DataContext = vm;
            
            InitializeComponent();
        }

        // This is needed otherwise if properties of a config are updated by another page this page will not
        // get notified and will show the old values.
        public void UpdateViewModel()
        {
            vm.SelectedConfig?.UpdateViewModel();

            if (!string.IsNullOrEmpty(ListViewSortBy))
            {
                configsListView.Items.SortDescriptions.Add(new SortDescription(ListViewSortBy, ListViewSortDir));
            }
        }

        public void Create(object sender, RoutedEventArgs e)
            => new MainDialog(new CreateConfigDialog(this), "Create config").ShowDialog();

        public async void CreateConfig(ConfigForCreationDto dto) => await vm.CreateAsync(dto);

        public void Edit(object sender, RoutedEventArgs e) => EditConfig();

        public async void Save(object sender, RoutedEventArgs e)
        {
            if (vm.SelectedConfig is null)
            {
                ShowNoConfigSelectedError();
                return;
            }

            try
            {
                await vm.Save(vm.SelectedConfig);
                Alert.Success("Success", $"{vm.SelectedConfig.Config.Metadata.Name} was saved successfully!");
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void Delete(object sender, RoutedEventArgs e)
        {
            if (HoveredItem is null)
            {
                ShowNoConfigSelectedError();
                return;
            }

            if (Alert.Choice("Are you sure?", $"Do you really want to delete {HoveredItem.Name}? This cannot be undone."))
            {
                vm.Delete(HoveredItem);
            }
        }

        private void DeleteSelected(object sender, RoutedEventArgs e)
        {
            if (!SelectedConfigs.Any())
            {
                Alert.Error("No configs selected", "Please select at least one config to delete.");
                return;
            }

            if (Alert.Choice("Are you sure?", $"Do you really want to delete {SelectedConfigs.Count()} selected configs? This cannot be undone."))
            {
                foreach (var config in SelectedConfigs)
                {
                    vm.Delete(config);
                }
            }
        }

        private async void ExportSelected(object sender, RoutedEventArgs e)
        {
            if (!SelectedConfigs.Any())
            {
                Alert.Error("No configs selected", "Please select at least one config to export.");
                return;
            }

            var sfd = new SaveFileDialog
            {
                Filter = "OpenBullet Config |*.opk",
                Title = "Export configs"
            };
            sfd.ShowDialog();

            if (!string.IsNullOrWhiteSpace(sfd.FileName))
            {
                try
                {
                    var bytes = await ConfigPacker.PackAsync(SelectedConfigs.Select(c => c.Config));
                    await File.WriteAllBytesAsync(sfd.FileName, bytes);
                    Alert.Success("Success", "Successfully exported the selected configs");
                }
                catch (Exception ex)
                {
                    Alert.Exception(ex);
                }
            }
        }

        // TODO: Check if current config is not saved and prompt warning
        public async void Rescan(object sender, RoutedEventArgs e) => await vm.RescanAsync();

        public async Task PerformRescanAsync() => await vm.RescanAsync();

        private void OpenFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start("explorer.exe", Path.Combine(Directory.GetCurrentDirectory(), "UserData\\Configs"));
            }
            catch (Exception ex)
            {
                // This happens on access denied
                Alert.Exception(ex);
            }
        }

        private void ShowNoConfigSelectedError() => Alert.Error("No config selected", "Please select a config first!");

        private void NavigateToConfigSection()
        {
            var mode = vm.SelectedConfig.Config.Mode;
            var page = obSettingsService.Settings.GeneralSettings.ConfigSectionOnLoad switch
            {
                ConfigSection.Metadata => MainWindowPage.ConfigMetadata,
                ConfigSection.Readme => MainWindowPage.ConfigReadme,
                ConfigSection.Stacker => mode switch
                {
                    ConfigMode.LoliCode or ConfigMode.Stack => MainWindowPage.ConfigStacker,
                    ConfigMode.CSharp => MainWindowPage.ConfigCSharpCode,
                    ConfigMode.Legacy => MainWindowPage.ConfigLoliScript,
                    _ => MainWindowPage.ConfigMetadata
                },
                ConfigSection.LoliCode => mode switch
                {
                    ConfigMode.LoliCode or ConfigMode.Stack => MainWindowPage.ConfigLoliCode,
                    ConfigMode.CSharp => MainWindowPage.ConfigCSharpCode,
                    ConfigMode.Legacy => MainWindowPage.ConfigLoliScript,
                    _ => MainWindowPage.ConfigMetadata
                },
                ConfigSection.Settings => MainWindowPage.ConfigSettings,
                ConfigSection.CSharpCode => mode switch
                {
                    ConfigMode.LoliCode or ConfigMode.Stack or ConfigMode.CSharp  => MainWindowPage.ConfigLoliCode,
                    ConfigMode.Legacy => MainWindowPage.ConfigLoliScript,
                    _ => MainWindowPage.ConfigMetadata
                },
                ConfigSection.LoliScript => mode switch
                {
                    ConfigMode.LoliCode or ConfigMode.Stack => MainWindowPage.ConfigLoliCode,
                    ConfigMode.CSharp => MainWindowPage.ConfigCSharpCode,
                    ConfigMode.Legacy => MainWindowPage.ConfigLoliScript,
                    _ => MainWindowPage.ConfigMetadata
                },
                _ => throw new NotImplementedException(),
            };

            SP.GetService<MainWindow>().NavigateTo(page);
        }

        private void UpdateSearch(object sender, TextChangedEventArgs e)
        {
            vm.SearchString = filterTextbox.Text;
        }

        private void Search(object sender, RoutedEventArgs e) { }

        private void ItemHovered(object sender, SelectionChangedEventArgs e)
        {
            var items = e.AddedItems as IList<object>;

            if (items.Count == 1)
            {
                vm.HoveredConfig = items[0] as ConfigViewModel;
            }
        }

        private void ListItemDoubleClick(object sender, MouseButtonEventArgs e) => EditConfig();

        private void EditConfig()
        {
            if (HoveredItem is null)
            {
                ShowNoConfigSelectedError();
                return;
            }

            if (HoveredItem.Config.IsRemote)
            {
                Alert.Error("Remote", "You cannot edit remote configs!");
                return;
            }

            // Check if the config was saved
            if (obSettingsService.Settings.GeneralSettings.WarnConfigNotSaved
                && configService.SelectedConfig != null
                && configService.SelectedConfig.HasUnsavedChanges()
                && !Alert.Confirm("Config not saved", $"The currently selected config ({configService.SelectedConfig.Metadata.Name}) has unsaved changes," +
                    $" are you sure you want to edit another config?", nameof(obSettingsService.Settings.GeneralSettings.WarnConfigNotSaved)))
            {
                return;
            }

            vm.SelectedConfig = HoveredItem;
            SP.GetService<ViewModelsService>().Debugger.ClearLog();
            NavigateToConfigSection();
        }

        private void ColumnHeaderClicked(object sender, RoutedEventArgs e)
        {
            var column = sender as GridViewColumnHeader;
            ListViewSortBy = column.Tag.ToString();

            if (listViewSortCol != null)
            {
                AdornerLayer.GetAdornerLayer(listViewSortCol).Remove(listViewSortAdorner);
                configsListView.Items.SortDescriptions.Clear();
            }

            ListViewSortDir = ListSortDirection.Ascending;

            if (listViewSortCol == column && listViewSortAdorner.Direction == ListViewSortDir)
            {
                ListViewSortDir = ListSortDirection.Descending;
            }

            listViewSortCol = column;
            listViewSortAdorner = new SortAdorner(listViewSortCol, ListViewSortDir);
            AdornerLayer.GetAdornerLayer(listViewSortCol).Add(listViewSortAdorner);
            configsListView.Items.SortDescriptions.Add(new SortDescription(ListViewSortBy, ListViewSortDir));
        }

        private void configsListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            dragStartPoint = e.GetPosition(null);
        }

        private void configsListView_MouseMove(object sender, MouseEventArgs e)
        {
            var mousePos = e.GetPosition(null);
            var diff = dragStartPoint - mousePos;

            if (e.LeftButton == MouseButtonState.Pressed &&
                (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                 Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
            {
                var listView = sender as ListView;
                var listViewItem = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);

                if (listViewItem == null) return;

                // Get the dragged item
                var config = (ConfigViewModel)listView.ItemContainerGenerator.ItemFromContainer(listViewItem);

                // Initialize the drag & drop operation
                var dragData = new DataObject("myFormat", config);
                DragDrop.DoDragDrop(listViewItem, dragData, DragDropEffects.Move);
            }
        }

        private async void configsListView_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);

                foreach (var file in files.Where(f => f.EndsWith(".opk")))
                {
                    try
                    {
                        await vm.ImportConfigFromFileAsync(file);
                    }
                    catch (Exception ex)
                    {
                        Alert.Exception(ex);
                    }
                }
            }
        }

        private void SelectAll(object sender, RoutedEventArgs e) => configsListView.SelectAll();

        private void DeselectAll(object sender, RoutedEventArgs e) => configsListView.UnselectAll();

        // Helper to find ancestor of a certain type
        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            do
            {
                if (current is T ancestor) return ancestor;
                current = VisualTreeHelper.GetParent(current);
            } while (current != null);
            return null;
        }
    }
}
