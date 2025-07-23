using System;
using OpenBullet2.Native.ViewModels;

namespace OpenBullet2.Native.Services
{
    public class ViewModelsService
    {
        private readonly Lazy<JobsViewModel> _jobs = new(() => new JobsViewModel());
        private readonly Lazy<ProxiesViewModel> _proxies = new(() => new ProxiesViewModel());
        private readonly Lazy<WordlistsViewModel> _wordlists = new(() => new WordlistsViewModel());
        private readonly Lazy<ConfigsViewModel> _configs = new(() => new ConfigsViewModel());
        private readonly Lazy<HitsViewModel> _hits = new(() => new HitsViewModel());
        private readonly Lazy<OBSettingsViewModel> _obSettings = new(() => new OBSettingsViewModel());
        private readonly Lazy<RLSettingsViewModel> _rlSettings = new(() => new RLSettingsViewModel());
        private readonly Lazy<PluginsViewModel> _plugins = new(() => new PluginsViewModel());
        private readonly Lazy<ConfigMetadataViewModel> _configMetadata = new(() => new ConfigMetadataViewModel());
        private readonly Lazy<ConfigReadmeViewModel> _configReadme = new(() => new ConfigReadmeViewModel());
        private readonly Lazy<ConfigStackerViewModel> _configStacker = new(() => new ConfigStackerViewModel());
        private readonly Lazy<ConfigSettingsViewModel> _configSettings = new(() => new ConfigSettingsViewModel());
        private readonly Lazy<DebuggerViewModel> _debugger = new(() => new DebuggerViewModel());

        public JobsViewModel Jobs => _jobs.Value;
        public ProxiesViewModel Proxies => _proxies.Value;
        public WordlistsViewModel Wordlists => _wordlists.Value;
        public ConfigsViewModel Configs => _configs.Value;
        public HitsViewModel Hits => _hits.Value;
        public OBSettingsViewModel OBSettings => _obSettings.Value;
        public RLSettingsViewModel RLSettings => _rlSettings.Value;
        public PluginsViewModel Plugins => _plugins.Value;
        public ConfigMetadataViewModel ConfigMetadata => _configMetadata.Value;
        public ConfigReadmeViewModel ConfigReadme => _configReadme.Value;
        public ConfigStackerViewModel ConfigStacker => _configStacker.Value;
        public ConfigSettingsViewModel ConfigSettings => _configSettings.Value;
        public DebuggerViewModel Debugger => _debugger.Value;
    }
}
