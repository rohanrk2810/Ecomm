using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
namespace Commerce.Identity.Infrastructure;

public sealed class OidcSettings
{
    public string Issuer { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string PublicAppOrigin { get; set; } = string.Empty;
    public string SigningCertificatePath { get; set; } = string.Empty;
    public string EncryptionCertificatePath { get; set; } = string.Empty;
    public string CertificatePassword { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int SessionMinutes { get; set; } = 60;
    public int FailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}
public sealed class CustomerSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string SecurityStamp { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}
public sealed class AuthenticationAudit
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? SessionId { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}
public static class DevelopmentOidcCertificates
{
    public static void Create(OidcSettings settings)
    {
        foreach (var (path, purpose) in new[] { (settings.SigningCertificatePath, "signing"), (settings.EncryptionCertificatePath, "encryption") })
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrEmpty(settings.CertificatePassword))
                throw new InvalidOperationException("Inject development certificate paths/password.");
            if (File.Exists(path)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? throw new InvalidOperationException("Invalid certificate directory."));
            using var rsa = RSA.Create(3072);
            var request = new CertificateRequest("CN=commerce.identity.local." + purpose, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, settings.CertificatePassword));
        }
    }
}
public sealed class CustomerSessionValidator(IdentityDatabase db, TimeProvider clock)
{
    public Task<bool> ActiveAsync(string? subject, string? sessionId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(subject, out var userId) || !Guid.TryParse(sessionId, out var id)) return Task.FromResult(false);
        var now = clock.GetUtcNow();
        return (from session in db.CustomerSessions
                join user in db.Users on session.UserId equals user.Id
                where session.Id == id && session.UserId == userId && session.RevokedAtUtc == null &&
                session.ExpiresAtUtc > now && user.EmailConfirmed && session.SecurityStamp == user.SecurityStamp
                select session.Id).AnyAsync(cancellationToken);
    }
    public async Task ValidateCookieAsync(CookieValidatePrincipalContext context)
    {
        var sessionId = context.Principal?.FindFirstValue("sid");
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal is null) return;
        if (context.Principal.Identity is ClaimsIdentity identity && identity.FindFirst("sid") is null && sessionId is not null)
            identity.AddClaim(new Claim("sid", sessionId));
        if (!await ActiveAsync(context.Principal.FindFirstValue(ClaimTypes.NameIdentifier),
            context.Principal.FindFirstValue("sid"), context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    }
}
public sealed class SessionIntrospectionHandler(CustomerSessionValidator sessions)
    : IOpenIddictServerHandler<OpenIddictServerEvents.HandleIntrospectionRequestContext>
{
    public async ValueTask HandleAsync(OpenIddictServerEvents.HandleIntrospectionRequestContext context)
    {
        if (!await sessions.ActiveAsync(context.GenericTokenPrincipal.GetClaim(Claims.Subject),
            context.GenericTokenPrincipal.GetClaim("sid"), context.CancellationToken))
            context.Reject(Errors.InvalidToken, "The session is no longer active.");
    }
}
