using MediatR;
using Microsoft.Extensions.Logging;
using OpenBullet2.Core.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenBullet2.Core.Application.Jobs;

public record StopJobCommand(int JobId) : IRequest;
public record PauseJobCommand(int JobId) : IRequest;
public record ResumeJobCommand(int JobId) : IRequest;
public record AbortJobCommand(int JobId) : IRequest;

public class JobOperationHandler(JobManagerService jobManager, ILogger<JobOperationHandler> logger) : 
    IRequestHandler<StopJobCommand>,
    IRequestHandler<PauseJobCommand>,
    IRequestHandler<ResumeJobCommand>,
    IRequestHandler<AbortJobCommand>
{
    public async Task Handle(StopJobCommand request, CancellationToken cancellationToken)
    {
        var job = GetJob(request.JobId);
        await job.Stop();
        logger.LogInformation("Stopped job {JobId}", request.JobId);
    }

    public async Task Handle(PauseJobCommand request, CancellationToken cancellationToken)
    {
        var job = GetJob(request.JobId);
        await job.Pause();
        logger.LogInformation("Paused job {JobId}", request.JobId);
    }

    public async Task Handle(ResumeJobCommand request, CancellationToken cancellationToken)
    {
        var job = GetJob(request.JobId);
        await job.Resume();
        logger.LogInformation("Resumed job {JobId}", request.JobId);
    }

    public async Task Handle(AbortJobCommand request, CancellationToken cancellationToken)
    {
        var job = GetJob(request.JobId);
        await job.Abort();
        logger.LogInformation("Aborted job {JobId}", request.JobId);
    }

    private RuriLib.Models.Jobs.Job GetJob(int id)
        => jobManager.Jobs.FirstOrDefault(j => j.Id == id) 
            ?? throw new KeyNotFoundException($"Job {id} not found");
}
