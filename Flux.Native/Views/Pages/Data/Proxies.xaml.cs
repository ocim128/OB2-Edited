using Microsoft.Win32;
using Flux.Core.Entities;
using Flux.Native.DTOs;
using Flux.Native.Extensions;
using Flux.Native.Helpers;
using Flux.Native.ViewModels.Data;
using Flux.Native.Views.Dialogs.Proxy;
using RuriLib.Models.Proxies;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Threading.Tasks;

using Flux.Native.Services;

namespace Flux.Native.Views.Pages.Data;

/// <summary>
/// Interaction logic for Proxies.xaml
/// </summary>
public partial class Proxies : Page, IUpdatablePage
{
    private const string ConfirmationTitle = "Are you sure?";
    private readonly ProxiesViewModel vm;
    private GridViewColumnHeader listViewSortCol;
    private SortAdorner listViewSortAdorner;

    private IEnumerable<ProxyEntity> GetSelectedProxies() => proxiesListView.SelectedItems.Cast<ProxyEntity>().ToList();

    public Proxies(ProxiesViewModel vm)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("Proxies: Starting page construction");
            this.vm = vm;
            DataContext = this.vm;
            
            System.Diagnostics.Debug.WriteLine("Proxies: Initializing ViewModel");
            _ = this.vm.InitializeAsync().ContinueWith(t => { if (t.Exception != null) System.Diagnostics.Debug.WriteLine($"InitializeAsync failed: {t.Exception.InnerException?.Message}"); }, TaskContinuationOptions.OnlyOnFaulted);

            InitializeComponent();
            System.Diagnostics.Debug.WriteLine("Proxies: Page construction completed successfully");
        }
        catch (Exception ex)
        {
            var errorDetails = $"Proxies page constructor failed: {ex.GetType().Name} - {ex.Message}";
            if (ex.InnerException != null)
            {
                errorDetails += $" | Inner: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}";
            }
            
            System.Diagnostics.Debug.WriteLine(errorDetails);
            System.Diagnostics.Debug.WriteLine($"Full Proxies constructor error: {ex}");
            
            /* Overkill diagnostic logging removed
            try
            {
                    ex, 
                    "Proxies.Constructor", 
                    "Failed to construct Proxies page during navigation", 
                    false);
            }
            catch { }
            */
            
            throw; // Re-throw so navigation can handle it
        }
    }

    private void AddGroup(object sender, RoutedEventArgs e)
        => new MainDialog(new AddProxyGroupDialog(this), "Add proxy group").ShowDialog();

    private void EditGroup(object sender, RoutedEventArgs e)
    {
        if (!vm.GroupIsValid)
        {
            ShowInvalidGroupError();
            return;
        }

        _ = new MainDialog(new AddProxyGroupDialog(this, vm.SelectedGroup), "Edit proxy group").ShowDialog();
    }

    private async void DeleteGroup(object sender, RoutedEventArgs e)
    {
        if (!vm.GroupIsValid)
        {
            ShowInvalidGroupError();
            return;
        }

        if (Alert.Choice(ConfirmationTitle, "Do you really want to delete the selected proxy group? This cannot be undone."))
        {
            try
            {
                await vm.DeleteSelectedGroupAsync();
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }
    }

    private async void DeleteNotWorking(object sender, RoutedEventArgs e)
    {
        if (!vm.GroupIsValid)
        {
            ShowInvalidGroupError();
            return;
        }

        if (Alert.Choice(ConfirmationTitle, "Do you really want to delete all not working proxies from the current group? This cannot be undone."))
        {
            try
            {
                await vm.DeleteNotWorkingAsync();
                Alert.Success("Done", "Successfully deleted the not working proxies from the group");
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }
    }

    private async void DeleteUntested(object sender, RoutedEventArgs e)
    {
        if (!vm.GroupIsValid)
        {
            ShowInvalidGroupError();
            return;
        }

        if (Alert.Choice(ConfirmationTitle, "Do you really want to delete all untested proxies from the current group? This cannot be undone."))
        {
            try
            {
                await vm.DeleteUntestedAsync();
                Alert.Success("Done", "Successfully deleted the untested proxies from the group");
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }
    }

    private void Import(object sender, RoutedEventArgs e)
    {
        if (!vm.GroupIsValid)
        {
            ShowInvalidGroupError();
            return;
        }

        _ = new MainDialog(new ImportProxiesDialog(this), "Import proxies").ShowDialog();
    }

    public async void AddGroup(ProxyGroupEntity entity)
    {
        try
        {
            await vm.AddGroupAsync(entity);
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
        }
    }
    public async void EditGroup(ProxyGroupEntity entity)
    {
        try
        {
            await vm.EditGroupAsync(entity);
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
        }
    }

    public void UpdateViewModel() => vm.UpdateViewModel();

    public async Task Refresh() => await vm.RefreshListAsync();

    private void ExportSelected(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Filter = "Text File |*.txt",
            Title = "Export proxies"
        };
        _ = sfd.ShowDialog();

        if (!string.IsNullOrWhiteSpace(sfd.FileName))
        {
            if (GetSelectedProxies().Any())
            {
                GetSelectedProxies().SaveToFile(sfd.FileName, static p => p.ToString());
                Alert.Success("Success", "Successfully exported the selected proxies");
            }
            else
            {
                Alert.Error("Uh-oh", "No proxies selected");
            }
        }
    }

    private async void CopySelectedProxies(object sender, RoutedEventArgs e)
    {
        try
        {
            await GetSelectedProxies().CopyToClipboardAsync(static p => $"{p.Host}:{p.Port}");
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
        }
    }

    private async void CopySelectedProxiesFull(object sender, RoutedEventArgs e)
    {
        try
        {
            await GetSelectedProxies().CopyToClipboardAsync(static p => p.ToString());
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
        }
    }

    public async void AddProxies(ProxiesForImportDto dto)
    {
        try
        {
            await vm.AddProxiesAsync(dto);
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
        }
    }

    private async void DeleteSelected(object sender, RoutedEventArgs e)
    {
        if (!GetSelectedProxies().Any())
        {
            Alert.Error("No proxies selected", "Please select at least one proxy to delete.");
            return;
        }

        if (Alert.Choice(ConfirmationTitle, $"Do you really want to delete {GetSelectedProxies().Count()} selected proxies? This cannot be undone."))
        {
            try
            {
                await vm.DeleteAsync(GetSelectedProxies().ToList());
                Alert.Success("Done", "Successfully deleted the selected proxies from the group");
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
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
            proxiesListView.Items.SortDescriptions.Clear();
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
        proxiesListView.Items.SortDescriptions.Add(new SortDescription(sortBy, newDir));
    }

    private void SelectAll(object sender, RoutedEventArgs e) => proxiesListView.SelectAll();

    private void DeselectAll(object sender, RoutedEventArgs e) => proxiesListView.UnselectAll();

    private void ProxyListViewDrop(object sender, DragEventArgs e)
    {
        // Drag-drop proxy import is not yet implemented.
        // The XAML AllowDrop handler is kept for future use.
    }

    private void ItemRightClick(object sender, MouseButtonEventArgs e)
    {
        // This method is intentionally empty; the UI handles context menus directly.
    }

    private static void ShowInvalidGroupError()
    {
        Alert.Error("Invalid Group", "The provided group name is not valid. It cannot be empty or contain spaces.");
    }
}


