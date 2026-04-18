using Flux.Core.Entities;
using Flux.Native.Helpers;
using Flux.Native.ViewModels.Data;
using Flux.Native.Views.Dialogs.Wordlist;
using RuriLib.Models.Environment;
using RuriLib.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Threading.Tasks;
using Microsoft.Win32;


namespace Flux.Native.Views.Pages.Data
{
    /// <summary>
    /// Interaction logic for Wordlists.xaml
    /// </summary>
    public partial class Wordlists : Page
    {
        private readonly WordlistsViewModel vm;
        private readonly EnvironmentSettings env;
        private GridViewColumnHeader listViewSortCol;
        private SortAdorner listViewSortAdorner;

        private IEnumerable<WordlistEntity> GetSelectedWordlists() => wordlistListView.SelectedItems.Cast<WordlistEntity>().ToList();

        public Wordlists(WordlistsViewModel vm, RuriLibSettingsService rlSettingsService)
        {
            this.vm = vm;
            DataContext = this.vm;
            _ = this.vm.InitializeAsync().ContinueWith(t => { if (t.Exception != null) System.Diagnostics.Debug.WriteLine($"InitializeAsync failed: {t.Exception.InnerException?.Message}"); }, TaskContinuationOptions.OnlyOnFaulted);

            InitializeComponent();
            env = rlSettingsService.Environment;
        }

        public async Task Refresh()
        {
            await vm.RefreshListAsync();
        }

        private void Add(object sender, RoutedEventArgs e)
            => new MainDialog(new AddWordlistDialog(this), "Add a wordlist").ShowDialog();

        private async void DeleteSelected(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!GetSelectedWordlists().Any())
                {
                    Alert.Error("No wordlist selected", "Please select at least one wordlist to delete.");
                    return;
                }

                if (Alert.Choice("Are you sure?", $"Do you really want to delete {GetSelectedWordlists().Count()} selected wordlist(s)? This cannot be undone."))
                {
                    foreach (var wordlist in GetSelectedWordlists())
                    {
                        await vm.DeleteAsync(wordlist);
                    }

                    Alert.Success("Done", "Successfully deleted the selected wordlist references from the DB");
                }
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void DeleteAll(object sender, RoutedEventArgs e)
        {
            if (Alert.Choice("Are you sure?", "Do you really want to delete ALL wordlists? This cannot be undone."))
            {
                vm.DeleteAll();
                Alert.Success("Done", "Successfully deleted all wordlist references from the DB");
            }
        }

        private async void ExportSelected(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!GetSelectedWordlists().Any())
                {
                    Alert.Error("No wordlist selected", "Please select at least one wordlist to export.");
                    return;
                }

                var sfd = new SaveFileDialog
                {
                    Filter = "Wordlist |*.txt",
                    Title = "Export wordlists"
                };
                sfd.ShowDialog();

                if (!string.IsNullOrWhiteSpace(sfd.FileName))
                {
                    foreach (var wordlist in GetSelectedWordlists())
                    {
                        var sourceFile = wordlist.FileName;
                        var destinationFile = Path.Combine(Path.GetDirectoryName(sfd.FileName), Path.GetFileName(sourceFile));
                        File.Copy(sourceFile, destinationFile, true);
                    }
                    Alert.Success("Success", "Successfully exported the selected wordlists");
                }
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async void DeleteNotFound(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Alert.Choice("Are you sure?", "Do you really want to delete all wordlists that could not be found on disk? This cannot be undone."))
                {
                    var deleted = await vm.DeleteNotFoundAsync();
                    Alert.Success("Done", $"Successfully deleted {deleted} unresolved wordlist references from the DB");
                }
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        
        public async Task AddWordlist(WordlistEntity wordlist)
        {
            try
            {
                await vm.AddAsync(wordlist);
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void ColumnHeaderClicked(object sender, RoutedEventArgs e)
        {
            var column = sender as GridViewColumnHeader;
            if (column?.Tag is not string sortBy) return;

            if (listViewSortCol != null)
            {
                var oldLayer = AdornerLayer.GetAdornerLayer(listViewSortCol);
                if (oldLayer != null) oldLayer.Remove(listViewSortAdorner);
                wordlistListView.Items.SortDescriptions.Clear();
            }

            var newDir = ListSortDirection.Ascending;

            if (listViewSortCol == column && listViewSortAdorner.Direction == newDir)
            {
                newDir = ListSortDirection.Descending;
            }

            listViewSortCol = column;
            listViewSortAdorner = new SortAdorner(listViewSortCol, newDir);
            var layer = AdornerLayer.GetAdornerLayer(listViewSortCol);
            if (layer != null) layer.Add(listViewSortAdorner);
            wordlistListView.Items.SortDescriptions.Add(new SortDescription(sortBy, newDir));
        }

        private async void HandleDrop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);

                    foreach (var file in files.Where(f => f.EndsWith(".txt")))
                    {
                        await ProcessDroppedFile(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async Task ProcessDroppedFile(string file)
        {
            try
            {
                var path = file;
                var cwd = Directory.GetCurrentDirectory();

                // Make the path relative if inside the CWD
                if (path.StartsWith(cwd))
                {
                    path = path[(cwd.Length + 1)..];
                }

                var firstLine = File.ReadLines(path).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? string.Empty;

                await vm.AddAsync(new WordlistEntity
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    FileName = path,
                    Type = env.RecognizeWordlistType(firstLine),
                    Purpose = string.Empty,
                    Total = File.ReadLines(path).Count()
                });
            }
            catch // Intentionally empty to gracefully handle file access errors during drag and drop
            {

            }
        }

        private void SelectAll(object sender, RoutedEventArgs e) => wordlistListView.SelectAll();

        private void DeselectAll(object sender, RoutedEventArgs e) => wordlistListView.UnselectAll();
    }
}


