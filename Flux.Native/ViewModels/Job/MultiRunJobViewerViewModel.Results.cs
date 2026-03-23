using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Flux.Native.ViewModels.Base;
using Flux.Shared.Models;
using RuriLib.Models.Configs;

namespace Flux.Native.ViewModels.Jobs;

public partial class MultiRunJobViewerViewModel
{
    private ObservableCollection<HitViewModel> hitsCollection = [];
    public ObservableCollection<HitViewModel> HitsCollection
    {
        get => hitsCollection;
        private set
        {
            hitsCollection = value;
            OnPropertyChanged();
        }
    }

    public static IEnumerable<HitsFilter> HitsFilters => Enum.GetValues(typeof(HitsFilter)).Cast<HitsFilter>();

    private HitsFilter hitsFilter = HitsFilter.Hits;
    public HitsFilter HitsFilter
    {
        get => hitsFilter;
        set
        {
            hitsFilter = value;
            OnPropertyChanged();
            UpdateHitsCollection();
        }
    }

    private string searchQuery = string.Empty;
    public string SearchQuery
    {
        get => searchQuery;
        set
        {
            if (searchQuery == value)
            {
                return;
            }

            searchQuery = value;
            OnPropertyChanged();
            UpdateHitsCollection();
        }
    }

    public string GetAllHitsForClipboard()
        => string.Join(Environment.NewLine, allResults
            .Where(static hit => hit.Type == "SUCCESS")
            .Select(static hit => hit.Data ?? string.Empty));

    public string GetAllHitsWithCaptureForClipboard()
        => string.Join(Environment.NewLine, allResults
            .Where(static hit => hit.Type == "SUCCESS")
            .Select(static hit => $"{hit.Data} | {hit.Capture}"));

    private void UpdateHitsCollection()
    {
        var filteredHits = ApplySearchFilter(allResults)
            .Where(MatchesFilter)
            .Select(static hit => new HitViewModel(hit))
            .ToList();

        RunOnUiThread(() => HitsCollection = new ObservableCollection<HitViewModel>(filteredHits));
    }

    private IEnumerable<JobRuntimeResultDto> ApplySearchFilter(IEnumerable<JobRuntimeResultDto> hits)
    {
        var query = SearchQuery;
        if (string.IsNullOrWhiteSpace(query))
        {
            return hits;
        }

        return hits.Where(hit => HitMatchesSearch(hit, query));
    }

    private bool MatchesFilter(JobRuntimeResultDto hit)
        => HitsFilter switch
        {
            HitsFilter.Hits => hit.Type == "SUCCESS",
            HitsFilter.ToCheck => hit.Type == "NONE",
            HitsFilter.Custom => hit.Type is not "SUCCESS" and not "NONE" and not "FAIL",
            _ => false
        };

    private static bool HitMatchesSearch(JobRuntimeResultDto hit, string query)
        => ContainsIgnoreCase(hit.Data, query)
            || ContainsIgnoreCase(hit.Proxy, query)
            || ContainsIgnoreCase(hit.Type, query)
            || ContainsIgnoreCase(hit.Capture, query);

    private static bool ContainsIgnoreCase(string source, string query)
        => !string.IsNullOrEmpty(source) && source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
}

public class HitViewModel : ViewModelBase
{
    public HitViewModel(JobRuntimeResultDto hit)
    {
        Hit = hit;
    }

    public JobRuntimeResultDto Hit { get; }
    public string ResultId => Hit.Id;
    public DateTime Time => Hit.Timestamp;
    public string Data => Hit.Data;
    public string Proxy => Hit.Proxy;
    public string Type => Hit.Type;
    public string Capture => Hit.Capture;
    public RuriLib.Models.Proxies.ProxyType? ProxyType => Hit.ProxyType;
    public ConfigMode? ConfigMode => Hit.ConfigMode;
    public bool HasBotLog => Hit.HasBotLog;
}

public enum HitsFilter
{
    Hits = 0,
    Custom = 1,
    ToCheck = 2
}
