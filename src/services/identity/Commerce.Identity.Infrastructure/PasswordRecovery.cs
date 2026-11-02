using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Commerce.Identity.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace Commerce.Identity.Infrastructure;

public sealed class PasswordChallenge
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset ResendAfterUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
}
public sealed class PasswordRecoverySettings
{
    public int LifetimeMinutes { get; set; } = 15;
    public int CooldownSeconds { get; set; } = 60;
}
public sealed record PasswordResetRequest(string? Email);
public sealed record PasswordReset(Guid UserId, string? Token, string? NewPassword);
public sealed class PasswordRecovery(IdentityDatabase db, UserManager<Account> users, IDataProtectionProvider protection,
    IOptions<VerificationSettings> mail, IOptions<PasswordRecoverySettings> settings,
    IOptions<RegistrationSettings> registration, TimeProvider clock)
{
    private Task<int> LockAsync(string email, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock(hashtextextended({email}, 0))", ct);
    public async Task RequestAsync(string? email, CancellationToken ct)
    {
        email = email?.Trim();
        if (string.IsNullOrEmpty(email) || email.Length > 254 || !new EmailAddressAttribute().IsValid(email)) return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(users.NormalizeEmail(email) ?? string.Empty, ct);
        var user = await users.FindByEmailAsync(email);
        if (user is null || !user.EmailConfirmed) return;
        var now = clock.GetUtcNow();
        var challenge = await db.PasswordChallenges.FindAsync([user.Id], ct);
        if (challenge is not null && challenge.ResendAfterUtc > now) return;
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
        if (challenge is null) { challenge = new PasswordChallenge { UserId = user.Id }; db.PasswordChallenges.Add(challenge); }
        challenge.TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        challenge.ExpiresAtUtc = now.AddMinutes(settings.Value.LifetimeMinutes);
        challenge.ResendAfterUtc = now.AddSeconds(settings.Value.CooldownSeconds);
        challenge.ConsumedAtUtc = null;
        await db.VerificationEmails.Where(x => x.UserId == user.Id && x.Kind == "password-reset" && x.DeliveredAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.CancelledAtUtc, now), ct);
        var link = mail.Value.StorefrontBaseUri.TrimEnd('/') + "/reset-password#user=" + user.Id + "&token=" + token;
        db.VerificationEmails.Add(new VerificationEmail
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Kind = "password-reset",
            ExpiresAtUtc = challenge.ExpiresAtUtc,
            NextAttemptAtUtc = now,
            ProtectedPayload = protection.CreateProtector(VerificationIssuer.MailPurpose).Protect(JsonSerializer.Serialize(new VerificationEmailContent(user.Email!, link)))
        });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    public async Task<bool> ResetAsync(PasswordReset request, string correlationId, CancellationToken ct)
    {
        if (request.UserId == Guid.Empty || string.IsNullOrEmpty(request.Token) || request.Token.Length > 4096 ||
            request.Token.Any(x => !char.IsAsciiLetterOrDigit(x) && x != '-' && x != '_') || string.IsNullOrEmpty(request.NewPassword) ||
            request.NewPassword.Length > registration.Value.MaximumPasswordLength) return false;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await users.FindByIdAsync(request.UserId.ToString());
        if (user is null) return false;
        await LockAsync(user.NormalizedEmail ?? string.Empty, ct);
        await db.Entry(user).ReloadAsync(ct);
        var challenge = await db.PasswordChallenges.FindAsync([user.Id], ct);
        var now = clock.GetUtcNow();
        if (!user.EmailConfirmed || challenge is null || challenge.ConsumedAtUtc is not null || challenge.ExpiresAtUtc <= now ||
            !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(challenge.TokenHash), SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)))) return false;
        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token)); }
        catch (FormatException) { return false; }
        if (!(await users.ResetPasswordAsync(user, token, request.NewPassword)).Succeeded) return false;
        challenge.ConsumedAtUtc = now;
        await db.CustomerSessions.Where(x => x.UserId == user.Id && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.RevokedAtUtc, now), ct);
        await db.VerificationEmails.Where(x => x.UserId == user.Id && x.Kind == "password-reset" && x.DeliveredAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.CancelledAtUtc, now), ct);
        db.AuthenticationAudits.Add(new AuthenticationAudit
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Action = "PasswordReset",
            OccurredAtUtc = now,
            CorrelationId = correlationId
        });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return true;
    }
}
