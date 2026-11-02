using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Commerce.Identity.Infrastructure;

public sealed class MfaEnrollmentChallenge
{
    public Guid UserId { get; set; }
    public string SecurityStamp { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
public sealed record MfaEnrollmentResult(string? Key = null, string[]? RecoveryCodes = null);
public sealed class MfaEnrollment(IdentityDatabase db, UserManager<Account> users, TimeProvider clock)
{
    public async Task<MfaEnrollmentResult?> ExecuteAsync(string email, string password, string? code, bool confirm, string correlation, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var normalized = users.NormalizeEmail(email.Trim()) ?? string.Empty;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({normalized}, 0))", ct);
        var account = await users.FindByEmailAsync(email.Trim());
        if (account is null || account.TwoFactorEnabled || !account.EmailConfirmed || await users.IsLockedOutAsync(account)) return null;
        if (!await users.CheckPasswordAsync(account, password))
        { await users.AccessFailedAsync(account); await transaction.CommitAsync(ct); return null; }
        var now = clock.GetUtcNow();
        var pending = await db.MfaEnrollmentChallenges.FindAsync([account.Id], ct);
        if (!confirm)
        {
            if (pending is null || pending.ExpiresAtUtc <= now || pending.SecurityStamp != account.SecurityStamp)
            {
                if (!(await users.ResetAuthenticatorKeyAsync(account)).Succeeded) return null;
                if (pending is null) { pending = new MfaEnrollmentChallenge { UserId = account.Id }; db.MfaEnrollmentChallenges.Add(pending); }
                pending.ExpiresAtUtc = now.AddMinutes(10); pending.SecurityStamp = account.SecurityStamp!;
            }
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return new MfaEnrollmentResult(await users.GetAuthenticatorKeyAsync(account));
        }
        code = code?.Replace(" ", "").Replace("-", "");
        if (pending is null || pending.ExpiresAtUtc <= now || pending.SecurityStamp != account.SecurityStamp) return null;
        if (code is null || code.Length != 6 || code.Any(x => !char.IsAsciiDigit(x)) ||
            !await users.VerifyTwoFactorTokenAsync(account, users.Options.Tokens.AuthenticatorTokenProvider, code))
        {
            await users.AccessFailedAsync(account); await transaction.CommitAsync(ct); return null;
        }
        if (!(await users.SetTwoFactorEnabledAsync(account, true)).Succeeded || !(await users.UpdateSecurityStampAsync(account)).Succeeded)
            throw new InvalidOperationException("MFA enrollment could not be persisted.");
        await users.ResetAccessFailedCountAsync(account);
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(account, 10))?.ToArray()
            ?? throw new InvalidOperationException("Recovery codes could not be created.");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
        var used = await db.MfaCodeUses.FindAsync([account.Id, digest], ct);
        if (used is null) db.MfaCodeUses.Add(new MfaCodeUse { UserId = account.Id, Digest = digest, ExpiresAtUtc = now.AddMinutes(5) });
        else used.ExpiresAtUtc = now.AddMinutes(5);
        await db.CustomerSessions.Where(x => x.UserId == account.Id && x.RevokedAtUtc == null).ExecuteUpdateAsync(x => x.SetProperty(p => p.RevokedAtUtc, now), ct);
        db.MfaEnrollmentChallenges.Remove(pending);
        db.AuthenticationAudits.Add(new AuthenticationAudit { Id = Guid.NewGuid(), UserId = account.Id, Action = "MfaEnrolled", OccurredAtUtc = now, CorrelationId = correlation });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new MfaEnrollmentResult(RecoveryCodes: codes);
    }
}
