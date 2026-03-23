using System;
using Flux.Core.Models.Jobs;
using RuriLib.Models.Jobs;

namespace Flux.Shared.Models;

public record DesktopJobListItemDto(
    int Id,
    JobType JobType,
    JobStatus Status,
    string Name,
    string ConfigDisplayName,
    string JobTypeDisplay,
    string DataPoolInfo,
    string DataPoolDisplayInfo,
    int Bots,
    int Skip,
    JobProxyMode ProxyMode,
    int Cpm,
    double Progress,
    long TestedCount,
    long TotalCount,
    int DataHits,
    int DataCustom,
    DateTime StartTime,
    TimeSpan Elapsed,
    TimeSpan Remaining,
    bool CpmTriggerEnabled);
