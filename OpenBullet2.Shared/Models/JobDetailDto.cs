using System;
using System.Collections.Generic;

namespace OpenBullet2.Shared.Models;

public record JobDetailDto(
    JobSummaryDto Summary,
    string DataPool,
    IEnumerable<JobResultDto> RecentResults,
    JobCountersDto Counters,
    IEnumerable<BotStateDto> Bots,
    IEnumerable<NotificationDto> Notifications);
