using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using RuriLib.Helpers;
using RuriLib.Models.Data.Resources;
using RuriLib.Models.Jobs.Execution;
using RuriLib.Models.Proxies;
using RuriLib.Parallelization;
using RuriLib.Models.Scripting;

namespace RuriLib.Models.Jobs;

internal sealed class JobInitializationResult
{
    public required AsyncLocker AsyncLocker { get; init; }
    public required BotExecutionCoordinator ExecutionCoordinator { get; init; }
    public required Dictionary<string, ConfigResource> Resources { get; init; }
    public required HttpClient HttpClient { get; init; }
    public required Lazy<dynamic> PythonEngine { get; init; }
    public required dynamic GlobalVariables { get; init; }
    public ProxyPool? ProxyPool { get; init; }
    public MethodInfo? DllMethod { get; init; }
    public IScript? Script { get; init; }
}
