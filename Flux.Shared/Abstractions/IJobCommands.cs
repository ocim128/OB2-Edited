using System.Threading.Tasks;
using RuriLib.Models.Jobs;

namespace Flux.Shared.Abstractions;

public interface IJobCommands
{
    Task StartAsync(MultiRunJob job);
    Task StopAsync(MultiRunJob job);
    Task AbortAsync(MultiRunJob job);
    Task PauseAsync(MultiRunJob job);
    Task ResumeAsync(MultiRunJob job);
    Task ChangeBotsAsync(MultiRunJob job, int bots);
    void SkipWait(MultiRunJob job);
    void ResetSkip(MultiRunJob job);
}
