using Microsoft.Win32;
using OpenBullet2.Core.Entities;
using OpenBullet2.Native.DTOs;
using OpenBullet2.Native.Extensions;
using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.Services;
using OpenBullet2.Native.ViewModels;
using OpenBullet2.Native.ViewModels.Data;
using OpenBullet2.Native.Views.Dialogs.Proxy;
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
using Microsoft.Extensions.DependencyInjection;


namespace OpenBullet2.Native.Views.Pages.Data;

/// <summary>
/// Interaction logic for Proxies.xaml
/// </summary>
public partial class Proxies : Page
{
    private const string ConfirmationTitle = "Are you sure?";
    private readonly ProxiesViewModel vm;
    private GridViewColumnHeader listViewSortCol;
    private SortAdorner listViewSortAdorner;

    private IEnumerable<ProxyEntity> GetSelectedProxies() => proxiesListView.SelectedItems.Cast<ProxyEntity>().ToList();

    public Proxies()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("Proxies: Starting page construction");
            vm = App.ServiceProvider.GetRequiredService<ViewModelsService>().Proxies;
            DataContext = vm;
            
            System.Diagnostics.Debug.WriteLine("Proxies: Initializing ViewModel");
            _ = vm.InitializeAsync();

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
        var sortBy = column.Tag.ToString();

        if (listViewSortCol != null)
        {
            AdornerLayer.GetAdornerLayer(listViewSortCol).Remove(listViewSortAdorner);
            proxiesListView.Items.SortDescriptions.Clear();
        }

        var newDir = ListSortDirection.Ascending;

        if (listViewSortCol == column && listViewSortAdorner.Direction == newDir)
        {
            newDir = ListSortDirection.Descending;
        }

        listViewSortCol = column;
        listViewSortAdorner = new SortAdorner(listViewSortCol, newDir);
        AdornerLayer.GetAdornerLayer(listViewSortCol).Add(listViewSortAdorner);
        proxiesListView.Items.SortDescriptions.Add(new SortDescription(sortBy, newDir));
    }

    private void SelectAll(object sender, RoutedEventArgs e) => proxiesListView.SelectAll();

    private void DeselectAll(object sender, RoutedEventArgs e) => proxiesListView.UnselectAll();

    private void ProxyListViewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);

            foreach (var file in files.Where(static f => f.EndsWith(".txt")))
            {
                var lines = File.ReadAllLines(file);
                var dto = new ProxiesForImportDto { Lines = lines };

                if (file.Contains("socks4a", StringComparison.OrdinalIgnoreCase))
                {
                    dto.DefaultType = ProxyType.Socks4a;
                }
                else if (file.Contains("socks4", StringComparison.OrdinalIgnoreCase))
                {
                    dto.DefaultType = ProxyType.Socks4;
                }
                else
                {
                    dto.DefaultType = file.Contains("socks5", StringComparison.OrdinalIgnoreCase) ? ProxyType.Socks5 : ProxyType.Http;
                }
                // Call AddProxies to add the proxies from the dropped file
                // await AddProxies(dto); // Uncomment and make AddProxies async Task if needed
            }
        }
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


