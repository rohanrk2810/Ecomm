using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Commerce.Identity.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using static OpenIddict.Abstractions.OpenIddictConstants;
using IdentityAccount = Commerce.Identity.Infrastructure.Account;
namespace Commerce.Identity.Api.Pages.Account;

[EnableRateLimiting("registration")]
public sealed class LoginModel(UserManager<IdentityAccount> users, SignInManager<IdentityAccount> signIn,
    MfaGate mfa, IdentityDatabase db, IOptions<OidcSettings> settings, TimeProvider clock) : PageModel
{
    [BindProperty, Required, EmailAddress, StringLength(254)] public string Email { get; set; } = string.Empty;
    [BindProperty, Required, StringLength(128), DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string ReturnUrl { get; set; } = string.Empty;
    [BindProperty, StringLength(32)] public string? AuthenticatorCode { get; set; }
    [BindProperty, StringLength(128)] public string? RecoveryCode { get; set; }
    public string? Error { get; private set; }
    public IActionResult OnGet() => ValidReturnUrl() ? Page() : BadRequest();
    private bool ValidReturnUrl() => Url.IsLocalUrl(ReturnUrl) && ReturnUrl.Split('?')[0] == "/connect/authorize";
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ValidReturnUrl()) return BadRequest();
        IdentityAccount? account = null;
        if (ModelState.IsValid) account = await users.FindByEmailAsync(Email.Trim());
        var success = account is not null && (await signIn.CheckPasswordSignInAsync(account, Password, true)).Succeeded;
        if (success && account!.TwoFactorEnabled) success = await mfa.VerifyAsync(account, AuthenticatorCode, RecoveryCode, cancellationToken);
        Password = string.Empty; AuthenticatorCode = null; RecoveryCode = null;
        ModelState.Remove(nameof(Password)); ModelState.Remove(nameof(AuthenticatorCode)); ModelState.Remove(nameof(RecoveryCode));
        if (!success || account is null)
        {
            db.AuthenticationAudits.Add(new AuthenticationAudit
            {
                Id = Guid.NewGuid(),
                UserId = account?.Id,
                Action = "SignInRejected",
                OccurredAtUtc = clock.GetUtcNow(),
                CorrelationId = HttpContext.TraceIdentifier
            });
            await db.SaveChangesAsync(cancellationToken);
            Error = "Unable to sign in. Check your details, email verification and account availability.";
            Response.StatusCode = 400; return Page();
        }
        var now = clock.GetUtcNow();
        var session = new CustomerSession
        {
            Id = Guid.NewGuid(),
            UserId = account.Id,
            SecurityStamp = account.SecurityStamp ?? string.Empty,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(settings.Value.SessionMinutes)
        };
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            db.CustomerSessions.Add(session);
            db.AuthenticationAudits.Add(new AuthenticationAudit
            {
                Id = Guid.NewGuid(),
                UserId = account.Id,
                SessionId = session.Id,
                Action = "SessionSignedIn",
                OccurredAtUtc = now,
                CorrelationId = HttpContext.TraceIdentifier
            });
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        }
        var principal = await signIn.CreateUserPrincipalAsync(account);
        if (principal.Identity is not ClaimsIdentity identity) throw new InvalidOperationException("Account principal is missing.");
        identity.AddClaim(new Claim("sid", session.Id.ToString()));
        await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal,
            new AuthenticationProperties { IsPersistent = false, IssuedUtc = now, ExpiresUtc = session.ExpiresAtUtc });
        // A fresh password check satisfied prompt=login. Preserve nonce/state/PKCE parameters.
        var query = QueryHelpers.ParseQuery(ReturnUrl.Contains('?') ? ReturnUrl[(ReturnUrl.IndexOf('?') + 1)..] : string.Empty);
        query.Remove("prompt");
        return LocalRedirect(QueryHelpers.AddQueryString("/connect/authorize", query));
    }
}
