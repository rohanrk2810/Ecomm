using Commerce.Identity.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Abstractions;
namespace Commerce.Identity.Api;

public sealed class CurrentStaffRequirement : IAuthorizationRequirement;
public sealed class CurrentStaffHandler(StaffAccessReader access) : AuthorizationHandler<CurrentStaffRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CurrentStaffRequirement requirement)
    {
        if (context.Resource is HttpContext http && context.User.Identity?.IsAuthenticated == true &&
            context.User.HasClaim("amr", "mfa") && await access.AllowedAsync(context.User.GetClaim("sub"),
                context.User.GetClaim("sid"), http.RequestAborted)) context.Succeed(requirement);
    }
}

public sealed class ApiAuthorizationResults : Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler
{
    private readonly Microsoft.AspNetCore.Authorization.Policy.AuthorizationMiddlewareResultHandler fallback = new();
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        Microsoft.AspNetCore.Authorization.Policy.PolicyAuthorizationResult result)
    {
        if (context.Request.Path.StartsWithSegments("/api") && (result.Challenged || result.Forbidden))
        {
            if (result.Challenged) context.Response.Headers.WWWAuthenticate = "Bearer";
            await Results.Problem(statusCode: result.Challenged ? 401 : 403,
                title: result.Challenged ? "Authentication is required." : "Access is not available for this session.").ExecuteAsync(context);
            return;
        }
        await fallback.HandleAsync(next, context, policy, result);
    }
}
