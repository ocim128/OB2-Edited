using Microsoft.AspNetCore.SignalR;
using Flux.Core.Services;
using Flux.Web.Dtos.Common;
using Flux.Web.Exceptions;
using Flux.Web.Interfaces;
using Flux.Web.Models.Identity;

namespace Flux.Web.SignalR;

/// <summary>
/// Hub that checks the authorization.
/// </summary>
public abstract class AuthorizedHub : Hub
{
    private readonly FluxSettingsService _fluxSettingsService;
    private readonly IAuthTokenService _tokenService;

    /// <summary></summary>
    protected AuthorizedHub(IAuthTokenService tokenService,
        FluxSettingsService fluxSettingsService, bool onlyAdmin)
    {
        _tokenService = tokenService;
        _fluxSettingsService = fluxSettingsService;
        OnlyAdmin = onlyAdmin;
    }

    /// <summary>
    /// The verified user.
    /// </summary>
    protected ApiUser? AuthenticatedUser { get; private set; }

    /// <summary>
    /// Whether this hub should only be used by the admin user.
    /// </summary>
    private bool OnlyAdmin { get; }

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        // If the admin user does not need any login, allow anonymous requests
        if (!_fluxSettingsService.Settings.SecuritySettings.RequireAdminLogin)
        {
            AuthenticatedUser = new ApiUser {
                Id = -1, Role = UserRole.Admin, Username = _fluxSettingsService.Settings.SecuritySettings.AdminUsername
            };

            return;
        }

        // Make sure the user provided a valid auth token
        var request = Context.GetHttpContext()!.Request;
        var accessToken = request.Query["access_token"].FirstOrDefault();

        if (accessToken is null)
        {
            await Clients.Caller.SendAsync(
                CommonMethods.Error,
                new ErrorMessage { Message = "Missing auth token", Type = nameof(UnauthorizedException) });

            throw new UnauthorizedException(
                ErrorCode.MissingAuthToken, "Missing auth token");
        }

        try
        {
            var validToken = _tokenService.ValidateToken(accessToken);
            AuthenticatedUser = ApiUser.FromToken(validToken);
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync(
                CommonMethods.Error,
                new ErrorMessage { Message = ex.Message, Type = ex.GetType().Name });

            throw;
        }

        if (OnlyAdmin && AuthenticatedUser?.Role is not UserRole.Admin)
        {
            await Clients.Caller.SendAsync(
                CommonMethods.Error,
                new ErrorMessage {
                    Message = "You must be an admin to use this hub", Type = nameof(UnauthorizedException)
                });

            throw new UnauthorizedException(ErrorCode.NotAdmin,
                "You must be an admin to use this hub");
        }
    }
}
