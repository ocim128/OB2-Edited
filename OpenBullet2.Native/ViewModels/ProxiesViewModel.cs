using Microsoft.EntityFrameworkCore;
using OpenBullet2.Core.Entities;
using OpenBullet2.Core.Helpers;
using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.DTOs;
using RuriLib.Models.Jobs;
using RuriLib.Models.Proxies;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using OpenBullet2.Native.ViewModels.Base;


namespace OpenBullet2.Native.ViewModels;

    public class ProxiesViewModel : ViewModelBase
{
    private ObservableCollection<ProxyGroupEntity> proxyGroupsCollection;
    private ObservableCollection<ProxyEntity> proxiesCollection;
    private readonly IProxyGroupRepository proxyGroupRepo;
    private readonly IProxyRepository proxyRepo;
    private readonly JobManagerService jobManager;
    private bool initialized;
    private readonly ProxyGroupEntity allGroup = new() { Id = -1, Name = "All" };

    public ObservableCollection<ProxyEntity> ProxiesCollection
    {
        get => proxiesCollection;
        private set
        {
            proxiesCollection = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<ProxyGroupEntity> ProxyGroupsCollection
    {
        get => proxyGroupsCollection;
        private set
        {
            proxyGroupsCollection = value;
            OnPropertyChanged();
        }
    }

    public int SelectedGroupId
    {
        get => SelectedGroup.Id;
        set
        {
            SelectedGroup = proxyGroupsCollection.First(g => g.Id == value);
            OnPropertyChanged();
            _ = RefreshListAsync();
        }
    }

    public int Total => ((CollectionView)CollectionViewSource.GetDefaultView(ProxiesCollection)).Count;
    public int Working => ((CollectionView)CollectionViewSource.GetDefaultView(ProxiesCollection)).Cast<ProxyEntity>().Count(static p => p.Status == ProxyWorkingStatus.Working);
    public int NotWorking => ((CollectionView)CollectionViewSource.GetDefaultView(ProxiesCollection)).Cast<ProxyEntity>().Count(static p => p.Status == ProxyWorkingStatus.NotWorking);
    public bool GroupIsValid => SelectedGroup != allGroup;
    public ProxyGroupEntity SelectedGroup { get; private set; }

    private string searchString = string.Empty;
    public string SearchString
    {
        get => searchString;
        set
        {
            searchString = value;
            OnPropertyChanged();
            CollectionViewSource.GetDefaultView(ProxiesCollection).Refresh();
            OnPropertyChanged(nameof(Total));
        }
    }

    public IEnumerable<string> ProxyTypes
    {
        get
        {
            return new string[] { "All" }.Concat(
                ProxiesCollection.Select(p => p.Type.ToString()).Distinct().OrderBy(t => t));
        }
    }

    private string typeFilter = "All";
    public string TypeFilter
    {
        get => typeFilter;
        set
        {
            typeFilter = value;
            OnPropertyChanged();
            CollectionViewSource.GetDefaultView(ProxiesCollection).Refresh();
            OnPropertyChanged(nameof(Total));
        }
    }

    public IEnumerable<string> Countries
    {
        get
        {
            return new string[] { "All" }.Concat(
                ProxiesCollection.Select(p => p.Country).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c));
        }
    }

    private string countryFilter = "All";
    public string CountryFilter
    {
        get => countryFilter;
        set
        {
            countryFilter = value;
            OnPropertyChanged();
            CollectionViewSource.GetDefaultView(ProxiesCollection).Refresh();
            OnPropertyChanged(nameof(Total));
        }
    }

    public IEnumerable<string> Statuses
    {
        get
        {
            return new string[] { "All" }.Concat(
                ProxiesCollection.Select(p => p.Status.ToString()).Distinct().OrderBy(s => s));
        }
    }

    private string statusFilter = "All";
    public string StatusFilter
    {
        get => statusFilter;
        set
        {
            statusFilter = value;
            OnPropertyChanged();
            CollectionViewSource.GetDefaultView(ProxiesCollection).Refresh();
            OnPropertyChanged(nameof(Total));
        }
    }

    public ProxiesViewModel(
        IProxyGroupRepository proxyGroupRepository,
        IProxyRepository proxyRepository,
        JobManagerService jobManagerService)
    {
        proxyGroupRepo = proxyGroupRepository ?? throw new ArgumentNullException(nameof(proxyGroupRepository));
        proxyRepo = proxyRepository ?? throw new ArgumentNullException(nameof(proxyRepository));
        jobManager = jobManagerService ?? throw new ArgumentNullException(nameof(jobManagerService));
        ProxyGroupsCollection =
        [
            allGroup
        ];
        ProxiesCollection = [];
        SelectedGroupId = allGroup.Id;
    }

    public async Task InitializeAsync()
    {
        if (!initialized)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("ProxiesViewModel: Starting initialization");
                await RefreshGroupsAsync();
                initialized = true;
                System.Diagnostics.Debug.WriteLine("ProxiesViewModel: Initialization completed successfully");
            }
            catch (Exception ex)
            {
                var errorDetails = $"ProxiesViewModel initialization failed: {ex.GetType().Name} - {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorDetails += $" | Inner: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}";
                }
                
                System.Diagnostics.Debug.WriteLine(errorDetails);
                System.Diagnostics.Debug.WriteLine($"Full ProxiesViewModel error: {ex}");
                
                /* Overkill diagnostic logging removed
                try
                {
                        ex, 
                        "ProxiesViewModel.InitializeAsync", 
                        "Failed to initialize ProxiesViewModel during page navigation", 
                        false);
                }
                catch { }
                */
                
                throw; // Re-throw so the UI can handle it
            }
        }
    }

    public async Task RefreshGroupsAsync()
    {
        SelectedGroupId = allGroup.Id;
        var entities = await proxyGroupRepo.GetAll().ToListAsync();
        ProxyGroupsCollection = new ObservableCollection<ProxyGroupEntity>(new ProxyGroupEntity[] { allGroup }.Concat(entities));

        await RefreshListAsync();
    }

    public void HookFilters()
    {
        var view = (CollectionView)CollectionViewSource.GetDefaultView(ProxiesCollection);
        view.Filter = ProxiesFilter;
    }

    private bool ProxiesFilter(object item)
    {
        var proxy = item as ProxyEntity;
        if (proxy == null) return false;
        
        var searchOk = string.IsNullOrEmpty(searchString) || 
                       proxy.Host?.Contains(searchString, StringComparison.OrdinalIgnoreCase) == true ||
                       proxy.Username?.Contains(searchString, StringComparison.OrdinalIgnoreCase) == true ||
                       proxy.Country?.Contains(searchString, StringComparison.OrdinalIgnoreCase) == true;
        
        var typeOk = typeFilter == "All" || proxy.Type.ToString() == typeFilter;
        var countryOk = countryFilter == "All" || proxy.Country == countryFilter;
        var statusOk = statusFilter == "All" || proxy.Status.ToString() == statusFilter;

        return searchOk && typeOk && countryOk && statusOk;
    }

    public async Task RefreshListAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("ProxiesViewModel: Starting RefreshListAsync");
            var items = SelectedGroup == allGroup
                ? await proxyRepo.GetAll().ToListAsync()
                : await proxyRepo.GetAll().Include(p => p.Group).Where(p => p.Group.Id == SelectedGroup.Id).ToListAsync();

            System.Diagnostics.Debug.WriteLine($"ProxiesViewModel: Loaded {items.Count} proxies from repository");
            
            ProxiesCollection = new ObservableCollection<ProxyEntity>(items);
            OnPropertyChanged(nameof(Total));
            OnPropertyChanged(nameof(Working));
            OnPropertyChanged(nameof(NotWorking));
            OnPropertyChanged(nameof(ProxyTypes));
            OnPropertyChanged(nameof(Countries));
            OnPropertyChanged(nameof(Statuses));
            HookFilters();
            System.Diagnostics.Debug.WriteLine("ProxiesViewModel: RefreshListAsync completed successfully");
        }
        catch (Exception ex)
        {
            var errorDetails = $"ProxiesViewModel RefreshListAsync failed: {ex.GetType().Name} - {ex.Message}";
            if (ex.InnerException != null)
            {
                errorDetails += $" | Inner: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}";
            }
            
            System.Diagnostics.Debug.WriteLine(errorDetails);
            System.Diagnostics.Debug.WriteLine($"Full RefreshListAsync error: {ex}");
            
            /* Overkill diagnostic logging removed
            try
            {
                    ex, 
                    "ProxiesViewModel.RefreshListAsync", 
                    $"Failed to refresh proxies list from database. SelectedGroup: {SelectedGroup?.Name ?? "null"}", 
                    false);
            }
            catch { }
            */
            
            // Initialize with empty collection to prevent further UI errors
            ProxiesCollection = new ObservableCollection<ProxyEntity>();
            OnPropertyChanged(nameof(Total));
            OnPropertyChanged(nameof(Working));
            OnPropertyChanged(nameof(NotWorking));
            OnPropertyChanged(nameof(ProxyTypes));
            OnPropertyChanged(nameof(Countries));
            OnPropertyChanged(nameof(Statuses));
            
            throw; // Re-throw so calling code can handle it
        }
    }

    public Task AddGroupAsync(ProxyGroupEntity group)
    {
        ProxyGroupsCollection.Add(group);
        SelectedGroupId = allGroup.Id;

        return proxyGroupRepo.AddAsync(group);
    }

    public async Task EditGroupAsync(ProxyGroupEntity group)
    {
        await proxyGroupRepo.UpdateAsync(group);
        await RefreshGroupsAsync();
    }

    public async Task DeleteSelectedGroupAsync()
    {
        if (SelectedGroup == allGroup)
        {
            throw new Exception("Select a group first");
        }

        var firstProxies = jobManager.Jobs.OfType<ProxyCheckJob>()
            .Select(j => j.Proxies.FirstOrDefault()).Where(p => p != null);

        // Run through all the list of proxies
        foreach (var f in firstProxies)
        {
            // If we find that a proxy which is in use by a job belongs to the group to delete
            if (f != null && ProxiesCollection.Any(p => p.Id == f.Id))
            {
                // Prompt error and return
                throw new Exception("Group in use by a proxy check job");
            }
        }

        // This will cascade delete all the proxies in the group
        await proxyGroupRepo.DeleteAsync(SelectedGroup);

        SelectedGroupId = allGroup.Id;

        await RefreshGroupsAsync();
    }

    public async Task AddProxiesAsync(ProxiesForImportDto dto)
    {
        if (SelectedGroup == allGroup)
        {
            throw new Exception("Select a group first");
        }

        var proxies = new List<Proxy>();

        foreach (var line in dto.Lines.Where(l => !string.IsNullOrEmpty(l)).Distinct())
        {
            try
            {
                proxies.Add(Proxy.Parse(line, dto.DefaultType, dto.DefaultUsername, dto.DefaultPassword));
            }
            catch
            {
            }
        }

        var entities = proxies.ConvertAll(Mapper.MapProxyToProxyEntity);
        var currentGroup = await proxyGroupRepo.GetAsync(SelectedGroup.Id);
        proxyRepo.Attach(currentGroup);
        entities.ForEach(e => e.Group = currentGroup);

        await proxyRepo.AddAsync(entities);
        _ = await proxyRepo.RemoveDuplicatesAsync(currentGroup.Id);
        await RefreshListAsync();
    }

    public async Task DeleteAsync(IEnumerable<ProxyEntity> proxies)
    {
        await proxyRepo.DeleteAsync(proxies);
        await RefreshListAsync();
    }

    public async Task DeleteNotWorkingAsync()
    {
        var toRemove = proxiesCollection.Where(static p => p.Status == ProxyWorkingStatus.NotWorking);
        await proxyRepo.DeleteAsync(toRemove);
        await RefreshListAsync();
    }

    public async Task DeleteUntestedAsync()
    {
        var toRemove = proxiesCollection.Where(static p => p.Status == ProxyWorkingStatus.Untested);
        await proxyRepo.DeleteAsync(toRemove);
        await RefreshListAsync();
    }

    public override void UpdateViewModel()
    {
        _ = RefreshListAsync();
        base.UpdateViewModel();
    }
}
