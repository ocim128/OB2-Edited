using System;
using System.Collections.Generic;

namespace Flux.Shared.Models;

public record MultiRunJobViewerSnapshotDto(
    DesktopJobListItemDto Summary,
    bool HasConfig,
    string ConfigName,
    string ConfigAuthor,
    string ConfigIconBase64,
    string DataPoolInfo,
    string ProxySourcesInfo,
    string HitOutputsInfo,
    IReadOnlyList<CustomInputPromptDto> CustomInputs,
    IReadOnlyDictionary<string, string> CustomInputAnswers,
    DateTime? WaitUntil,
    int DataToCheck,
    int DataFails,
    int DataRetried,
    int DataBanned,
    int DataErrors,
    int DataInvalid,
    int ProxiesTotal,
    int ProxiesAlive,
    int ProxiesBad,
    int ProxiesBanned,
    decimal CaptchaCredit,
    IReadOnlyList<BotStateDto> Bots,
    IReadOnlyList<JobRuntimeResultDto> Results);
