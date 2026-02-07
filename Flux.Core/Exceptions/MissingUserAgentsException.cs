using System;

namespace Flux.Core.Exceptions;

/// <summary>
/// Thrown when no valid user agents are found in the user-agents.json file.
/// </summary>
public class MissingUserAgentsException : Exception
{
    /// <summary>
    /// Creates a new MissingUserAgentsException.
    /// </summary>
    public MissingUserAgentsException(string message) : base(message) { }
}
