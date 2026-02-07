using Microsoft.AspNetCore.Mvc.Filters;
using Flux.Web.Exceptions;
using Flux.Web.Extensions;
using Flux.Web.Models.Identity;

namespace Flux.Web.Auth;

internal class AdminFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.GetApiUser().Role is not UserRole.Admin)
        {
            throw new ForbiddenException(
                ErrorCode.NotAdmin, "You must be an admin user to perform this operation");
        }
    }
}
