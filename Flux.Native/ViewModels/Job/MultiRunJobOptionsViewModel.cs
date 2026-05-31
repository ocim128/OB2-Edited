using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flux.Core.Entities;
using Flux.Core.Models.Data;
using Flux.Core.Models.Hits;
using Flux.Core.Models.Jobs;
using Flux.Core.Models.Proxies;
using Flux.Core.Repositories;
using Flux.Core.Services;
using Flux.Native.Helpers;
using Flux.Native.Utils;
using Flux.Native.ViewModels.Base;
using Flux.Native.ViewModels.Configs;
using Flux.Native.ViewModels.Settings.Metadata;
using Microsoft.EntityFrameworkCore;
using RuriLib.Models.Jobs;
using RuriLib.Models.Jobs.StartConditions;
using RuriLib.Models.Proxies;
using RuriLib.Services;

namespace Flux.Native.ViewModels.Jobs;

public class MultiRunJobOptionsViewModel : ViewModelBase
{
    private readonly IRecordRepository recordRepo;
    private readonly IWordlistRepository wordlistRepo;
    private readonly RuriLibSettingsService rlSettingsService;
    private readonly ConfigService configService;
    private readonly JobFactoryService jobFactory;
    private readonly IProxyGroupRepository proxyGroupRepo;

    public MultiRunJobOptionsViewModel(
        MultiRunJobOptions? options,
        IRecordRepository recordRepo,
        IWordlistRepository wordlistRepo,
        RuriLibSettingsService rlSettingsService,
        ConfigService configService,
        JobFactoryService jobFactory,
        IProxyGroupRepository proxyGroupRepo)
    {
        Options = options ?? JobOptionsFactory.CreateNew(JobType.MultiRun) as MultiRunJobOptions;
        this.recordRepo = recordRepo;
        this.wordlistRepo = wordlistRepo;
        this.rlSettingsService = rlSettingsService;
        this.configService = configService;
        this.jobFactory = jobFactory;
        this.proxyGroupRepo = proxyGroupRepo;

        SetConfigData();

        DataPoolOptions = Options.DataPool switch
        {
            WordlistDataPoolOptions w => new WordlistDataPoolOptionsViewModel(w, wordlistRepo),
            FileDataPoolOptions f => new FileDataPoolOptionsViewModel(f),
            RangeDataPoolOptions r => new RangeDataPoolOptionsViewModel(r),
            CombinationsDataPoolOptions c => new CombinationsDataPoolOptionsViewModel(c),
            InfiniteDataPoolOptions i => new InfiniteDataPoolOptionsViewModel(i),
            _ => throw new NotImplementedException()
        };

        proxyGroups = proxyGroupRepo.GetAll().AsNoTracking().ToList();
        PopulateProxySources();
        HitOutputsCollection = new ObservableCollection<HitOutputOptions>(Options.HitOutputs);

        ExecutionFields = BuildExecutionFields();
        ProxyOptionFields = BuildProxyOptionFields();
        AutomationFields = BuildAutomationFields();
        RefreshConfigurationFields();
    }

    public MultiRunJobOptions Options { get; init; }

    public IReadOnlyList<MetadataFieldViewModel> ExecutionFields { get; }
    public IReadOnlyList<MetadataFieldViewModel> ProxyOptionFields { get; }
    public IReadOnlyList<MetadataFieldViewModel> AutomationFields { get; }

    #region Start Condition
    public event Action<StartConditionMode> StartConditionModeChanged;

    public StartConditionMode StartConditionMode
    {
        get => Options.StartCondition switch
        {
            RelativeTimeStartCondition => StartConditionMode.Relative,
            AbsoluteTimeStartCondition => StartConditionMode.Absolute,
            _ => throw new NotImplementedException()
        };
        set
        {
            Options.StartCondition = value switch
            {
                StartConditionMode.Relative => new RelativeTimeStartCondition(),
                StartConditionMode.Absolute => new AbsoluteTimeStartCondition(),
                _ => throw new NotImplementedException()
            };

            OnPropertyChanged();
            OnPropertyChanged(nameof(StartInMode));
            OnPropertyChanged(nameof(StartAtMode));
            StartConditionModeChanged?.Invoke(StartConditionMode);
        }
    }

    public bool StartInMode
    {
        get => StartConditionMode is StartConditionMode.Relative;
        set
        {
            if (value)
            {
                StartConditionMode = StartConditionMode.Relative;
            }

            OnPropertyChanged();
        }
    }

    public bool StartAtMode
    {
        get => StartConditionMode is StartConditionMode.Absolute;
        set
        {
            if (value)
            {
                StartConditionMode = StartConditionMode.Absolute;
            }

            OnPropertyChanged();
        }
    }

    public DateTime StartAtTime
    {
        get => Options.StartCondition is AbsoluteTimeStartCondition abs ? abs.StartAt : DateTime.Now;
        set
        {
            if (Options.StartCondition is AbsoluteTimeStartCondition abs)
            {
                abs.StartAt = value;
            }

            OnPropertyChanged();
        }
    }

    public TimeSpan StartIn
    {
        get => Options.StartCondition is RelativeTimeStartCondition rel ? rel.StartAfter : TimeSpan.Zero;
        set
        {
            if (Options.StartCondition is RelativeTimeStartCondition rel)
            {
                rel.StartAfter = value;
            }

            OnPropertyChanged();
        }
    }
    #endregion

    #region Config
    private BitmapImage configIcon;
    public BitmapImage ConfigIcon
    {
        get => configIcon;
        private set
        {
            configIcon = value;
            OnPropertyChanged();
        }
    }

    private string configNameAndAuthor;
    public string ConfigNameAndAuthor
    {
        get => configNameAndAuthor;
        set
        {
            configNameAndAuthor = value;
            OnPropertyChanged();
        }
    }

    public bool IsConfigSelected => SelectedConfig is not null;
    public RuriLib.Models.Configs.Config SelectedConfig { get; private set; }

    public void SelectConfig(ConfigViewModel vm)
    {
        Options.ConfigId = vm.Config.Id;
        Options.Bots = vm.Config.Settings.GeneralSettings.SuggestedBots;
        SetConfigData();
        RefreshConfigurationFields();
    }

    private void SetConfigData()
    {
        SelectedConfig = configService.GetConfigsList().FirstOrDefault(c => c.Id == Options.ConfigId);

        if (SelectedConfig is null)
        {
            ConfigIcon = null;
            ConfigNameAndAuthor = string.Empty;
            OnPropertyChanged(nameof(IsConfigSelected));
            return;
        }

        ConfigIcon = Images.Base64ToBitmapImage(SelectedConfig.Metadata.Base64Image);
        ConfigNameAndAuthor = $"{SelectedConfig.Metadata.Name} by {SelectedConfig.Metadata.Author}";
        OnPropertyChanged(nameof(IsConfigSelected));
    }

    public async Task TrySetRecordAsync()
    {
        if (Options.DataPool is WordlistDataPoolOptions wdpo)
        {
            var record = await recordRepo.GetAll()
                .FirstOrDefaultAsync(r => r.ConfigId == Options.ConfigId && r.WordlistId == wdpo.WordlistId).ConfigureAwait(false);

            Options.Skip = record?.Checkpoint ?? 0;
            RefreshConfigurationFields();
        }
    }
    #endregion

    #region Hit Outputs
    private ObservableCollection<HitOutputOptions> hitOutputsCollection;
    public ObservableCollection<HitOutputOptions> HitOutputsCollection
    {
        get => hitOutputsCollection;
        set
        {
            hitOutputsCollection = value;
            OnPropertyChanged();
        }
    }

    public void AddDatabaseHitOutput()
    {
        if (!Options.HitOutputs.Any(o => o is DatabaseHitOutputOptions))
        {
            AddHitOutput(new DatabaseHitOutputOptions());
        }
    }

    public void AddFileSystemHitOutput() => AddHitOutput(new FileSystemHitOutputOptions());
    public void AddDiscordWebhookHitOutput() => AddHitOutput(new DiscordWebhookHitOutputOptions());
    public void AddTelegramBotHitOutput() => AddHitOutput(new TelegramBotHitOutputOptions());
    public void AddCustomWebhookHitOutput() => AddHitOutput(new CustomWebhookHitOutputOptions());

    private void AddHitOutput(HitOutputOptions options)
    {
        Options.HitOutputs.Add(options);
        HitOutputsCollection.Add(options);
    }

    public void RemoveHitOutput(HitOutputOptions hitOutput)
    {
        HitOutputsCollection.Remove(hitOutput);
        Options.HitOutputs.Remove(hitOutput);
    }
    #endregion

    #region Proxy Sources
    private readonly IEnumerable<ProxyGroupEntity> proxyGroups;
    public IEnumerable<string> ProxyGroupNames => new[] { "All" }.Concat(proxyGroups.Select(g => g.Name));
    public IEnumerable<ProxyType> ProxyTypes => Enum.GetValues(typeof(ProxyType)).Cast<ProxyType>();

    private ObservableCollection<ProxySourceOptionsViewModel> proxySourcesCollection;
    public ObservableCollection<ProxySourceOptionsViewModel> ProxySourcesCollection
    {
        get => proxySourcesCollection;
        set
        {
            proxySourcesCollection = value;
            OnPropertyChanged();
        }
    }

    public void AddGroupProxySource()
    {
        var options = new GroupProxySourceOptions();
        Options.ProxySources.Add(options);
        ProxySourcesCollection.Add(new GroupProxySourceOptionsViewModel(options, proxyGroups));
    }

    public void AddFileProxySource()
    {
        var options = new FileProxySourceOptions();
        Options.ProxySources.Add(options);
        ProxySourcesCollection.Add(new FileProxySourceOptionsViewModel(options));
    }

    public void AddRemoteProxySource()
    {
        var options = new RemoteProxySourceOptions();
        Options.ProxySources.Add(options);
        ProxySourcesCollection.Add(new RemoteProxySourceOptionsViewModel(options));
    }

    public void RemoveProxySource(ProxySourceOptionsViewModel vm)
    {
        ProxySourcesCollection.Remove(vm);
        Options.ProxySources.Remove(vm.Options);
    }

    private void PopulateProxySources()
    {
        ProxySourcesCollection = new ObservableCollection<ProxySourceOptionsViewModel>();

        foreach (var source in Options.ProxySources)
        {
            switch (source)
            {
                case GroupProxySourceOptions group:
                    ProxySourcesCollection.Add(new GroupProxySourceOptionsViewModel(group, proxyGroups));
                    break;

                case FileProxySourceOptions file:
                    ProxySourcesCollection.Add(new FileProxySourceOptionsViewModel(file));
                    break;

                case RemoteProxySourceOptions remote:
                    ProxySourcesCollection.Add(new RemoteProxySourceOptionsViewModel(remote));
                    break;
            }
        }
    }
    #endregion

    #region Data Pool
    private DataPoolOptionsViewModel dataPoolOptions;
    public DataPoolOptionsViewModel DataPoolOptions
    {
        get => dataPoolOptions;
        set
        {
            dataPoolOptions = value;
            Options.DataPool = dataPoolOptions.Options;
            OnPropertyChanged();
        }
    }

    public bool WordlistDataPoolMode
    {
        get => DataPoolOptions is WordlistDataPoolOptionsViewModel;
        set
        {
            if (value)
            {
                DataPoolOptions = new WordlistDataPoolOptionsViewModel(new WordlistDataPoolOptions(), wordlistRepo);
            }

            OnPropertyChanged();
        }
    }

    public bool FileDataPoolMode
    {
        get => DataPoolOptions is FileDataPoolOptionsViewModel;
        set
        {
            if (value)
            {
                DataPoolOptions = new FileDataPoolOptionsViewModel(new FileDataPoolOptions());
            }

            OnPropertyChanged();
        }
    }

    public bool RangeDataPoolMode
    {
        get => DataPoolOptions is RangeDataPoolOptionsViewModel;
        set
        {
            if (value)
            {
                DataPoolOptions = new RangeDataPoolOptionsViewModel(new RangeDataPoolOptions());
            }

            OnPropertyChanged();
        }
    }

    public bool CombinationsDataPoolMode
    {
        get => DataPoolOptions is CombinationsDataPoolOptionsViewModel;
        set
        {
            if (value)
            {
                DataPoolOptions = new CombinationsDataPoolOptionsViewModel(new CombinationsDataPoolOptions());
            }

            OnPropertyChanged();
        }
    }

    public bool InfiniteDataPoolMode
    {
        get => DataPoolOptions is InfiniteDataPoolOptionsViewModel;
        set
        {
            if (value)
            {
                DataPoolOptions = new InfiniteDataPoolOptionsViewModel(new InfiniteDataPoolOptions());
            }

            OnPropertyChanged();
        }
    }

    public IEnumerable<string> WordlistTypes => rlSettingsService.Environment.WordlistTypes.Select(t => t.Name);
    #endregion

    public Task AddWordlist(WordlistEntity entity) => wordlistRepo.AddAsync(entity);

    private IReadOnlyList<MetadataFieldViewModel> BuildExecutionFields() =>
    [
        IntField("Bots", () => Options.Bots, value => Options.Bots = value, 1, jobFactory.BotLimit),
        IntField("Skip", () => Options.Skip, value => Options.Skip = value, 0),
        EnumField("Proxy mode", () => Options.ProxyMode, value => Options.ProxyMode = (JobProxyMode)value, Enum.GetValues(typeof(JobProxyMode))),
        EnumField("No valid proxy behaviour", () => Options.NoValidProxyBehaviour, value => Options.NoValidProxyBehaviour = (NoValidProxyBehaviour)value, Enum.GetValues(typeof(NoValidProxyBehaviour))),
        IntField("Reload interval (sec)", () => Options.PeriodicReloadIntervalSeconds, value => Options.PeriodicReloadIntervalSeconds = value, 0),
        IntField("Proxy ban time (sec)", () => Options.ProxyBanTimeSeconds, value => Options.ProxyBanTimeSeconds = value, 0)
    ];

    private IReadOnlyList<MetadataFieldViewModel> BuildProxyOptionFields() =>
    [
        BoolField("Shuffle proxies", () => Options.ShuffleProxies, value => Options.ShuffleProxies = value),
        BoolField("Never ban proxies", () => Options.NeverBanProxies, value => Options.NeverBanProxies = value),
        BoolField("Concurrent mode (rotating services)", () => Options.ConcurrentProxyMode, value => Options.ConcurrentProxyMode = value),
        BoolField("Mark lines as ToCheck on abort", () => Options.MarkAsToCheckOnAbort, value => Options.MarkAsToCheckOnAbort = value)
    ];

    private IReadOnlyList<MetadataFieldViewModel> BuildAutomationFields() =>
    [
        BoolField("CPM trigger (Ctrl+Alt+Shift+Y modem refresh)", () => Options.CpmTriggerEnabled, value => Options.CpmTriggerEnabled = value),
        new MetadataMessageFieldViewModel("Starts 1 minute after the job runs. If CPM drops below 5000, triggers the modem refresh. Cooldown is 1 minute, retry every 5 seconds.", Brushes.LightGray)
    ];

    private void RefreshConfigurationFields()
    {
        foreach (var field in ExecutionFields.Concat(ProxyOptionFields).Concat(AutomationFields))
        {
            field.Refresh();
        }
    }

    private MetadataBooleanFieldViewModel BoolField(string label, Func<bool> getter, Action<bool> setter, string? description = null) =>
        new(label, getter, setter, description, RefreshConfigurationFields);

    private MetadataIntegerFieldViewModel IntField(string label, Func<int> getter, Action<int> setter, int minimum = 0, int maximum = int.MaxValue, int interval = 1, string? description = null) =>
        new(label, getter, setter, minimum, maximum, interval, description, RefreshConfigurationFields);

    private MetadataEnumFieldViewModel EnumField(string label, Func<object> getter, Action<object> setter, Array options, string? description = null) =>
        new(label, getter, setter, options, description, RefreshConfigurationFields);
}

public enum StartConditionMode
{
    Relative,
    Absolute
}

#region Data Pool ViewModels
public class DataPoolOptionsViewModel : ViewModelBase
{
    public DataPoolOptionsViewModel(DataPoolOptions options)
    {
        Options = options;
    }

    public DataPoolOptions Options { get; init; }
}

public class WordlistDataPoolOptionsViewModel : DataPoolOptionsViewModel
{
    private readonly IWordlistRepository wordlistRepo;
    private WordlistEntity wordlist;
    private WordlistDataPoolOptions WordlistOptions => Options as WordlistDataPoolOptions;

    public WordlistDataPoolOptionsViewModel(WordlistDataPoolOptions options, IWordlistRepository wordlistRepo) : base(options)
    {
        this.wordlistRepo = wordlistRepo;

        if (options.WordlistId != -1)
        {
            wordlist = Task.Run(() => wordlistRepo.GetAsync(options.WordlistId)).GetAwaiter().GetResult();
        }

        if (wordlist is null)
        {
            options.WordlistId = -1;
        }
    }

    public bool HasWordlist => WordlistOptions.WordlistId != -1 && wordlist is not null;
    public string Info => WordlistOptions.WordlistId == -1 ? "No wordlist selected" : $"{wordlist.Name} ({wordlist.Total} lines)";

    public void SelectWordlist(WordlistEntity selectedWordlist)
    {
        wordlist = selectedWordlist;
        WordlistOptions.WordlistId = selectedWordlist.Id;
        OnPropertyChanged(nameof(HasWordlist));
        OnPropertyChanged(nameof(Info));
    }
}

public class FileDataPoolOptionsViewModel : DataPoolOptionsViewModel
{
    private FileDataPoolOptions FileOptions => Options as FileDataPoolOptions;

    public FileDataPoolOptionsViewModel(FileDataPoolOptions options) : base(options)
    {
    }

    public string FileName
    {
        get => FileOptions.FileName;
        set
        {
            FileOptions.FileName = value;
            OnPropertyChanged();
        }
    }

    public string WordlistType
    {
        get => FileOptions.WordlistType;
        set
        {
            FileOptions.WordlistType = value;
            OnPropertyChanged();
        }
    }
}

public class RangeDataPoolOptionsViewModel : DataPoolOptionsViewModel
{
    private RangeDataPoolOptions RangeOptions => Options as RangeDataPoolOptions;

    public RangeDataPoolOptionsViewModel(RangeDataPoolOptions options) : base(options)
    {
    }

    public long Start
    {
        get => RangeOptions.Start;
        set
        {
            RangeOptions.Start = value;
            OnPropertyChanged();
        }
    }

    public int Amount
    {
        get => RangeOptions.Amount;
        set
        {
            RangeOptions.Amount = value;
            OnPropertyChanged();
        }
    }

    public int Step
    {
        get => RangeOptions.Step;
        set
        {
            RangeOptions.Step = value;
            OnPropertyChanged();
        }
    }

    public bool Pad
    {
        get => RangeOptions.Pad;
        set
        {
            RangeOptions.Pad = value;
            OnPropertyChanged();
        }
    }

    public string WordlistType
    {
        get => RangeOptions.WordlistType;
        set
        {
            RangeOptions.WordlistType = value;
            OnPropertyChanged();
        }
    }
}

public class CombinationsDataPoolOptionsViewModel : DataPoolOptionsViewModel
{
    private CombinationsDataPoolOptions CombinationsOptions => Options as CombinationsDataPoolOptions;

    public CombinationsDataPoolOptionsViewModel(CombinationsDataPoolOptions options) : base(options)
    {
    }

    public string CharSet
    {
        get => CombinationsOptions.CharSet;
        set
        {
            CombinationsOptions.CharSet = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GeneratedAmountText));
        }
    }

    public int Length
    {
        get => CombinationsOptions.Length;
        set
        {
            CombinationsOptions.Length = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GeneratedAmountText));
        }
    }

    public string WordlistType
    {
        get => CombinationsOptions.WordlistType;
        set
        {
            CombinationsOptions.WordlistType = value;
            OnPropertyChanged();
        }
    }

    public string GeneratedAmountText => $"{(long)Math.Pow(CharSet.Length, Length)} combinations will be generated";
}

public class InfiniteDataPoolOptionsViewModel : DataPoolOptionsViewModel
{
    private InfiniteDataPoolOptions InfiniteOptions => Options as InfiniteDataPoolOptions;

    public InfiniteDataPoolOptionsViewModel(InfiniteDataPoolOptions options) : base(options)
    {
    }

    public string WordlistType
    {
        get => InfiniteOptions.WordlistType;
        set
        {
            InfiniteOptions.WordlistType = value;
            OnPropertyChanged();
        }
    }
}
#endregion

#region Proxy Sources ViewModels
public class ProxySourceOptionsViewModel : ViewModelBase
{
    public ProxySourceOptionsViewModel(ProxySourceOptions options)
    {
        Options = options;
    }

    public ProxySourceOptions Options { get; init; }
}

public class GroupProxySourceOptionsViewModel : ProxySourceOptionsViewModel
{
    private readonly IEnumerable<ProxyGroupEntity> proxyGroups;
    private GroupProxySourceOptions GroupOptions => Options as GroupProxySourceOptions;

    public GroupProxySourceOptionsViewModel(GroupProxySourceOptions options, IEnumerable<ProxyGroupEntity> proxyGroups) : base(options)
    {
        this.proxyGroups = proxyGroups;
    }

    public string GroupName
    {
        get => GroupOptions.GroupId == -1 ? "All" : proxyGroups.FirstOrDefault(g => g.Id == GroupOptions.GroupId)?.Name ?? "All";
        set
        {
            GroupOptions.GroupId = value == "All" ? -1 : (proxyGroups.FirstOrDefault(g => g.Name == value)?.Id ?? -1);
            OnPropertyChanged();
        }
    }
}

public class FileProxySourceOptionsViewModel : ProxySourceOptionsViewModel
{
    private FileProxySourceOptions FileOptions => Options as FileProxySourceOptions;

    public FileProxySourceOptionsViewModel(FileProxySourceOptions options) : base(options)
    {
    }

    public string FileName
    {
        get => FileOptions.FileName;
        set
        {
            FileOptions.FileName = value;
            OnPropertyChanged();
        }
    }

    public ProxyType DefaultType
    {
        get => FileOptions.DefaultType;
        set
        {
            FileOptions.DefaultType = value;
            OnPropertyChanged();
        }
    }
}

public class RemoteProxySourceOptionsViewModel : ProxySourceOptionsViewModel
{
    private RemoteProxySourceOptions RemoteOptions => Options as RemoteProxySourceOptions;

    public RemoteProxySourceOptionsViewModel(RemoteProxySourceOptions options) : base(options)
    {
    }

    public string Url
    {
        get => RemoteOptions.Url;
        set
        {
            RemoteOptions.Url = value;
            OnPropertyChanged();
        }
    }

    public ProxyType DefaultType
    {
        get => RemoteOptions.DefaultType;
        set
        {
            RemoteOptions.DefaultType = value;
            OnPropertyChanged();
        }
    }
}
#endregion
