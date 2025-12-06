using RuriLib.Services;
using OpenBullet2.Native.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using OpenBullet2.Native.Infrastructure.DependencyInjection;

namespace OpenBullet2.Native.ViewModels
{
    public class PluginsViewModel : OpenBullet2.Native.ViewModels.Infrastructure.ViewModelBase
    {
        private ObservableCollection<PluginInfo> pluginsCollection;
        private PluginRepository pluginRepo;
        private HotkeyService hotkeyService;

        public ObservableCollection<PluginInfo> PluginsCollection
        {
            get => pluginsCollection;
            set
            {
                pluginsCollection = value;
                OnPropertyChanged();
            }
        }

        public PluginsViewModel(
            PluginRepository pluginRepository,
            HotkeyService hotkeyService)
        {
            pluginRepo = pluginRepository ?? throw new ArgumentNullException(nameof(pluginRepository));
            this.hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
            
            RefreshList();
        }
        
        public bool HotkeysEnabled
        {
            get => hotkeyService?.IsEnabled ?? false;
            set
            {
                if (hotkeyService != null && hotkeyService.IsEnabled != value)
                {
                    hotkeyService.IsEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public void Add(string filePath)
        {
            var bytes = File.ReadAllBytes(filePath);
            using var ms = new MemoryStream(bytes);
            ms.Seek(0, SeekOrigin.Begin);

            pluginRepo.AddPlugin(ms);
            RefreshList();
        }

        public void RefreshList()
        {
            PluginsCollection = new ObservableCollection<PluginInfo>(
                pluginRepo.GetPluginNames().Select(p => new PluginInfo(p)));
        }

        public void Delete(PluginInfo plugin)
        {
            pluginRepo.DeletePlugin(plugin.Name);
            PluginsCollection.Remove(plugin);
        }
    }

    public class PluginInfo
    {
        public string Name { get; set; }

        public PluginInfo(string name)
        {
            Name = name;
        }
    }
}
