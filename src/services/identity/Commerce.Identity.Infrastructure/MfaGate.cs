using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Commerce.Identity.Infrastructure;

public sealed class MfaCodeUse
{
    public Guid UserId { get; set; }
    public string Digest { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
// Called only after the password check; successful verification never grants a role.
public sealed class MfaGate(IdentityDatabase db, UserManager<Account> users, TimeProvider clock)
{
    public async Task<bool> VerifyAsync(Account account, string? code, string? recoveryCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(recoveryCode)) return false;
        var stamp = account.SecurityStamp;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var email = account.NormalizedEmail ?? string.Empty;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({email}, 0))", ct);
        await db.Entry(account).ReloadAsync(ct);
        if (!account.TwoFactorEnabled || !account.EmailConfirmed || account.SecurityStamp != stamp || await users.IsLockedOutAsync(account)) return false;
        var now = clock.GetUtcNow();
        bool valid;
        if (!string.IsNullOrWhiteSpace(recoveryCode))
            valid = string.IsNullOrWhiteSpace(code) && (await users.RedeemTwoFactorRecoveryCodeAsync(account, recoveryCode.Trim())).Succeeded;
        else
        {
            code = code!.Replace(" ", "").Replace("-", "");
            if (code.Length != 6 || code.Any(x => !char.IsAsciiDigit(x))) return false;
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
            var previous = await db.MfaCodeUses.FindAsync([account.Id, digest], ct);
            valid = (previous is null || previous.ExpiresAtUtc <= now) &&
                await users.VerifyTwoFactorTokenAsync(account, users.Options.Tokens.AuthenticatorTokenProvider, code);
            if (valid)
            {
                // Identity's authenticator validator accepts neighboring 30-second steps.
                // Retain the digest beyond that entire acceptance window.
                await db.MfaCodeUses.Where(x => x.UserId == account.Id && x.ExpiresAtUtc <= now).ExecuteDeleteAsync(ct);
                if (previous is not null) db.Entry(previous).State = EntityState.Detached;
                db.MfaCodeUses.Add(new MfaCodeUse { UserId = account.Id, Digest = digest, ExpiresAtUtc = now.AddMinutes(5) });
            }
        }
        if (!valid) await users.AccessFailedAsync(account);
        else await users.ResetAccessFailedCountAsync(account);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return valid;
    }
}
