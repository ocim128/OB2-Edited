using Flux.Web.Exceptions;
using Flux.Web.Models.Identity;

namespace Flux.Web.Extensions;

static internal class HttpContextExtensions
{
    public static ApiUser GetApiUser(this HttpContext context)
        => (ApiUser)(context.Items["apiUser"] ??
                     throw new UnauthorizedException(ErrorCode.NotAuthenticated,
                         "Not authenticated"));

    public static void SetApiUser(this HttpContext context, ApiUser value)
        => context.Items["apiUser"] = value;
}
