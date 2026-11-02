using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Commerce.Identity.Application;
using Commerce.Identity.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
namespace Commerce.Identity.Infrastructure;

public sealed class EmailVerificationStore(IdentityDatabase db, UserManager<Account> users,
    VerificationIssuer issuer, TimeProvider clock) : IEmailVerificationStore
{
    public async Task ResendAsync(string email, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(users.NormalizeEmail(email) ?? string.Empty, cancellationToken);
        var account = await users.FindByEmailAsync(email);
        if (account is not null && !account.EmailConfirmed)
        {
            var challenge = await db.EmailChallenges.FindAsync([account.Id], cancellationToken);
            if (challenge is null || challenge.ResendAfterUtc <= clock.GetUtcNow())
            {
                await issuer.IssueAsync(account, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }
    public async Task<bool> ConfirmAsync(Guid userId, string token, string correlationId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var account = await users.FindByIdAsync(userId.ToString());
        if (account is null) return false;
        await LockAsync(account.NormalizedEmail ?? string.Empty, cancellationToken);
        await db.Entry(account).ReloadAsync(cancellationToken);
        if (account.EmailConfirmed) return false;
        var challenge = await db.EmailChallenges.FindAsync([userId], cancellationToken);
        if (challenge is null || challenge.ConsumedAtUtc is not null || challenge.ExpiresAtUtc <= clock.GetUtcNow() ||
            !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(challenge.TokenHash), SHA256.HashData(Encoding.UTF8.GetBytes(token)))) return false;
        var decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        if (!(await users.ConfirmEmailAsync(account, decodedToken)).Succeeded) return false;
        var now = clock.GetUtcNow();
        challenge.ConsumedAtUtc = now;
        var eventId = Guid.NewGuid();
        db.Outbox.Add(new OutboxMessage
        {
            Id = eventId,
            CreatedAtUtc = now,
            Payload = JsonSerializer.Serialize(new EventEnvelope<UserEmailVerified>(eventId, "UserEmailVerified", 1, now,
                "commerce.identity", userId.ToString(), 2, correlationId, correlationId, Activity.Current?.Id,
                new UserEmailVerified(userId)), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        await db.VerificationEmails.Where(x => x.UserId == userId && x.Kind == "verification" && x.DeliveredAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.CancelledAtUtc, now), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
    private Task<int> LockAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({normalizedEmail}, 0))", cancellationToken);
}
