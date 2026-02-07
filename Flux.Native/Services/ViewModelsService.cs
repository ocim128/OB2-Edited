using System;
using Flux.Native.ViewModels;
using Flux.Native.ViewModels.Configs;
using Flux.Native.ViewModels.Jobs;
using Flux.Native.ViewModels.Data;
using Flux.Native.ViewModels.Settings;
using Flux.Native.ViewModels.Tools;
using Flux.Native.ViewModels.Shared;

namespace Flux.Native.Services
{
    public class ViewModelsService
    {
        public JobsViewModel Jobs { get; }
        public ProxiesViewModel Proxies { get; }
        public WordlistsViewModel Wordlists { get; }
        public ConfigsViewModel Configs { get; }
        public HitsViewModel Hits { get; }
        public OBSettingsViewModel OBSettings { get; }
        public RLSettingsViewModel RLSettings { get; }
        public PluginsViewModel Plugins { get; }
        public ConfigMetadataViewModel ConfigMetadata { get; }
        public ConfigReadmeViewModel ConfigReadme { get; }
        public ConfigStackerViewModel ConfigStacker { get; }
        public ConfigSettingsViewModel ConfigSettings { get; }
        public DebuggerViewModel Debugger { get; }

        public ViewModelsService(
            JobsViewModel jobs,
            ProxiesViewModel proxies,
            WordlistsViewModel wordlists,
            ConfigsViewModel configs,
            HitsViewModel hits,
            OBSettingsViewModel fluxSettings,
            RLSettingsViewModel rlSettings,
            PluginsViewModel plugins,
            ConfigMetadataViewModel configMetadata,
            ConfigReadmeViewModel configReadme,
            ConfigStackerViewModel configStacker,
            ConfigSettingsViewModel configSettings,
            DebuggerViewModel debugger)
        {
            Jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            Proxies = proxies ?? throw new ArgumentNullException(nameof(proxies));
            Wordlists = wordlists ?? throw new ArgumentNullException(nameof(wordlists));
            Configs = configs ?? throw new ArgumentNullException(nameof(configs));
            Hits = hits ?? throw new ArgumentNullException(nameof(hits));
            OBSettings = fluxSettings ?? throw new ArgumentNullException(nameof(fluxSettings));
            RLSettings = rlSettings ?? throw new ArgumentNullException(nameof(rlSettings));
            Plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
            ConfigMetadata = configMetadata ?? throw new ArgumentNullException(nameof(configMetadata));
            ConfigReadme = configReadme ?? throw new ArgumentNullException(nameof(configReadme));
            ConfigStacker = configStacker ?? throw new ArgumentNullException(nameof(configStacker));
            ConfigSettings = configSettings ?? throw new ArgumentNullException(nameof(configSettings));
            Debugger = debugger ?? throw new ArgumentNullException(nameof(debugger));
        }
    }
}
