using System.Security.Claims;
using Commerce.Identity.Infrastructure;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
namespace Commerce.Identity.Api.Controllers;

public sealed class AuthorizationController(CustomerSessionValidator sessions,
    IdentityDatabase db, TimeProvider clock) : Controller
{
    [HttpGet("/connect/authorize")]
    public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("OIDC request missing.");
        var authentication = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!authentication.Succeeded || request.HasPromptValue(PromptValues.Login))
        {
            if (request.HasPromptValue(PromptValues.None)) return Forbid(new AuthenticationProperties(new Dictionary<string, string?>
            { [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            return Challenge(new AuthenticationProperties { RedirectUri = Request.PathBase + Request.Path + Request.QueryString }, IdentityConstants.ApplicationScheme);
        }
        var userId = authentication.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var sid = authentication.Principal?.FindFirstValue("sid");
        if (!await sessions.ActiveAsync(userId, sid, cancellationToken))
            return Challenge(new AuthenticationProperties { RedirectUri = Request.Path + Request.QueryString }, IdentityConstants.ApplicationScheme);
        var identity = new ClaimsIdentity("OIDC", Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, userId); identity.SetClaim("sid", sid);
        var session = await db.CustomerSessions.AsNoTracking().SingleAsync(x => x.Id == Guid.Parse(sid!), cancellationToken);
        identity.SetClaims("amr", session.MfaVerifiedAtUtc is not null ? ["pwd", "mfa"] : ["pwd"]);
        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());
        principal.SetResources("commerce");
        principal.SetDestinations(_ => [Destinations.AccessToken, Destinations.IdentityToken]);
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
    [HttpPost("/connect/token")]
    public async Task<IActionResult> Exchange(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("OIDC request missing.");
        if (!request.IsAuthorizationCodeGrantType()) return BadRequest();
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (result.Principal is null || !await sessions.ActiveAsync(result.Principal.GetClaim(Claims.Subject),
            result.Principal.GetClaim("sid"), cancellationToken) ||
            (result.Principal.GetClaims("amr").Contains("mfa") && !await sessions.MfaAsync(
                result.Principal.GetClaim(Claims.Subject), result.Principal.GetClaim("sid"), cancellationToken)))
            return Forbid(new AuthenticationProperties(new Dictionary<string, string?>
            { [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        return SignIn(result.Principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
    [HttpGet("/connect/logout")]
    public IActionResult Logout() => View("Logout", HttpContext.GetOpenIddictServerRequest());
    [HttpPost("/connect/logout"), ValidateAntiForgeryToken]
    public async Task<IActionResult> LogoutPost(CancellationToken cancellationToken)
    {
        var authentication = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (Guid.TryParse(authentication.Principal?.FindFirstValue("sid"), out var id))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var session = await db.CustomerSessions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (session is not null && session.RevokedAtUtc is null)
            {
                session.RevokedAtUtc = clock.GetUtcNow();
                db.AuthenticationAudits.Add(new AuthenticationAudit
                {
                    Id = Guid.NewGuid(),
                    UserId = session.UserId,
                    SessionId = id,
                    Action = "SessionSignedOut",
                    OccurredAtUtc = clock.GetUtcNow(),
                    CorrelationId = HttpContext.TraceIdentifier
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return SignOut(new AuthenticationProperties { RedirectUri = "/" }, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
