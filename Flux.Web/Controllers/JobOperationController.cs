using MediatR;
using Microsoft.AspNetCore.Mvc;
using Flux.Core.Application.Jobs;
using Flux.Core.Models.Jobs;
using Flux.Core.Services;
using Flux.Web.Auth;
using Flux.Web.Dtos.Job;
using Flux.Web.Dtos.Job.MultiRun;
using Flux.Web.Exceptions;
using Flux.Web.Extensions;
using Flux.Web.Models.Identity;
using RuriLib.Models.Jobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Flux.Web.Controllers;

/// <summary>
/// Manage job operations (Commands/RPC).
/// </summary>
[TypeFilter<GuestFilter>]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/job")]
public class JobOperationController(
    IMediator mediator,
    JobManagerService jobManager,
    ILogger<JobOperationController> logger) : ApiController
{
    private readonly IMediator _mediator = mediator;
    private readonly JobManagerService _jobManager = jobManager;
    private readonly ILogger<JobOperationController> _logger = logger;

    /// <summary>
    /// Start a job.
    /// </summary>
    [HttpPost("start")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult> StartJob(JobCommandDto dto)
    {
        EnsureOwnership(dto.JobId);
        await _mediator.Send(new StartJobCommand(dto.JobId, dto.Wait));
        return Ok();
    }

    /// <summary>
    /// Stop a job.
    /// </summary>
    [HttpPost("stop")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult> StopJob(JobCommandDto dto)
    {
        EnsureOwnership(dto.JobId);
        await _mediator.Send(new StopJobCommand(dto.JobId));
        return Ok();
    }

    /// <summary>
    /// Pause a job.
    /// </summary>
    [HttpPost("pause")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult> PauseJob(JobCommandDto dto)
    {
        EnsureOwnership(dto.JobId);
        await _mediator.Send(new PauseJobCommand(dto.JobId));
        return Ok();
    }

    /// <summary>
    /// Resume a paused job.
    /// </summary>
    [HttpPost("resume")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult> ResumeJob(JobCommandDto dto)
    {
        EnsureOwnership(dto.JobId);
        await _mediator.Send(new ResumeJobCommand(dto.JobId));
        return Ok();
    }

    /// <summary>
    /// Abort a job.
    /// </summary>
    [HttpPost("abort")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult> AbortJob(JobCommandDto dto)
    {
        EnsureOwnership(dto.JobId);
        await _mediator.Send(new AbortJobCommand(dto.JobId));
        return Ok();
    }

    /// <summary>
    /// Skip a job's waiting time.
    /// </summary>
    [HttpPost("skip-wait")]
    [MapToApiVersion("1.0")]
    public ActionResult SkipWaitJob(JobCommandDto dto)
    {
        var job = GetJob(dto.JobId);
        job.SkipWait();
        _logger.LogInformation("Skipped wait for job {JobId}", dto.JobId);
        return Ok();
    }

    /// <summary>
    /// Change the number of bots in a job.
    /// </summary>
    [HttpPost("change-bots")]
    [MapToApiVersion("1.0")]
    public ActionResult ChangeBots(ChangeBotsDto dto)
    {
        var job = GetJob(dto.JobId);

        if (job is MultiRunJob mrj) mrj.Bots = dto.Bots;
        else if (job is ProxyCheckJob pcj) pcj.Bots = dto.Bots;

        _logger.LogInformation("Changed bots to {Bots} for job {JobId}", dto.Bots, dto.JobId);
        return Ok();
    }

    /// <summary>
    /// Get the details of all bots in a multi run job.
    /// </summary>
    [HttpGet("bot-details")]
    [MapToApiVersion("1.0")]
    public ActionResult<IEnumerable<BotDetailsDto>> GetBotDetails(int id)
    {
        var job = GetJob<MultiRunJob>(id);
        return Ok(job.CurrentBotDatas
            .Where(b => b is not null)
            .Select(b => new BotDetailsDto {
                Id = b.BOTNUM,
                Data = b.Line.Data,
                Proxy = b.Proxy?.ToString() ?? "N/A",
                Info = b.ExecutionInfo
            }));
    }

    /// <summary>
    /// Get the custom user inputs that can be set in a given multi run job for
    /// the currently selected config.
    /// </summary>
    [HttpGet("multi-run/custom-inputs")]
    [MapToApiVersion("1.0")]
    public ActionResult<IEnumerable<CustomInputQuestionDto>> GetCustomInputs(int id)
    {
        var job = GetJob<MultiRunJob>(id);

        if (job.Config is null)
        {
            throw new BadRequestException(ErrorCode.InvalidJobConfiguration, $"The job with id {id} is missing a config");
        }

        return Ok(job.Config.Settings.InputSettings.CustomInputs.Select(i =>
            new CustomInputQuestionDto {
                Description = i.Description, DefaultAnswer = i.DefaultAnswer, VariableName = i.VariableName,
                CurrentAnswer = job.CustomInputsAnswers.TryGetValue(i.VariableName, out var answer) ? answer : null
            }));
    }

    /// <summary>
    /// Set the values of custom inputs in a multi run job for the
    /// currently selected config.
    /// </summary>
    [HttpPatch("multi-run/custom-inputs")]
    [MapToApiVersion("1.0")]
    public ActionResult SetCustomInputs(CustomInputsDto dto)
    {
        var job = GetJob<MultiRunJob>(dto.JobId);

        foreach (var input in dto.Answers)
        {
            job.CustomInputsAnswers[input.VariableName] = input.Answer;
        }
        
        _logger.LogInformation("Set custom inputs for job {Id}", dto.JobId);
        return Ok();
    }

    /// <summary>
    /// Get the full debugger log of a hit. Note that bot log must
    /// be enabled in the settings, or it will be blank.
    /// </summary>
    [HttpGet("hit-log")]
    [MapToApiVersion("1.0")]
    public ActionResult<string> GetHitLog(int jobId, string hitId)
    {
        var job = GetJob<MultiRunJob>(jobId);
        var hit = job.Hits.FirstOrDefault(h => h.Id == hitId);

        if (hit is null)
        {
            throw new KeyNotFoundException($"No hit with ID {hitId} in job {jobId}");
        }

        return Ok(hit.BotLogger is null ? string.Empty : string.Join(Environment.NewLine, hit.BotLogger.Entries.Select(e => e.Message)));
    }

    private T GetJob<T>(int id) where T : Job
    {
        var job = _jobManager.Jobs.FirstOrDefault(j => j.Id == id)
            ?? throw new KeyNotFoundException($"No job with ID {id}");

        if (job is not T casted)
        {
            throw new InvalidCastException($"Job with ID {id} is not a {typeof(T).Name}");
        }

        EnsureOwnership(job);
        return casted;
    }

    private Job GetJob(int id)
    {
        var job = _jobManager.Jobs.FirstOrDefault(j => j.Id == id)
            ?? throw new KeyNotFoundException($"No job with ID {id}");

        EnsureOwnership(job);
        return job;
    }

    private void EnsureOwnership(int jobId) => EnsureOwnership(GetJob(jobId));

    private void EnsureOwnership(Job job)
    {
        var apiUser = HttpContext.GetApiUser();

        if (apiUser.Role is UserRole.Guest && apiUser.Id != job.OwnerId)
        {
            _logger.LogWarning("Guest user {Username} tried to access a job not owned by them", apiUser.Username);
            throw new EntryNotFoundException(ErrorCode.JobNotFound, job.Id, nameof(JobManagerService));
        }
    }
}
