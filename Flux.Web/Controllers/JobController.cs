using AutoMapper;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Flux.Core.Application.Jobs;
using Flux.Core.Entities;
using Flux.Core.Models.Jobs;
using Flux.Core.Repositories;
using Flux.Core.Services;
using Flux.Web.Auth;
using Flux.Web.Dtos.Common;
using Flux.Web.Dtos.Job;
using Flux.Web.Dtos.Job.MultiRun;
using Flux.Web.Dtos.Job.ProxyCheck;
using Flux.Web.Dtos.JobMonitor;
using Flux.Web.Exceptions;
using Flux.Web.Extensions;
using Flux.Web.Models.Identity;
using Flux.Web.Utils;
using RuriLib.Models.Data.DataPools;
using RuriLib.Models.Hits.HitOutputs;
using RuriLib.Models.Jobs;
using RuriLib.Models.Jobs.Monitor;
using RuriLib.Models.Jobs.StartConditions;
using RuriLib.Models.Proxies.ProxySources;
using Flux.Core.Models.Hits;
using Flux.Core.Models.Proxies;
using Flux.Core.Models.Proxies.Sources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Flux.Web.Controllers;

/// <summary>
/// Manage jobs (CRUD, operations, and monitor).
/// </summary>
[TypeFilter<GuestFilter>]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/job")]
public class JobController(
    IJobRepository jobRepo,
    ILogger<JobController> logger,
    IGuestRepository guestRepo,
    IMapper mapper,
    JobManagerService jobManager,
    JobFactoryService jobFactory,
    IProxyGroupRepository proxyGroupRepo,
    IRecordRepository recordRepo,
    JobMonitorService jobMonitorService,
    IMediator mediator) : ApiController
{
    private readonly IGuestRepository _guestRepo = guestRepo;
    private readonly JobFactoryService _jobFactory = jobFactory;
    private readonly JobManagerService _jobManager = jobManager;
    private readonly IJobRepository _jobRepo = jobRepo;
    private readonly ILogger<JobController> _logger = logger;
    private readonly IMapper _mapper = mapper;
    private readonly IProxyGroupRepository _proxyGroupRepo = proxyGroupRepo;
    private readonly IRecordRepository _recordRepo = recordRepo;
    private readonly JobMonitorService _jobMonitorService = jobMonitorService;
    private readonly IMediator _mediator = mediator;

    #region CRUD Operations

    /// <summary>
    /// Get overview information about all jobs.
    /// </summary>
    [HttpGet("all")]
    [MapToApiVersion("1.0")]
    public ActionResult<IEnumerable<JobOverviewDto>> GetAll()
    {
        var apiUser = HttpContext.GetApiUser();

        var jobs = _jobManager.Jobs
            .Where(j => CanSee(apiUser, j))
            .OrderBy(j => j.Id);

        var mapped = jobs.Select(job => new JobOverviewDto {
                Id = job.Id,
                OwnerId = job.OwnerId,
                Type = GetJobType(job),
                Status = job.Status,
                Name = job.Name
            })
            .ToList();

        return Ok(mapped);
    }

    /// <summary>
    /// Get overview information about all multi run jobs.
    /// </summary>
    [HttpGet("multi-run/all")]
    [MapToApiVersion("1.0")]
    public ActionResult<IEnumerable<MultiRunJobOverviewDto>> GetAllMultiRunJobs()
        => Ok(_jobManager.Jobs
            .Where(j => CanSee(HttpContext.GetApiUser(), j) && j is MultiRunJob)
            .Cast<MultiRunJob>()
            .OrderBy(j => j.Id)
            .Select(MapMultiRunJobOverviewDto));

    /// <summary>
    /// Get overview information about all proxy check jobs.
    /// </summary>
    [HttpGet("proxy-check/all")]
    [MapToApiVersion("1.0")]
    public ActionResult<IEnumerable<ProxyCheckJobOverviewDto>> GetAllProxyCheckJobs()
        => Ok(_jobManager.Jobs
            .Where(j => CanSee(HttpContext.GetApiUser(), j) && j is ProxyCheckJob)
            .Cast<ProxyCheckJob>()
            .OrderBy(j => j.Id)
            .Select(j => {
                var dto = _mapper.Map<ProxyCheckJobOverviewDto>(j);
                dto.Type = JobType.ProxyCheck;
                return dto;
            }));

    /// <summary>
    /// Get a multi run job by ID.
    /// </summary>
    [HttpGet("multi-run")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<MultiRunJobDto>> GetMultiRunJob(int id)
        => await MapMultiRunJobDto(GetJob<MultiRunJob>(id));

    /// <summary>
    /// Get a proxy check job by ID.
    /// </summary>
    [HttpGet("proxy-check")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<ProxyCheckJobDto>> GetProxyCheckJob(int id)
        => await MapProxyCheckJobDto(GetJob<ProxyCheckJob>(id));

    /// <summary>
    /// Get the options of a multi run job. If <paramref name="id" /> is -1,
    /// the default options will be provided.
    /// </summary>
    [HttpGet("multi-run/options")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<MultiRunJobOptionsDto>> GetMultiRunJobOptions(int id = -1)
    {
        if (id == -1)
        {
            var options = JobOptionsFactory.CreateNew(JobType.MultiRun);
            var mapped = _mapper.Map<MultiRunJobOptionsDto>(options);
            mapped.Name = $"{HttpContext.GetApiUser().Username}'s job";
            return mapped;
        }

        var entity = await GetEntityAsync(id);
        EnsureOwnership(entity);

        var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
        var jobOptions = JsonConvert.DeserializeObject<JobOptionsWrapper>(entity.JobOptions, jsonSettings)?.Options;

        if (jobOptions is null)
        {
            throw new ApiException(ErrorCode.InvalidJobConfiguration, "The job options are null");
        }

        if (jobOptions is not MultiRunJobOptions mrjJobOptions)
        {
            throw new ApiException(ErrorCode.InvalidJobType, "Invalid job options type");
        }

        return _mapper.Map<MultiRunJobOptionsDto>(mrjJobOptions);
    }

    /// <summary>
    /// Get the options of a proxy check job. If <paramref name="id" /> is -1,
    /// the default options will be provided.
    /// </summary>
    [HttpGet("proxy-check/options")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<ProxyCheckJobOptionsDto>> GetProxyCheckJobOptions(int id = -1)
    {
        if (id == -1)
        {
            var options = JobOptionsFactory.CreateNew(JobType.ProxyCheck);
            var mapped = _mapper.Map<ProxyCheckJobOptionsDto>(options);
            mapped.Name = $"{HttpContext.GetApiUser().Username}'s job";
            return mapped;
        }

        var entity = await GetEntityAsync(id);
        EnsureOwnership(entity);

        var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
        var jobOptions = JsonConvert.DeserializeObject<JobOptionsWrapper>(entity.JobOptions, jsonSettings)?.Options;

        if (jobOptions is null)
        {
            throw new ApiException(ErrorCode.InvalidJobConfiguration, "The job options are null");
        }

        if (jobOptions is not ProxyCheckJobOptions pcJobOptions)
        {
            throw new ApiException(ErrorCode.InvalidJobType, "Invalid job options type");
        }

        return _mapper.Map<ProxyCheckJobOptionsDto>(pcJobOptions);
    }

    /// <summary>
    /// Create a multi run job.
    /// </summary>
    [HttpPost("multi-run")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<MultiRunJobDto>> CreateMultiRunJob(CreateMultiRunJobDto dto)
    {
        var apiUser = HttpContext.GetApiUser();
        var jobOptions = _mapper.Map<MultiRunJobOptions>(dto);
        var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
        var wrapper = new JobOptionsWrapper { Options = jobOptions };

        var entity = new JobEntity {
            Owner = await _guestRepo.GetAsync(apiUser.Id),
            CreationDate = DateTime.UtcNow,
            JobType = JobType.MultiRun,
            JobOptions = JsonConvert.SerializeObject(wrapper, jsonSettings)
        };

        await _jobRepo.AddAsync(entity);
        _logger.LogInformation("Created a new multi run job with id {Id}", entity.Id);

        try
        {
            var job = await _jobFactory.FromOptionsAsync(entity.Id, apiUser.Id, jobOptions);
            _jobManager.AddJob(job);
            return await MapMultiRunJobDto((MultiRunJob)job);
        }
        catch
        {
            await _jobRepo.DeleteAsync(entity);
            throw;
        }
    }

    /// <summary>
    /// Create a proxy check job.
    /// </summary>
    [HttpPost("proxy-check")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<ProxyCheckJobDto>> CreateProxyCheckJob(CreateProxyCheckJobDto dto)
    {
        var apiUser = HttpContext.GetApiUser();
        var jobOptions = _mapper.Map<ProxyCheckJobOptions>(dto);
        var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
        var wrapper = new JobOptionsWrapper { Options = jobOptions };

        var entity = new JobEntity {
            Owner = await _guestRepo.GetAsync(apiUser.Id),
            CreationDate = DateTime.UtcNow,
            JobType = JobType.ProxyCheck,
            JobOptions = JsonConvert.SerializeObject(wrapper, jsonSettings)
        };

        await _jobRepo.AddAsync(entity);
        _logger.LogInformation("Created a new proxy check job with id {Id}", entity.Id);

        try
        {
            var job = await _jobFactory.FromOptionsAsync(entity.Id, apiUser.Id, jobOptions);
            _jobManager.AddJob(job);
            return await MapProxyCheckJobDto((ProxyCheckJob)job);
        }
        catch
        {
            await _jobRepo.DeleteAsync(entity);
            throw;
        }
    }

    /// <summary>
    /// Update a multi run job.
    /// </summary>
    [HttpPut("multi-run")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<MultiRunJobDto>> UpdateMultiRunJob(
        UpdateMultiRunJobDto dto, [FromServices] IValidator<UpdateMultiRunJobDto> validator)
    {
        await validator.ValidateAndThrowAsync(dto);
        var job = GetJob<MultiRunJob>(dto.Id);

        if (job.Status is not JobStatus.Idle)
        {
            throw new ResourceInUseException(ErrorCode.JobNotIdle, $"Job {dto.Id} is not idle");
        }

        var entity = await GetEntityAsync(dto.Id);
        EnsureOwnership(entity);

        var jobOptions = _mapper.Map<MultiRunJobOptions>(dto);
        var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
        var wrapper = new JobOptionsWrapper { Options = jobOptions };
        entity.JobOptions = JsonConvert.SerializeObject(wrapper, jsonSettings);

        await _jobRepo.UpdateAsync(entity);

        var oldJob = _jobManager.Jobs.First(j => j.Id == dto.Id);
        var newJob = await _jobFactory.FromOptionsAsync(dto.Id, entity.Owner?.Id ?? 0, jobOptions);

        _jobManager.RemoveJob(oldJob);
        _jobManager.AddJob(newJob);
        _logger.LogInformation("Updated the multi run job with id {Id}", dto.Id);

        return await MapMultiRunJobDto((MultiRunJob)newJob);
    }

    /// <summary>
    /// Update a proxy check job.
    /// </summary>
    [HttpPut("proxy-check")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<ProxyCheckJobDto>> UpdateProxyCheckJob(
        UpdateProxyCheckJobDto dto, [FromServices] IValidator<UpdateProxyCheckJobDto> validator)
    {
        await validator.ValidateAndThrowAsync(dto);
        var job = GetJob<ProxyCheckJob>(dto.Id);

        if (job.Status is not JobStatus.Idle)
        {
            throw new ResourceInUseException(ErrorCode.JobNotIdle, $"Job {dto.Id} is not idle");
        }

        var entity = await GetEntityAsync(dto.Id);
        EnsureOwnership(entity);

        var jobOptions = _mapper.Map<ProxyCheckJobOptions>(dto);
        var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
        var wrapper = new JobOptionsWrapper { Options = jobOptions };
        entity.JobOptions = JsonConvert.SerializeObject(wrapper, jsonSettings);

        await _jobRepo.UpdateAsync(entity);

        var oldJob = _jobManager.Jobs.First(j => j.Id == dto.Id);
        var newJob = await _jobFactory.FromOptionsAsync(dto.Id, entity.Owner?.Id ?? 0, jobOptions);

        _jobManager.RemoveJob(oldJob);
        _jobManager.AddJob(newJob);
        _logger.LogInformation("Updated the proxy check job with id {Id}", dto.Id);

        return await MapProxyCheckJobDto((ProxyCheckJob)newJob);
    }

    /// <summary>
    /// Delete a job.
    /// </summary>
    [HttpDelete]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult> Delete(int id)
    {
        var entity = await GetEntityAsync(id);
        var job = GetJob(id);

        EnsureOwnership(entity);
        EnsureOwnership(job);

        await _jobRepo.DeleteAsync(entity);
        _jobManager.RemoveJob(job);
        _logger.LogInformation("Deleted job with id {Id}", id);

        return Ok();
    }

    /// <summary>
    /// Delete all jobs.
    /// </summary>
    [HttpDelete("all")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<AffectedEntriesDto>> DeleteAll()
    {
        var apiUser = HttpContext.GetApiUser();
        int deletedCount;

        var notIdleJobs = _jobManager.Jobs
            .Where(j => CanSee(apiUser, j) && j.Status != JobStatus.Idle);

        if (notIdleJobs.Any())
        {
            throw new ResourceInUseException(ErrorCode.JobNotIdle, "There are non-idle jobs, please stop them first");
        }

        if (apiUser.Role is UserRole.Admin)
        {
            deletedCount = await _jobRepo.GetAll().CountAsync();
            _jobRepo.Purge();
            _jobManager.Clear();
        }
        else
        {
            var entities = await _jobRepo.GetAll()
                .Include(j => j.Owner)
                .Where(j => j.Owner.Id == apiUser.Id)
                .ToListAsync();

            deletedCount = entities.Count;
            await _jobRepo.DeleteAsync(entities);

            foreach (var job in _jobManager.Jobs.Where(j => j.OwnerId == apiUser.Id).ToList())
            {
                _jobManager.RemoveJob(job);
            }
        }
        
        _logger.LogInformation("Deleted {DeletedCount} jobs", deletedCount);
        return new AffectedEntriesDto { Count = deletedCount };
    }

    /// <summary>
    /// Get the record of a config and wordlist combination. If no record
    /// exists, a fake one with checkpoint 0 will be returned.
    /// </summary>
    [HttpGet("multi-run/record")]
    [MapToApiVersion("1.0")]
    public async Task<ActionResult<RecordDto>> GetRecord(string configId, int wordlistId)
    {
        var record = await _recordRepo.GetAll()
            .FirstOrDefaultAsync(r => r.ConfigId == configId && r.WordlistId == wordlistId);

        if (record is null)
        {
            return new RecordDto {
                ConfigId = configId, WordlistId = wordlistId, Checkpoint = 0
            };
        }
        
        return _mapper.Map<RecordDto>(record);
    }

    #endregion

    #region Job Operations

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

    #endregion

    #region Job Monitor Triggered Actions

    /// <summary>
    /// List all available triggered actions.
    /// </summary>
    [HttpGet("/api/v{version:apiVersion}/job-monitor/triggered-action/all")]
    [MapToApiVersion("1.0")]
    [TypeFilter<AdminFilter>]
    public ActionResult<IEnumerable<TriggeredActionDto>> GetAllTriggeredActions()
    {
        var actions = _jobMonitorService.TriggeredActions;
        return Ok(actions.Select(MapTriggeredAction));
    }

    /// <summary>
    /// Get a triggered action by id.
    /// </summary>
    [HttpGet("/api/v{version:apiVersion}/job-monitor/triggered-action")]
    [MapToApiVersion("1.0")]
    [TypeFilter<AdminFilter>]
    public ActionResult<TriggeredActionDto> GetTriggeredAction(string id) =>
        MapTriggeredAction(GetTriggeredActionById(id));

    /// <summary>
    /// Create a new triggered action.
    /// </summary>
    [HttpPost("/api/v{version:apiVersion}/job-monitor/triggered-action")]
    [MapToApiVersion("1.0")]
    [TypeFilter<AdminFilter>]
    public ActionResult<TriggeredActionDto> CreateTriggeredAction(
        CreateTriggeredActionDto dto)
    {
        var actions = _jobMonitorService.TriggeredActions;

        var newAction = _mapper.Map<TriggeredAction>(dto);
        actions.Add(newAction);
        _jobMonitorService.SaveStateIfChanged();

        _logger.LogInformation("Created triggered action {Id}", newAction.Id);

        return MapTriggeredAction(newAction);
    }

    /// <summary>
    /// Update a triggered action.
    /// </summary>
    [HttpPut("/api/v{version:apiVersion}/job-monitor/triggered-action")]
    [MapToApiVersion("1.0")]
    [TypeFilter<AdminFilter>]
    public async Task<ActionResult<TriggeredActionDto>> UpdateTriggeredAction(
        UpdateTriggeredActionDto dto,
        [FromServices] IValidator<UpdateTriggeredActionDto> validator)
    {
        await validator.ValidateAndThrowAsync(dto);
        
        var targetAction = GetTriggeredActionById(dto.Id);

        var newAction = _mapper.Map(dto, targetAction);
        _jobMonitorService.SaveStateIfChanged();

        _logger.LogInformation("Updated triggered action {Id}", newAction.Id);

        return MapTriggeredAction(newAction);
    }

    /// <summary>
    /// Resets a triggered action's execution counter.
    /// </summary>
    [HttpPost("/api/v{version:apiVersion}/job-monitor/triggered-action/reset")]
    [MapToApiVersion("1.0")]
    [TypeFilter<AdminFilter>]
    public ActionResult ResetTriggeredAction(string id)
    {
        var targetAction = GetTriggeredActionById(id);

        targetAction.Reset();
        _jobMonitorService.SaveStateIfChanged();

        _logger.LogInformation("Reset triggered action {Id}", id);

        return Ok();
    }

    /// <summary>
    /// Sets a triggered action as active or inactive.
    /// </summary>
    [HttpPost("/api/v{version:apiVersion}/job-monitor/triggered-action/set-active")]
    [MapToApiVersion("1.0")]
    [TypeFilter<AdminFilter>]
    public ActionResult SetActiveTriggeredAction(string id, bool active)
    {
        var targetAction = GetTriggeredActionById(id);

        targetAction.IsActive = active;
        _jobMonitorService.SaveStateIfChanged();

        _logger.LogInformation("Set triggered action {Id} as {Active}", id, active ? "active" : "inactive");

        return Ok();
    }

    /// <summary>
    /// Delete a triggered action.
    /// </summary>
    [HttpDelete("/api/v{version:apiVersion}/job-monitor/triggered-action")]
    [MapToApiVersion("1.0")]
    [TypeFilter<AdminFilter>]
    public ActionResult DeleteTriggeredAction(string id)
    {
        var targetAction = GetTriggeredActionById(id);

        _jobMonitorService.TriggeredActions.Remove(targetAction);
        _jobMonitorService.SaveStateIfChanged();

        _logger.LogInformation("Deleted triggered action {Id}", id);

        return Ok();
    }

    #endregion

    #region Private Helpers

    private bool CanSee(ApiUser apiUser, Job job)
        => apiUser.Role is UserRole.Admin || job.OwnerId == apiUser.Id;

    private static MultiRunJobOverviewDto MapMultiRunJobOverviewDto(MultiRunJob job)
    {
        var dataPoolInfo = job.DataPool switch {
            WordlistDataPool w => $"{w.Wordlist?.Name} (Wordlist)",
            CombinationsDataPool => "Combinations",
            InfiniteDataPool => "Infinite",
            RangeDataPool => "Range",
            FileDataPool f => $"{f.FileName} (File)",
            _ => throw new NotImplementedException()
        };

        return new MultiRunJobOverviewDto {
            Id = job.Id,
            OwnerId = job.OwnerId,
            Type = JobType.MultiRun,
            Status = job.Status,
            Name = job.Name,
            ConfigName = job.Config?.Metadata.Name,
            UseProxies = RuriLib.Models.Jobs.ProxyManager.ShouldUseProxies(job.ProxyMode, job.Config?.Settings.ProxySettings),
            Bots = job.Bots,
            DataPoolInfo = dataPoolInfo,
            DataHits = job.DataHits,
            DataCustom = job.DataCustom,
            DataToCheck = job.DataToCheck,
            DataTotal = job.DataPool.Size,
            DataTested = job.Status is JobStatus.Idle ? job.Skip : job.DataTested + job.Skip,
            CPM = job.CPM,
            Progress = job.Progress < 0 ? 0 : job.Progress
        };
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

    private async Task<JobEntity> GetEntityAsync(int id)
    {
        var entity = await _jobRepo.GetAsync(id);

        if (entity is null)
        {
            throw new EntryNotFoundException(ErrorCode.JobNotFound, id, nameof(IJobRepository));
        }

        return entity;
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

    private void EnsureOwnership(JobEntity entity)
    {
        var apiUser = HttpContext.GetApiUser();

        if (apiUser.Role is UserRole.Guest && apiUser.Id != entity.Owner?.Id)
        {
            _logger.LogWarning("Guest user {Username} tried to access a job not owned by them", apiUser.Username);
            throw new EntryNotFoundException(ErrorCode.JobNotFound, entity.Id, nameof(IJobRepository));
        }
    }

    private static JobType GetJobType(Job job) =>
        job switch {
            MultiRunJob => JobType.MultiRun,
            ProxyCheckJob => JobType.ProxyCheck,
            _ => throw new NotImplementedException()
        };

    private async Task<ProxyCheckJobDto> MapProxyCheckJobDto(ProxyCheckJob job)
    {
        var checkOutput = job.ProxyOutput switch {
            DatabaseProxyCheckOutput => "database",
            _ => throw new NotImplementedException()
        };

        TimeStartConditionDto startCondition = job.StartCondition switch {
            RelativeTimeStartCondition r => new RelativeTimeStartConditionDto {
                PolyTypeName = PolyDtoCache.GetPolyTypeNameFromType<RelativeTimeStartConditionDto>()!,
                StartAfter = r.StartAfter
            },
            AbsoluteTimeStartCondition a => new AbsoluteTimeStartConditionDto {
                PolyTypeName = PolyDtoCache.GetPolyTypeNameFromType<AbsoluteTimeStartConditionDto>()!,
                StartAt = a.StartAt
            },
            _ => throw new NotImplementedException()
        };

        var entity = await GetEntityAsync(job.Id);
        EnsureOwnership(entity);

        var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
        var jobOptions = JsonConvert.DeserializeObject<JobOptionsWrapper>(entity.JobOptions, jsonSettings)?.Options;

        if (jobOptions is null)
        {
            throw new ApiException(ErrorCode.InvalidJobConfiguration, "The job options are null");
        }

        if (jobOptions is not ProxyCheckJobOptions pcjJobOptions)
        {
            throw new ApiException(ErrorCode.InvalidJobType, "Invalid job options type");
        }

        var groupName = "All";

        if (pcjJobOptions.GroupId != -1)
        {
            var proxyGroup = await _proxyGroupRepo.GetAsync(pcjJobOptions.GroupId);

            if (proxyGroup is not null)
            {
                groupName = proxyGroup.Name;
            }
        }

        return new ProxyCheckJobDto {
            Id = job.Id,
            Name = job.Name,
            StartCondition = startCondition,
            StartTime = job.StartTime,
            OwnerId = job.OwnerId,
            Type = GetJobType(job),
            Status = job.Status,
            Bots = job.Bots,
            GroupId = pcjJobOptions.GroupId,
            GroupName = groupName,
            CheckOnlyUntested = job.CheckOnlyUntested,
            Target = new ProxyCheckTargetDto { Url = job.Url, SuccessKey = job.SuccessKey },
            CheckOutput = checkOutput,
            Tested = job.Tested,
            Working = job.Working,
            NotWorking = job.NotWorking,
            Total = job.Total,
            TimeoutMilliseconds = (int)job.Timeout.TotalMilliseconds,
            CPM = job.CPM,
            Elapsed = job.Elapsed,
            Remaining = job.Remaining,
            Progress = job.Progress < 0 ? 0 : job.Progress
        };
    }

    private async Task<MultiRunJobDto> MapMultiRunJobDto(MultiRunJob job)
    {
        var dataPoolInfo = job.DataPool switch {
            WordlistDataPool w => $"{w.Wordlist?.Name} (Wordlist)",
            CombinationsDataPool c => $"Combinations of {c.CharSet} with length {c.Length}",
            RangeDataPool r => $"Range from {r.Start} with amount {r.Amount} and step {r.Step} (padding {r.Pad})",
            InfiniteDataPool => "Infinite",
            FileDataPool f => $"{f.FileName} (File)",
            _ => throw new NotImplementedException()
        };

        var proxySources = await Task.WhenAll(job.ProxySources.Select(async s => s switch {
            GroupProxySource g => $"{await GetProxyGroupName(g.GroupId)} (Group)",
            FileProxySource f => $"{f.FileName} (File)",
            RemoteProxySource r => $"{r.Url} (Remote)",
            _ => throw new NotImplementedException()
        }));

        var hitOutputs = job.HitOutputs.Select(o => o switch {
            DatabaseHitOutput => "Database",
            FileSystemHitOutput f => $"{f.BaseDir} (File System)",
            DiscordWebhookHitOutput => "Discord Webhook",
            TelegramBotHitOutput => "Telegram bot",
            CustomWebhookHitOutput => "Custom Webhook",
            _ => throw new NotImplementedException()
        }).ToList();

        TimeStartConditionDto startCondition = job.StartCondition switch {
            RelativeTimeStartCondition r => new RelativeTimeStartConditionDto {
                PolyTypeName = PolyDtoCache.GetPolyTypeNameFromType<RelativeTimeStartConditionDto>()!,
                StartAfter = r.StartAfter
            },
            AbsoluteTimeStartCondition a => new AbsoluteTimeStartConditionDto {
                PolyTypeName = PolyDtoCache.GetPolyTypeNameFromType<AbsoluteTimeStartConditionDto>()!,
                StartAt = a.StartAt
            },
            _ => throw new NotImplementedException()
        };

        return new MultiRunJobDto {
            Id = job.Id,
            Name = job.Name,
            StartCondition = startCondition,
            StartTime = job.StartTime,
            OwnerId = job.OwnerId,
            Type = GetJobType(job),
            Status = job.Status,
            Config =
                job.Config is not null
                    ? new JobConfigDto {
                        Id = job.Config.Id,
                        Name = job.Config.Metadata.Name,
                        Author = job.Config.Metadata.Author,
                        Base64Image = job.Config.Metadata.Base64Image,
                        NeedsProxies = job.Config.Settings.ProxySettings.UseProxies
                    }
                    : null,
            DataPoolInfo = dataPoolInfo,
            Bots = job.Bots,
            Skip = job.Skip,
            ProxyMode = job.ProxyMode,
            ProxySources = proxySources.ToList(),
            HitOutputs = hitOutputs,
            DataStats =
                new MrjDataStatsDto {
                    Hits = job.DataHits,
                    Custom = job.DataCustom,
                    Fails = job.DataFails,
                    Invalid = job.DataInvalid,
                    Retried = job.DataRetried,
                    Banned = job.DataBanned,
                    Errors = job.DataErrors,
                    ToCheck = job.DataToCheck,
                    Total = job.DataPool.Size,
                    Tested = job.DataTested
                },
            ProxyStats =
                new MrjProxyStatsDto {
                    Total = job.ProxiesTotal, Alive = job.ProxiesAlive, Bad = job.ProxiesBad, Banned = job.ProxiesBanned
                },
            CPM = job.CPM,
            CaptchaCredit = job.CaptchaCredit,
            Elapsed = job.Elapsed,
            Remaining = job.Remaining,
            Progress = job.Progress < 0 ? 0 : job.Progress,
            Hits = job.Hits.Select(h => new MrjHitDto {
                Id = h.Id,
                Date = h.Date,
                Type = h.Type,
                Data = h.DataString,
                Proxy = h.Proxy is not null 
                    ? new MrjProxy {
                        Type = h.Proxy.Type,
                        Host = h.Proxy.Host,
                        Port = h.Proxy.Port,
                        Username = h.Proxy.Username,
                        Password = h.Proxy.Password,
                    }
                    : null,
                CapturedData = h.CapturedDataString
            }).ToList()
        };
    }

    private async Task<string> GetProxyGroupName(int id)
        => id == -1 ? "All" : (await _proxyGroupRepo.GetAsync(id))?.Name ?? "Invalid";

    private TriggeredAction GetTriggeredActionById(string id)
    {
        var actions = _jobMonitorService.TriggeredActions;
        var targetAction = actions.Find(a => a.Id == id);

        if (targetAction is null)
        {
            throw new EntryNotFoundException(
                ErrorCode.TriggeredActionNotFound,
                id, nameof(IGuestRepository));
        }

        return targetAction;
    }

    private TriggeredActionDto MapTriggeredAction(TriggeredAction action)
    {
        var mapped = _mapper.Map<TriggeredActionDto>(action);

        // Search for a job with the given id and set its name
        var job = _jobManager.Jobs.FirstOrDefault(j => j.Id == mapped.JobId);

        if (job is not null)
        {
            mapped.JobName = job.Name;
            mapped.JobType = GetJobType(job);
        }

        return mapped;
    }

    #endregion
}
