using MediatR;
using Microsoft.Extensions.Logging;
using OpenBullet2.Core.Extensions;
using OpenBullet2.Core.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenBullet2.Core.Application.Jobs;

public record StartJobCommand(int JobId, bool Wait = false) : IRequest;

public class StartJobHandler(JobManagerService jobManager, ILogger<StartJobHandler> logger) : IRequestHandler<StartJobCommand>
{
    public async Task Handle(StartJobCommand request, CancellationToken cancellationToken)
    {
        var job = jobManager.Jobs.FirstOrDefault(j => j.Id == request.JobId) 
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found");

        if (request.Wait)
        {
            await job.Start();
        }
        else
        {
            job.Start().Forget(e => logger.LogError(e, "Error while starting job {JobId}", request.JobId));
        }

        logger.LogInformation("Started job {JobId}", request.JobId);
    }
}
