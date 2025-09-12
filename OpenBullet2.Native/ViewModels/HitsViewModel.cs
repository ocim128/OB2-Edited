using Microsoft.EntityFrameworkCore;
using OpenBullet2.Core.Entities;
using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using OpenBullet2.Native.Infrastructure.DependencyInjection;

namespace OpenBullet2.Native.ViewModels
{
    public class HitsViewModel : OpenBullet2.Native.ViewModels.Infrastructure.ViewModelBase
    {
        private readonly OpenBulletSettingsService obSettingsService;
        private readonly IHitRepository hitRepo;
        private bool initialized;

        private ObservableCollection<HitEntity> hitsCollection;
        public ObservableCollection<HitEntity> HitsCollection
        {
            get => hitsCollection;
            private set
            {
                hitsCollection = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Total));
            }
        }

        public int Total => ((CollectionView)CollectionViewSource.GetDefaultView(HitsCollection)).Count;

        private string searchString = string.Empty;
        public string SearchString
        {
            get => searchString;
            set
            {
                searchString = value;
                OnPropertyChanged();
                CollectionViewSource.GetDefaultView(HitsCollection).Refresh();
                OnPropertyChanged(nameof(Total));
            }
        }

        public IEnumerable<string> ConfigNames
        {
            get
            {
                return new string[] { "All" }.Concat(
                    HitsCollection.GroupBy(h => h.ConfigName).Select(g => g.First().ConfigName));
            }
        }

        private string configFilter = "All";
        public string ConfigFilter
        {
            get => configFilter;
            set
            {
                configFilter = value;
                OnPropertyChanged();
                CollectionViewSource.GetDefaultView(HitsCollection).Refresh();
                OnPropertyChanged(nameof(Total));
                OnPropertyChanged(nameof(ConfigNames));
            }
        }

        public IEnumerable<string> HitTypes
        {
            get
            {
                return new string[] { "All" }.Concat(
                    HitsCollection.GroupBy(h => h.Type).Select(g => g.First().Type));
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
                CollectionViewSource.GetDefaultView(HitsCollection).Refresh();
                OnPropertyChanged(nameof(Total));
                OnPropertyChanged(nameof(HitTypes));
            }
        }

        public HitsViewModel()
        {
            obSettingsService = ServiceLocator.GetService<OpenBulletSettingsService>();
            hitRepo = ServiceLocator.GetService<IHitRepository>();
            HitsCollection = new ObservableCollection<HitEntity>();
        }

        public async Task InitializeAsync()
        {
            if (!initialized)
            {
                await RefreshListAsync();
                initialized = true;
            }
        }

        public void HookFilters()
        {
            var view = (CollectionView)CollectionViewSource.GetDefaultView(HitsCollection);
            view.Filter = HitsFilter;
        }

        private bool HitsFilter(object item)
        {
            var hit = item as HitEntity;
            var searchOk = string.IsNullOrEmpty(searchString) || 
                          hit.Data.Contains(searchString, StringComparison.OrdinalIgnoreCase) ||
                          hit.CapturedData.Contains(searchString, StringComparison.OrdinalIgnoreCase);
            var configOk = configFilter == "All" || hit.ConfigName == configFilter;
            var typeOk = typeFilter == "All" || hit.Type == typeFilter;

            return searchOk && configOk && typeOk;
        }

        public async Task RefreshListAsync()
        {
            try
            {
                // TODO: Make this not fail when hits are being written and we try to read them!
                // A.k.a. make this use another repo, not the singleton, and refresh it when new hits come in
                var items = await hitRepo.GetAll().ToListAsync();
                HitsCollection = new ObservableCollection<HitEntity>(items);
                OnPropertyChanged(nameof(Total));
                OnPropertyChanged(nameof(ConfigNames));
                OnPropertyChanged(nameof(HitTypes));
                HookFilters();
            }
            catch
            {

            }
        }

        public Task Update(HitEntity hit) => hitRepo.UpdateAsync(hit);

        public async Task DeleteAsync(IEnumerable<HitEntity> hits)
        {
            await hitRepo.DeleteAsync(hits);
            await RefreshListAsync();
            OnPropertyChanged(nameof(Total));
        }

        public async Task PurgeAsync()
        {
            HitsCollection.Clear();
            await hitRepo.PurgeAsync();
            OnPropertyChanged(nameof(Total));
        }

        public async Task<int> DeleteDuplicatesAsync()
        {
            var duplicates = HitsCollection
                .GroupBy(h => h.GetHashCode(obSettingsService.Settings.GeneralSettings.IgnoreWordlistNameOnHitsDedupe))
                .Where(g => g.Count() > 1)
                .SelectMany(g => g.OrderBy(h => h.Date)
                .Reverse().Skip(1)).ToList();

            await hitRepo.DeleteAsync(duplicates);
            await RefreshListAsync();

            return duplicates.Count;
        }

        public override void UpdateViewModel()
        {
            _ = RefreshListAsync();
            base.UpdateViewModel();
        }
    }
}
