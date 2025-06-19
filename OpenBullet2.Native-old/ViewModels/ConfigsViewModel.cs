using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.DTOs;
using OpenBullet2.Native.Utils;
using RuriLib.Functions.Files;
using RuriLib.Models.Configs;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using RuriLib.Helpers;

namespace OpenBullet2.Native.ViewModels
{
    public class ConfigsViewModel : ViewModelBase
    {
        private readonly ConfigService configService;
        private readonly IConfigRepository configRepo;

        private ObservableCollection<ConfigViewModel> configsCollection;
        public ObservableCollection<ConfigViewModel> ConfigsCollection
        {
            get => configsCollection;
            set
            {
                configsCollection = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Total));
            }
        }

        private string searchString = string.Empty;
        public string SearchString
        {
            get => searchString;
            set
            {
                searchString = value;
                OnPropertyChanged();
                CollectionViewSource.GetDefaultView(ConfigsCollection).Refresh();
                OnPropertyChanged(nameof(Total));
            }
        }

        private ConfigViewModel selectedConfig;
        public ConfigViewModel SelectedConfig
        {
            get => selectedConfig;
            set
            {
                // Deselect the previously selected config
                if (selectedConfig != null)
                {
                    selectedConfig.IsSelected = false;
                }

                selectedConfig = value;
                configService.SelectedConfig = value?.Config;

                // Select the new config
                if (selectedConfig != null)
                {
                    selectedConfig.IsSelected = true;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsConfigSelected));
            }
        }

        public bool IsConfigSelected => SelectedConfig != null;

        private ConfigViewModel hoveredConfig;
        public ConfigViewModel HoveredConfig
        {
            get => hoveredConfig;
            set
            {
                // Unhover the previously hovered config
                if (hoveredConfig != null)
                {
                    hoveredConfig.IsHovered = false;
                }

                hoveredConfig = value;

                // Hover the new config
                if (hoveredConfig != null)
                {
                    hoveredConfig.IsHovered = true;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsConfigHovered));
            }
        }

        public bool IsConfigHovered => HoveredConfig != null;

        public int Total => ConfigsCollection.Count;

        public ConfigsViewModel()
        {
            configService = SP.GetService<ConfigService>();
            configService.OnRemotesLoaded += (s, e) => CreateCollection();
            configRepo = SP.GetService<IConfigRepository>();
            CreateCollection();
        }

        public async Task ImportConfigFromFileAsync(string path)
        {
            var config = await ConfigPacker.UnpackAsync(File.OpenRead(path));
            configService.Configs.Add(config);
            ConfigsCollection.Add(new ConfigViewModel(config));
        }

        public async Task CreateAsync(ConfigForCreationDto dto)
        {
            // Create it in the repo
            var fileName = FileUtils.ReplaceInvalidFileNameChars($"{dto.Name}.opk");
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "UserData/Configs", fileName);

            if (File.Exists(filePath))
            {
                filePath = FileUtils.GetFirstAvailableFileName(filePath);
            }

            var newConfig = await configRepo.CreateAsync(Path.GetFileNameWithoutExtension(filePath));
            newConfig.Metadata.Name = dto.Name;
            newConfig.Metadata.Category = dto.Category;
            newConfig.Metadata.Author = dto.Author;

            var newConfigVM = new ConfigViewModel(newConfig);
            await Save(newConfigVM);

            // Add it to the observable collection
            ConfigsCollection.Insert(0, newConfigVM);

            // Add it to the service
            configService.SelectedConfig = newConfig;
            configService.Configs.Add(newConfig);
        }

        public void Delete(ConfigViewModel vm)
        {
            if (vm == SelectedConfig)
            {
                SelectedConfig = null;
            }

            configRepo.Delete(vm.Config);
            ConfigsCollection.Remove(vm);
        }

        public Task Save(ConfigViewModel vm)
        {
            if (vm.IsRemote)
            {
                throw new Exception("You cannot save remote configs");
            }

            return configRepo.SaveAsync(vm.Config);
        }

        public async Task RescanAsync()
        {
            SelectedConfig = null;
            HoveredConfig = null;
            await configService.ReloadConfigsAsync();
            CreateCollection();
        }

        public void MoveConfig(ConfigViewModel draggedItem, ConfigViewModel targetItem)
        {
            var oldIndex = ConfigsCollection.IndexOf(draggedItem);
            var newIndex = ConfigsCollection.IndexOf(targetItem);

            if (oldIndex < 0 || newIndex < 0) return; // Should not happen

            ConfigsCollection.Move(oldIndex, newIndex);
        }

        public override void UpdateViewModel()
        {
            ConfigsCollection.ToList().ForEach(c => c.UpdateViewModel());
            base.UpdateViewModel();
        }

        public void CreateCollection()
        {
            var viewModels = configService.Configs.Select(c => new ConfigViewModel(c));
            ConfigsCollection = new ObservableCollection<ConfigViewModel>(viewModels);
            Application.Current.Dispatcher.Invoke(() => HookFilters());
        }

        private void HookFilters()
        {
            var view = (CollectionView)CollectionViewSource.GetDefaultView(ConfigsCollection);
            view.Filter = ConfigsFilter;
        }

        private bool ConfigsFilter(object item) => (item as ConfigViewModel).Config
            .Metadata.Name.Contains(searchString, StringComparison.OrdinalIgnoreCase);
    } // Closing ConfigsViewModel class

    public class ConfigViewModel : ViewModelBase
    {
        public Config Config { get; init; }

        private bool isSelected;
        public bool IsSelected
        {
            get => isSelected;
            set
            {
                isSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BackgroundColor));
                OnPropertyChanged(nameof(ForegroundColor));
            }
        }

        private bool isHovered;
        public bool IsHovered
        {
            get => isHovered;
            set
            {
                isHovered = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BackgroundColor));
                OnPropertyChanged(nameof(ForegroundColor));
            }
        }

        public string Id => Config.Id;
        public BitmapImage Icon => Images.Base64ToBitmapImage(Config.Metadata.Base64Image);
        public string Name => Config.Metadata.Name;
        public string Author => Config.Metadata.Author;
        public string Category => Config.Metadata.Category;
        public bool NeedsProxies => Config.Settings.ProxySettings.UseProxies;
        public string AllowedWordlistTypes => Config.Settings.DataSettings.AllowedWordlistTypesString;
        public DateTime CreationDate => Config.Metadata.CreationDate;
        public DateTime LastModified => Config.Metadata.LastModified;
        public string Readme => Config.Readme;
        public bool IsRemote => Config.IsRemote;

        public string BackgroundColor
        {
            get
            {
                if (IsSelected)
                {
                    return "#BBDEFB"; // Light blue for selected config
                }
                else if (IsHovered)
                {
                    return "#E0E0E0"; // Slightly darker gray for hovered config
                }
                else if (IsRemote)
                {
                    return "#BDBDBD"; // Muted gray for remote configs
                }
                else
                {
                    return "#F5F5F5"; // Light gray for normal configs
                }
            }
        }

        public string ForegroundColor
        {
            get
            {
                if (IsHovered)
                {
                    return "#FFFFFF"; // White for hovered config
                }
                else if (IsRemote)
                {
                    return "#757575"; // Light gray for remote configs
                }
                else
                {
                    return "#0D47A1"; // Dark blue for normal/selected configs
                }
            }
        }

        public ConfigViewModel(Config config)
        {
            Config = config;
        }
    } // Closing ConfigViewModel class
} // Closing namespace
