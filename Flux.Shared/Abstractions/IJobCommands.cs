using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Flux.Shared.Abstractions;

public interface IJobCommands
{
    Task StartAsync(int jobId, IReadOnlyDictionary<string, string>? customInputs = null, CancellationToken cancellationToken = default);
    Task StopAsync(int jobId, CancellationToken cancellationToken = default);
    Task AbortAsync(int jobId, CancellationToken cancellationToken = default);
    Task PauseAsync(int jobId, CancellationToken cancellationToken = default);
    Task ResumeAsync(int jobId, CancellationToken cancellationToken = default);
    Task ChangeBotsAsync(int jobId, int bots, CancellationToken cancellationToken = default);
    Task SkipWaitAsync(int jobId, CancellationToken cancellationToken = default);
    Task ResetSkipAsync(int jobId, CancellationToken cancellationToken = default);
}
