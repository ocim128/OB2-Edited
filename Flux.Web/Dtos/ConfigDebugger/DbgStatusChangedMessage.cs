using RuriLib.Models.Debugger;

namespace Flux.Web.Dtos.ConfigDebugger;

/// <summary>
/// The status changed.
/// </summary>
public class DbgStatusChangedMessage
{
    /// <summary>
    /// The new status.
    /// </summary>
    public ConfigDebuggerStatus NewStatus { get; set; }
}
