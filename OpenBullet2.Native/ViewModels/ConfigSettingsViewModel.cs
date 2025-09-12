using OpenBullet2.Core.Services;
using RuriLib.Models.Configs;
using RuriLib.Models.Configs.Settings;
using RuriLib.Models.Data.Resources.Options;
using RuriLib.Models.Data.Rules;
using RuriLib.Models.Proxies;
using RuriLib.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OpenBullet2.Native.Infrastructure.DependencyInjection;

namespace OpenBullet2.Native.ViewModels
{
    public class ConfigSettingsViewModel : OpenBullet2.Native.ViewModels.Infrastructure.ViewModelBase
    {
        private readonly RuriLibSettingsService rlSettingsService;
        private readonly ConfigService configService;
        private Config Config => configService.SelectedConfig;
        private GeneralSettings General => Config.Settings.GeneralSettings;
        private ProxySettings Proxy => Config.Settings.ProxySettings;
        private DataSettings Data => Config.Settings.DataSettings;
        private InputSettings Input => Config.Settings.InputSettings;
        private BrowserSettings Puppeteer => Config.Settings.BrowserSettings;

        // Direct binding to model properties - reduces wrapper overhead
        public GeneralSettings GeneralSettings => General;
        public ProxySettings ProxySettings => Proxy;
        public DataSettings DataSettings => Data;
        public InputSettings InputSettings => Input;
        public BrowserSettings BrowserSettings => Puppeteer;

        // Lazy-loaded collections for better performance
        private IEnumerable<string> _allStatuses;
        private IEnumerable<string> _proxyTypes;
        private IEnumerable<string> _wordlistTypes;
        
        public IEnumerable<string> AllStatuses => _allStatuses ??= rlSettingsService.GetStatuses();
        public IEnumerable<string> ProxyTypes => _proxyTypes ??= Enum.GetNames(typeof(ProxyType));
        public IEnumerable<string> WordlistTypes => _wordlistTypes ??= rlSettingsService.Environment.WordlistTypes.Select(w => w.Name);

        private ObservableCollection<string> continueStatuses;
        public ObservableCollection<string> ContinueStatuses
        {
            get => continueStatuses;
            set
            {
                continueStatuses = value;
                General.ContinueStatuses = continueStatuses.ToArray();
                OnPropertyChanged();
            }
        }

        // Removed redundant proxy property wrappers - use direct binding

        private ObservableCollection<string> proxyBanStatuses;
        public ObservableCollection<string> ProxyBanStatuses
        {
            get => proxyBanStatuses;
            set
            {
                proxyBanStatuses = value;
                Proxy.BanProxyStatuses = proxyBanStatuses.ToArray();
                OnPropertyChanged();
            }
        }

        private ObservableCollection<string> allowedProxyTypes;
        public ObservableCollection<string> AllowedProxyTypes
        {
            get => allowedProxyTypes;
            set
            {
                allowedProxyTypes = value;
                Proxy.AllowedProxyTypes = allowedProxyTypes
                    .Select(t => (ProxyType)Enum.Parse(typeof(ProxyType), t, true)).ToArray();
                OnPropertyChanged();
            }
        }

        private ObservableCollection<string> allowedWordlistTypes;
        public ObservableCollection<string> AllowedWordlistTypes
        {
            get => allowedWordlistTypes;
            set
            {
                allowedWordlistTypes = value;
                Data.AllowedWordlistTypes = allowedWordlistTypes.ToArray();
                OnPropertyChanged();
            }
        }

        // Removed redundant data property wrapper - use direct binding

        private IEnumerable<StringRule> _stringRules;
        public IEnumerable<StringRule> StringRules => _stringRules ??= Enum.GetValues(typeof(StringRule)).Cast<StringRule>();

        private ObservableCollection<DataRule> dataRulesCollection;
        public ObservableCollection<DataRule> DataRulesCollection
        {
            get => dataRulesCollection;
            set
            {
                dataRulesCollection = value;
                OnPropertyChanged();
            }
        }

        private string testDataForRules = string.Empty;
        public string TestDataForRules
        {
            get => testDataForRules;
            set
            {
                testDataForRules = value;
                OnPropertyChanged();
            }
        }

        private string testWordlistTypeForRules;
        public string TestWordlistTypeForRules
        {
            get => testWordlistTypeForRules;
            set
            {
                testWordlistTypeForRules = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<ConfigResourceOptions> resourcesCollection;
        public ObservableCollection<ConfigResourceOptions> ResourcesCollection
        {
            get => resourcesCollection;
            set
            {
                resourcesCollection = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<CustomInput> customInputsCollection;
        public ObservableCollection<CustomInput> CustomInputsCollection
        {
            get => customInputsCollection;
            set
            {
                customInputsCollection = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<string> quitBrowserStatuses;
        public ObservableCollection<string> QuitBrowserStatuses
        {
            get => quitBrowserStatuses;
            set
            {
                quitBrowserStatuses = value;
                Puppeteer.QuitBrowserStatuses = quitBrowserStatuses.ToArray();
                OnPropertyChanged();
            }
        }

        // Removed redundant browser property wrappers - use direct binding
        public List<string> BlockedUrls
        {
            get => Puppeteer.BlockedUrls;
            set
            {
                Puppeteer.BlockedUrls = value;
                OnPropertyChanged();
            }
        }

        public ConfigSettingsViewModel()
        {
            configService = ServiceLocator.GetService<ConfigService>();
            rlSettingsService = ServiceLocator.GetService<RuriLibSettingsService>();
            // Defer initialization until needed
        }

        public override void UpdateViewModel()
        {
            CreateCollections();
            // Initialize test wordlist type only when needed
            if (string.IsNullOrEmpty(TestWordlistTypeForRules) && WordlistTypes.Any())
                TestWordlistTypeForRules = WordlistTypes.First();
            base.UpdateViewModel();
        }

        public void AddCustomInput()
        {
            CustomInputsCollection.Add(new CustomInput());
            Input.CustomInputs = CustomInputsCollection.ToList();
        }

        public void RemoveCustomInput(CustomInput input)
        {
            CustomInputsCollection.Remove(input);
            Input.CustomInputs = CustomInputsCollection.ToList();
        }

        public void AddLinesFromFileResource()
        {
            ResourcesCollection.Add(new LinesFromFileResourceOptions());
            SaveResources();
        }

        public void AddRandomLinesFromFileResource()
        {
            ResourcesCollection.Add(new RandomLinesFromFileResourceOptions());
            SaveResources();
        }

        public void RemoveResource(ConfigResourceOptions resource)
        {
            ResourcesCollection.Remove(resource);
            SaveResources();
        }

        public void AddSimpleDataRule()
        {
            DataRulesCollection.Add(new SimpleDataRule());
            SaveDataRules();
        }

        public void AddRegexDataRule()
        {
            DataRulesCollection.Add(new RegexDataRule());
            SaveDataRules();
        }

        public void RemoveDataRule(DataRule rule)
        {
            DataRulesCollection.Remove(rule);
            SaveDataRules();
        }

        private void SaveResources() => Data.Resources = ResourcesCollection.ToList();
        private void SaveDataRules() => Data.DataRules = DataRulesCollection.ToList();

        private void CreateCollections()
        {
            // Only create collections if they don't exist or if data has changed
            if (continueStatuses == null || !continueStatuses.SequenceEqual(General.ContinueStatuses))
                ContinueStatuses = new ObservableCollection<string>(General.ContinueStatuses);
            
            if (proxyBanStatuses == null || !proxyBanStatuses.SequenceEqual(Proxy.BanProxyStatuses))
                ProxyBanStatuses = new ObservableCollection<string>(Proxy.BanProxyStatuses);
            
            if (allowedProxyTypes == null || !allowedProxyTypes.SequenceEqual(Proxy.AllowedProxyTypes.Select(t => t.ToString())))
                AllowedProxyTypes = new ObservableCollection<string>(Proxy.AllowedProxyTypes.Select(t => t.ToString()));
            
            if (allowedWordlistTypes == null || !allowedWordlistTypes.SequenceEqual(Data.AllowedWordlistTypes))
                AllowedWordlistTypes = new ObservableCollection<string>(Data.AllowedWordlistTypes);
            
            if (quitBrowserStatuses == null || !quitBrowserStatuses.SequenceEqual(Puppeteer.QuitBrowserStatuses))
                QuitBrowserStatuses = new ObservableCollection<string>(Puppeteer.QuitBrowserStatuses);

            if (customInputsCollection == null || customInputsCollection.Count != Input.CustomInputs.Count)
                CustomInputsCollection = new ObservableCollection<CustomInput>(Input.CustomInputs);
            
            if (resourcesCollection == null || resourcesCollection.Count != Data.Resources.Count)
                ResourcesCollection = new ObservableCollection<ConfigResourceOptions>(Data.Resources);
            
            if (dataRulesCollection == null || dataRulesCollection.Count != Data.DataRules.Count)
                DataRulesCollection = new ObservableCollection<DataRule>(Data.DataRules);
        }
    }
}
