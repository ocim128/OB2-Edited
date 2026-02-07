using RuriLib.Logging;

namespace Flux.Web.Dtos.ConfigDebugger;

/// <summary>
/// A new message was logged.
/// </summary>
public class DbgNewLogMessage
{
    /// <summary>
    /// The new log message.
    /// </summary>
    public BotLoggerEntry? NewMessage { get; set; }
}
