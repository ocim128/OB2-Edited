using System;
using System.IO;
using System.Windows.Media;
using Flux.Core.Models.Jobs;
using Flux.Native.ViewModels.Base;
using Flux.Shared.Models;
using RuriLib.Models.Jobs;

namespace Flux.Native.ViewModels.Jobs;

public class JobViewModel : ViewModelBase
{
    private DesktopJobListItemDto snapshot;

    protected JobViewModel(DesktopJobListItemDto snapshot)
    {
        this.snapshot = snapshot;
    }

    protected DesktopJobListItemDto Snapshot
    {
        get => snapshot;
        private set => snapshot = value;
    }

    public int Id => Snapshot.Id;
    public JobType JobType => Snapshot.JobType;
    public JobStatus Status => Snapshot.Status;
    public int Bots => Snapshot.Bots;
    public int Skip => Snapshot.Skip;
    public JobProxyMode ProxyMode => Snapshot.ProxyMode;
    public virtual int DataHits => Snapshot.DataHits;
    public virtual int DataCustom => Snapshot.DataCustom;
    public virtual int CPM => Snapshot.Cpm;
    public virtual double Progress => Snapshot.Progress;
    public virtual long TestedCount => Snapshot.TestedCount;
    public virtual long TotalCount => Snapshot.TotalCount;
    public DateTime StartTime => Snapshot.StartTime;
    public TimeSpan Elapsed => Snapshot.Elapsed;
    public TimeSpan Remaining => Snapshot.Remaining;
    public bool CpmTriggerEnabled => Snapshot.CpmTriggerEnabled;

    public string IdAndStatus => $"#{Id} [{Status}]";

    public virtual string StatusDisplayText => Status switch
    {
        JobStatus.Idle => "IDLE",
        JobStatus.Waiting => "WAITING",
        JobStatus.Starting => "STARTING",
        JobStatus.Running => "RUNNING",
        JobStatus.Pausing => "PAUSING",
        JobStatus.Paused => "PAUSED",
        JobStatus.Stopping => "STOPPING",
        JobStatus.Resuming => "RESUMING",
        _ => "UNKNOWN"
    };

    public virtual SolidColorBrush StatusColor => Status switch
    {
        JobStatus.Idle => new SolidColorBrush(Color.FromRgb(108, 117, 125)),
        JobStatus.Waiting => new SolidColorBrush(Color.FromRgb(23, 162, 184)),
        JobStatus.Starting => new SolidColorBrush(Color.FromRgb(255, 193, 7)),
        JobStatus.Running => new SolidColorBrush(Color.FromRgb(40, 167, 69)),
        JobStatus.Pausing => new SolidColorBrush(Color.FromRgb(255, 193, 7)),
        JobStatus.Paused => new SolidColorBrush(Color.FromRgb(253, 126, 20)),
        JobStatus.Stopping => new SolidColorBrush(Color.FromRgb(220, 53, 69)),
        JobStatus.Resuming => new SolidColorBrush(Color.FromRgb(23, 162, 184)),
        _ => new SolidColorBrush(Color.FromRgb(108, 117, 125))
    };

    public string ElapsedString => $"{(int)Elapsed.TotalDays} day(s) {Elapsed:hh\\:mm\\:ss}";
    public string RemainingString =>
        Status == JobStatus.Idle || Status == JobStatus.Stopping || Progress >= 1.0 || TestedCount >= TotalCount
            ? "0 day(s) 00:00:00"
            : $"{(int)Remaining.TotalDays} day(s) {Remaining:hh\\:mm\\:ss}";

    public string ElapsedStringHuman => FormatTimeSpanHuman(Elapsed);
    public string RemainingStringHuman =>
        Status == JobStatus.Idle || Status == JobStatus.Stopping || Progress >= 1.0 || TestedCount >= TotalCount
            ? "--"
            : FormatTimeSpanHuman(Remaining);

    public virtual string ProgressString => $"{TestedCount} / {TotalCount} ({(Progress < 0 ? 0 : Progress * 100):0.00}%)";

    public virtual void ApplySnapshot(DesktopJobListItemDto snapshot)
    {
        Snapshot = snapshot;
        UpdateViewModel();
        UpdateStats();
        PeriodicUpdate();
    }

    public override void UpdateViewModel()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusDisplayText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(IdAndStatus));
        OnPropertyChanged(nameof(Bots));
        OnPropertyChanged(nameof(Skip));
        OnPropertyChanged(nameof(ProxyMode));
        OnPropertyChanged(nameof(CPM));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(TestedCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ProgressString));
    }

    public virtual void PeriodicUpdate()
    {
        OnPropertyChanged(nameof(ElapsedString));
        OnPropertyChanged(nameof(RemainingString));
        OnPropertyChanged(nameof(ElapsedStringHuman));
        OnPropertyChanged(nameof(RemainingStringHuman));
        OnPropertyChanged(nameof(CPM));
    }

    public virtual void UpdateStats()
    {
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressString));
        OnPropertyChanged(nameof(DataHits));
        OnPropertyChanged(nameof(DataCustom));
    }

    public void UpdateStatus()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IdAndStatus));
    }

    protected static string FormatTimeSpanHuman(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays}d {span.Hours}h";
        }

        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        }

        if (span.TotalMinutes >= 1)
        {
            return $"{(int)span.TotalMinutes}m {span.Seconds}s";
        }

        return $"{span.Seconds}s";
    }
}

public class MultiRunJobViewModel : JobViewModel
{
    public MultiRunJobViewModel(DesktopJobListItemDto snapshot) : base(snapshot)
    {
    }

    public string ConfigName => Snapshot.ConfigDisplayName;
    public string ConfigDisplayName => ConfigName;
    public string JobTypeDisplay => Snapshot.JobTypeDisplay;
    public string DataPoolInfo => Snapshot.DataPoolInfo;
    public string DataPoolDisplayInfo => Snapshot.DataPoolDisplayInfo;

    public void UpdateBots() => OnPropertyChanged(nameof(Bots));

    public void UpdateSkip() => OnPropertyChanged(nameof(Skip));

    public override void UpdateViewModel()
    {
        base.UpdateViewModel();
        OnPropertyChanged(nameof(ConfigName));
        OnPropertyChanged(nameof(ConfigDisplayName));
        OnPropertyChanged(nameof(JobTypeDisplay));
        OnPropertyChanged(nameof(DataPoolInfo));
        OnPropertyChanged(nameof(DataPoolDisplayInfo));
    }
}

public class ProxyCheckJobViewModel : JobViewModel
{
    public ProxyCheckJobViewModel(DesktopJobListItemDto snapshot) : base(snapshot)
    {
    }

    public string ConfigDisplayName => "Proxy Check";
    public string JobTypeDisplay => Snapshot.JobTypeDisplay;
    public string DataPoolDisplayInfo => Snapshot.DataPoolDisplayInfo;
    public string Url => Snapshot.DataPoolDisplayInfo;
    public string UrlDisplayText => Snapshot.DataPoolDisplayInfo;

    public override void UpdateViewModel()
    {
        base.UpdateViewModel();
        OnPropertyChanged(nameof(ConfigDisplayName));
        OnPropertyChanged(nameof(JobTypeDisplay));
        OnPropertyChanged(nameof(DataPoolDisplayInfo));
        OnPropertyChanged(nameof(Url));
        OnPropertyChanged(nameof(UrlDisplayText));
    }
}
