using Microsoft.Win32;
using Newtonsoft.Json;
using Flux.Core.Entities;
using Flux.Core.Models.Data;
using Flux.Core.Models.Jobs;
using Flux.Core.Services;
using Flux.Native.Extensions;
using Flux.Native.Helpers;

using Flux.Native.Services;
using Flux.Native.ViewModels;
using Flux.Native.ViewModels.Data;
using RuriLib.Extensions;
using RuriLib.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Flux.Native.Views.Pages.Data
{
    /// <summary>
    /// Interaction logic for Hits.xaml
    /// </summary>
    public partial class Hits : Page
    {
        private readonly HitsViewModel vm;
        private readonly ConfigService configService;
        private readonly MainWindow window;
        private readonly RuriLibSettingsService rlSettingsService;
        private readonly FluxSettingsService fluxSettingsService;
        private GridViewColumnHeader listViewSortCol;
        private SortAdorner listViewSortAdorner;

        private IEnumerable<HitEntity> SelectedHits => hitsListView.SelectedItems.Cast<HitEntity>().ToList(); // This cannot be static since it accesses a UI element (hitsListView)

        private readonly Func<HitEntity, string> captureMapping = new(hit => $"{hit.Data} | {hit.CapturedData}");
        private readonly Func<HitEntity, string> fullMapping = new(hit =>
            "Data = " + hit.Data +
            " | Type = " + hit.Type +
            " | Config = " + hit.ConfigName +
            " | Wordlist = " + hit.WordlistName +
            " | Proxy = " + hit.Proxy +
            " | Date = " + hit.Date.ToLongDateString() +
            " | CapturedData = " + hit.CapturedData);

        public Hits()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Hits: Starting page construction");
                vm = App.ServiceProvider.GetRequiredService<ViewModelsService>().Hits;
                DataContext = vm;
                
                System.Diagnostics.Debug.WriteLine("Hits: Initializing ViewModel");
                _ = vm.InitializeAsync();

                InitializeComponent();
                window = App.ServiceProvider.GetRequiredService<MainWindow>();
                configService = App.ServiceProvider.GetRequiredService<ConfigService>();
                rlSettingsService = App.ServiceProvider.GetRequiredService<RuriLibSettingsService>();
                fluxSettingsService = App.ServiceProvider.GetRequiredService<FluxSettingsService>();
                var env = App.ServiceProvider.GetRequiredService<RuriLibSettingsService>().Environment;

                // HACK: Hardcoded stuff
                var menu = (ContextMenu)Resources["ItemContextMenu"];
                var copyMenu = (MenuItem)menu.Items[0];
                var saveMenu = (MenuItem)menu.Items[1];

                foreach (var format in env.ExportFormats.Select(f => f.Format))
                {
                    AddCopyMenuItem(format, copyMenu);
                    AddSaveMenuItem(format, saveMenu);
                }
                System.Diagnostics.Debug.WriteLine("Hits: Page construction completed successfully");
            }
            catch (Exception ex)
            {
                var errorDetails = $"Hits page constructor failed: {ex.GetType().Name} - {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorDetails += $" | Inner: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}";
                }
                
                System.Diagnostics.Debug.WriteLine(errorDetails);
                System.Diagnostics.Debug.WriteLine($"Full Hits constructor error: {ex}");
                
                /* Overkill diagnostic logging removed
                try
                {
                        ex, 
                        "Hits.Constructor", 
                        "Failed to construct Hits page during navigation", 
                        false);
                }
                catch { }
                */
                
                throw; // Re-throw so navigation can handle it
            }
        }

        private void AddCopyMenuItem(string format, MenuItem copyMenu)
        {
            var copyItem = new MenuItem();
            copyItem.Header = format;
            copyItem.Click += new RoutedEventHandler(CopySelectedCustom);
            ((MenuItem)copyMenu.Items[4]).Items.Add(copyItem);
        }

        private void AddSaveMenuItem(string format, MenuItem saveMenu)
        {
            var saveItem = new MenuItem();
            saveItem.Header = format;
            saveItem.Click += new RoutedEventHandler(SaveSelectedCustom);
            ((MenuItem)saveMenu.Items[3]).Items.Add(saveItem);
        }

        public void UpdateViewModel() => vm.UpdateViewModel();

        public async Task Refresh()
        {
            await vm.RefreshListAsync();
        }

        private async void DeleteSelected(object sender, RoutedEventArgs e)
        {
            try
            {
                await vm.DeleteAsync(SelectedHits);
                Alert.Success("Done", "Successfully deleted the selected hits from the DB");
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async void Purge(object sender, RoutedEventArgs e)
        {
            if (Alert.Confirm("Are you REALLY sure?", "This will delete ALL your hits, not just the ones you filtered. Are you sure you want to do this?", nameof(fluxSettingsService.Settings.GeneralSettings.PerformConfirmationOnDestructiveActions)))
            {
                try
                {
                    await vm.PurgeAsync();
                    Alert.Success("Done", "Successfully deleted all hits from the DB");
                }
                catch (Exception ex)
                {
                    Alert.Exception(ex);
                }
            }
        }

        private void UpdateSearch(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                // Filter functionality removed - method kept for compatibility
            }
        }

        private void Search(object sender, RoutedEventArgs e)
        {
            // Filter functionality removed - method kept for compatibility
        }

        private async void DeleteDuplicates(object sender, RoutedEventArgs e)
        {
            try
            {
                var deleted = await vm.DeleteDuplicatesAsync();
                Alert.Success("Done", $"Successfully deleted {deleted} duplicate hits");
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void ColumnHeaderClicked(object sender, RoutedEventArgs e)
        {
            var column = sender as GridViewColumnHeader;
            var sortBy = column.Tag.ToString();

            if (listViewSortCol != null)
            {
                AdornerLayer.GetAdornerLayer(listViewSortCol).Remove(listViewSortAdorner);
                hitsListView.Items.SortDescriptions.Clear();
            }

            var newDir = ListSortDirection.Ascending;

            if (listViewSortCol == column && listViewSortAdorner.Direction == newDir)
            {
                newDir = ListSortDirection.Descending;
            }

            listViewSortCol = column;
            listViewSortAdorner = new SortAdorner(listViewSortCol, newDir);
            AdornerLayer.GetAdornerLayer(listViewSortCol).Add(listViewSortAdorner);
            hitsListView.Items.SortDescriptions.Add(new SortDescription(sortBy, newDir));
        }

        private void SelectAll(object sender, RoutedEventArgs e) => hitsListView.SelectAll();

        private async void SendToRecheck(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!SelectedHits.Any())
                {
                    return;
                }

                var firstHit = SelectedHits.ToList()[0];

                var jobOptions = (MultiRunJobOptions)JobOptionsFactory.CreateNew(JobType.MultiRun);
                var wordlistType = rlSettingsService.Environment.RecognizeWordlistType(firstHit.Data);

                // Get the config
                var config = configService.GetConfigsList().FirstOrDefault(c => c.Metadata.Name == firstHit.ConfigName);

                // If we cannot find a config with that id anymore, don't set it
                if (config == null)
                {
                    Alert.Warning("Config not found", $"Could not find the config these hits refer to ({firstHit.ConfigName})");
                }
                else
                {
                    jobOptions.ConfigId = config.Id;
                    jobOptions.Bots = config.Settings.GeneralSettings.SuggestedBots;
                    wordlistType = config.Settings.DataSettings.AllowedWordlistTypes.First();
                }

                // Write the temporary file
                var tempFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                await File.WriteAllLinesAsync(tempFile, SelectedHits.Select(h => h.Data)).ConfigureAwait(false);
                var dataPoolOptions = new FileDataPoolOptions
                {
                    FileName = tempFile,
                    WordlistType = wordlistType
                };
                jobOptions.DataPool = dataPoolOptions;

                // Create the job entity and add it to the database
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    var jobs = App.ServiceProvider.GetRequiredService<ViewModelsService>().Jobs;
                    var jobVM = await jobs.CreateJobAsync(jobOptions);
                    window.DisplayJob(jobVM);
                });
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async void CopySelected(object sender, RoutedEventArgs e)
        {
            try
            {
                await SelectedHits.CopyToClipboardAsync(h => h.Data);
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async void CopySelectedProxies(object sender, RoutedEventArgs e)
        {
            try
            {
                await SelectedHits.CopyToClipboardAsync(h => h.Proxy);
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async void CopySelectedWithCapture(object sender, RoutedEventArgs e)
        {
            try
            {
                await SelectedHits.CopyToClipboardAsync(captureMapping);
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async void CopySelectedFull(object sender, RoutedEventArgs e)
        {
            try
            {
                await SelectedHits.CopyToClipboardAsync(fullMapping);
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async void CopySelectedCustom(object sender, RoutedEventArgs e)
        {
            try
            {
                var format = (sender as MenuItem).Header.ToString().Unescape();
                await SelectedHits.CopyToClipboardAsync(h => ApplyCustomFormat(h, format));
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private static string GetSaveFile()
        {
            var sfd = new SaveFileDialog();
            sfd.Filter = "TXT files | *.txt";
            sfd.FilterIndex = 1;
            sfd.ShowDialog();
            return sfd.FileName;
        }

        private void SaveSelected(object sender, RoutedEventArgs e)
            => TrySave(h => h.Data);

        private void SaveSelectedWithCapture(object sender, RoutedEventArgs e)
            => TrySave(captureMapping);

        private void SaveSelectedFull(object sender, RoutedEventArgs e)
            => TrySave(fullMapping);

        private void SaveSelectedCustom(object sender, RoutedEventArgs e)
        {
            var format = (sender as MenuItem).Header.ToString().Unescape();
            TrySave(h => ApplyCustomFormat(h, format));
        }

        private void TrySave(Func<HitEntity, string> mapping)
        {
            try
            {
                SelectedHits.SaveToFile(GetSaveFile(), mapping);
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private static string ApplyCustomFormat(HitEntity hit, string format)
            => new StringBuilder(format)
                .Replace("<DATA>", hit.Data)
                .Replace("<PROXY>", hit.Proxy)
                .Replace("<DATE>", hit.Date.ToLongDateString() + " " + hit.Date.ToLongTimeString())
                .Replace("<CONFIG>", hit.ConfigName)
                .Replace("<WORDLIST>", hit.WordlistName)
                .Replace("<TYPE>", hit.Type)
                .Replace("<CAPTURE>", hit.CapturedData)
                .ToString();

        private void LVIMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // This event handler is intentionally left empty. It's a placeholder in case
            // custom logic for right-click on list view items is needed in the future.
        }
    }
}


