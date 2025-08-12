using System;
using RuriLib.Models.Configs;

namespace RuriLib.Models.Jobs.Execution;

/// <summary>
/// Factory for creating appropriate execution handlers based on config mode
/// </summary>
public static class ExecutionHandlerFactory
{
    /// <summary>
    /// Creates an execution handler for the specified config mode
    /// </summary>
    public static IBotExecutionHandler CreateHandler(ConfigMode mode)
    {
        return mode switch
        {
            ConfigMode.DLL => new DllExecutionHandler(),
            ConfigMode.CSharp or ConfigMode.Stack or ConfigMode.LoliCode => new ScriptExecutionHandler(),
            _ => throw new NotSupportedException($"Config mode {mode} is not supported")
        };
    }
}