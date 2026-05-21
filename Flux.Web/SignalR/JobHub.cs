using Microsoft.AspNetCore.SignalR;
using Flux.Core.Services;
using Flux.Web.Dtos.Common;
using Flux.Web.Dtos.Job;
using Flux.Web.Exceptions;
using Flux.Web.Interfaces;

namespace Flux.Web.SignalR;

/// <summary>
/// SignalR hub for a generic job.
/// </summary>
public abstract class JobHub : AuthorizedHub
{
    private readonly IJobService _jobService;

    /// <summary></summary>
    protected JobHub(IAuthTokenService tokenService,
        ILogger logger, IJobService jobService,
        FluxSettingsService fluxSettingsService)
        : base(tokenService, fluxSettingsService, false)
    {
        _jobService = jobService;
    }

    /// <inheritdoc />
    public async override Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        var jobId = await GetJobIdOrThrowAsync();

        try
        {
            _jobService.RegisterConnection(Context.ConnectionId, jobId, AuthenticatedUser!);
        }
        catch (ApiException ex)
        {
            await SendApiErrorAsync(ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async override Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);

        var jobId = TryGetJobId();
        if (jobId is not null)
        {
            _jobService.UnregisterConnection(Context.ConnectionId, jobId.Value);
        }
    }

    /// <summary>
    /// Start a job.
    /// </summary>
    [HubMethodName("start")]
    public void Start() => _jobService.Start(GetJobIdOrThrow());

    /// <summary>
    /// Stop a job.
    /// </summary>
    [HubMethodName("stop")]
    public void Stop() => _jobService.Stop(GetJobIdOrThrow());

    /// <summary>
    /// Abort a job.
    /// </summary>
    [HubMethodName("abort")]
    public void Abort() => _jobService.Abort(GetJobIdOrThrow());

    /// <summary>
    /// Pause a job.
    /// </summary>
    [HubMethodName("pause")]
    public void Pause() => _jobService.Pause(GetJobIdOrThrow());

    /// <summary>
    /// Resume a job.
    /// </summary>
    [HubMethodName("resume")]
    public void Resume() => _jobService.Resume(GetJobIdOrThrow());

    /// <summary>
    /// Skip the wait for a job.
    /// </summary>
    [HubMethodName("skipWait")]
    public void SkipWait() => _jobService.SkipWait(GetJobIdOrThrow());

    /// <summary>
    /// Change the number of bots.
    /// </summary>
    [HubMethodName("changeBots")]
    public void ChangeBots(ChangeBotsMessage message) =>
        _jobService.ChangeBots(GetJobIdOrThrow(), message);

    /// <summary>
    /// Gets the job id provided by the user at connection setup.
    /// </summary>
    private async Task<int> GetJobIdOrThrowAsync()
    {
        try
        {
            return GetJobIdOrThrow();
        }
        catch (ApiException ex)
        {
            await SendApiErrorAsync(ex);
            throw;
        }
    }

    private Task SendApiErrorAsync(ApiException ex)
        => Clients.Caller.SendAsync(
            CommonMethods.Error,
            new ErrorMessage { Message = ex.Message, Type = ex.GetType().Name });

    private int GetJobIdOrThrow()
        => TryGetJobId() ?? throw new BadRequestException(
            ErrorCode.MissingJobId,
            "Please specify a valid job id");

    private int? TryGetJobId()
    {
        var request = Context.GetHttpContext()?.Request;
        var id = request?.Query["jobId"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return int.TryParse(id, out var jobId) ? jobId : null;
    }
}
