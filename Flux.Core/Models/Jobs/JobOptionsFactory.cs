using Flux.Core.Models.Hits;
using Flux.Core.Models.Proxies;
using RuriLib.Helpers;
using RuriLib.Models.Jobs.StartConditions;
using System;
using System.Collections.Generic;

namespace Flux.Core.Models.Jobs;

/// <summary>
/// A factory that creates a <see cref="JobOptions"/> object with default values.
/// </summary>
public class JobOptionsFactory
{
    /// <summary>
    /// Creates a new <see cref="JobOptions"/> object according to the provided <paramref name="type"/>.
    /// </summary>
    public static JobOptions CreateNew(JobType type)
    {
        JobOptions options = type switch
        {
            JobType.MultiRun => MakeMultiRun(),
            JobType.ProxyCheck => MakeProxyCheck(),
            _ => throw new NotImplementedException()
        };

        options.StartCondition = new RelativeTimeStartCondition();
        return options;
    }

    private static MultiRunJobOptions MakeMultiRun() => new()
    {
        HitOutputs = new List<HitOutputOptions>(),
        ProxySources = new List<ProxySourceOptions>()
    };

    private static ProxyCheckJobOptions MakeProxyCheck() => new ProxyCheckJobOptions
    {
        CheckOutput = new DatabaseProxyCheckOutputOptions()
    };

    public static JobOptions CloneExistant(JobOptions options) => options switch
    {
        MultiRunJobOptions x => CloneMultiRun(x),
        ProxyCheckJobOptions x => Cloner.Clone(x),
        _ => throw new NotImplementedException()
    };

    private static MultiRunJobOptions CloneMultiRun(MultiRunJobOptions options)
    {
        var newOptions = Cloner.Clone(options);
        newOptions.Skip = 0;
        return newOptions;
    }
}
