using System.Collections.Generic;

namespace Flux.Shared.Models;

public record BotLogDto(IReadOnlyList<BotLogEntryDto> Entries);
