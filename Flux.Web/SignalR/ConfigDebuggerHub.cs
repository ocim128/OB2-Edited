using AutoMapper;
using Microsoft.AspNetCore.SignalR;
using Flux.Core.Services;
using Flux.Web.Dtos.Common;
using Flux.Web.Dtos.ConfigDebugger;
using Flux.Web.Exceptions;
using Flux.Web.Interfaces;
using Flux.Web.Services;
using RuriLib.Models.Debugger;

namespace Flux.Web.SignalR;

// NOTE: Hubs are short lived and are disposed after every invocation.

/// <summary>
/// SignalR hub for the config debugger.
/// </summary>
public class ConfigDebuggerHub : AuthorizedHub
{
    private readonly ConfigDebuggerService _debuggerService;
    private readonly IMapper _mapper;

    /// <summary></summary>
    public ConfigDebuggerHub(ConfigDebuggerService debuggerService,
        IAuthTokenService tokenService, IMapper mapper,
        FluxSettingsService fluxSettingsService)
        : base(tokenService, fluxSettingsService, true)
    {
        _debuggerService = debuggerService;
        _mapper = mapper;
    }

    /// <inheritdoc />
    public async override Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        var configId = GetConfigId();

        if (configId is null)
        {
            await Clients.Caller.SendAsync(
                CommonMethods.Error,
                new ErrorMessage("Please specify a config id"));

            throw new ApiException(ErrorCode.MissingConfigId, "Please specify a config id");
        }

        _debuggerService.RegisterConnection(Context.ConnectionId, configId);
    }

    /// <inheritdoc />
    public async override Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);

        var configId = GetConfigId();

        _debuggerService.UnregisterConnection(Context.ConnectionId, configId!);
    }

    /// <summary>
    /// Start the debugger with the given options.
    /// </summary>
    [HubMethodName("start")]
    public void Start(DbgStartRequestDto dto) => _debuggerService.StartNew(
        GetConfigId()!,
        _mapper.Map<DebuggerOptions>(dto)
    );

    /// <summary>
    /// Stop the debugger.
    /// </summary>
    [HubMethodName("stop")]
    public async Task StopAsync()
    {
        var debugger = _debuggerService.TryGet(GetConfigId()!)!;

        if (debugger.Status is ConfigDebuggerStatus.Idle)
        {
            await Clients.Caller.SendAsync(
                CommonMethods.Error,
                new ErrorMessage { Type = "Invalid operation", Message = "The debugger is not running" });

            return;
        }

        debugger.Stop();
    }

    /// <summary>
    /// Take a step in step-by-step mode.
    /// </summary>
    [HubMethodName("takeStep")]
    public async Task TakeStepAsync()
    {
        var debugger = _debuggerService.TryGet(GetConfigId()!)!;

        if (!debugger.Options.StepByStep)
        {
            await Clients.Caller.SendAsync(
                CommonMethods.Error,
                new ErrorMessage { Type = "Invalid operation", Message = "The debugger is not in step by step mode" });

            return;
        }

        if (debugger.Status is not ConfigDebuggerStatus.WaitingForStep)
        {
            await Clients.Caller.SendAsync(
                CommonMethods.Error,
                new ErrorMessage { Type = "Invalid operation", Message = "The debugger is not waiting for a step" });

            return;
        }

        debugger.TryTakeStep();
    }

    /// <summary>
    /// Get the state of the debugger.
    /// </summary>
    [HubMethodName("getState")]
    public async Task GetStateAsync()
    {
        DbgStateDto? state = null;

        // Try to get an existing debugger
        var debugger = _debuggerService.TryGet(GetConfigId()!);

        // If there is a debugger
        if (debugger is not null)
        {
            state = new DbgStateDto {
                Log = debugger.Logger.Entries,
                Status = debugger.Status,
                Variables = debugger.Variables.Select(
                    ConfigDebuggerService.MapVariable)
            };
        }

        await Clients.Caller.SendAsync(
            ConfigDebuggerMethods.DebuggerState, state);
    }

    private string? GetConfigId()
    {
        var request = Context.GetHttpContext()!.Request;
        return request.Query["configId"].FirstOrDefault();
    }
}
