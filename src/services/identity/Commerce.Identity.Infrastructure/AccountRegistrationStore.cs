using System.Diagnostics;
using System.Text.Json;
using Commerce.Identity.Application;
using Commerce.Identity.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Identity.Infrastructure;

public sealed class AccountRegistrationStore(IdentityDatabase database, UserManager<Account> users,
    VerificationIssuer verification, TimeProvider clock) : IAccountRegistrationStore
{
    public async Task RegisterAsync(string email, string password, string correlationId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var normalizedEmail = users.NormalizeEmail(email);
        // Serialize attempts for one normalized address in this service's database.
        // Unique indexes remain authoritative. No Redis lock or cross-service access.
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({normalizedEmail}, 0))", cancellationToken);
        if (await users.FindByEmailAsync(email) is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        var accountId = Guid.NewGuid();
        var account = new Account { Id = accountId, UserName = accountId.ToString("N"), Email = email };
        var result = await users.CreateAsync(account, password);
        if (!result.Succeeded) throw new InvalidOperationException("Account registration could not be persisted.");
        await verification.IssueAsync(account, cancellationToken);
        var eventId = Guid.NewGuid();
        var now = clock.GetUtcNow();
        database.Outbox.Add(new OutboxMessage
        {
            Id = eventId,
            CreatedAtUtc = now,
            Payload = JsonSerializer.Serialize(new EventEnvelope<UserRegistered>(eventId, "UserRegistered", 1, now,
                "commerce.identity", account.Id.ToString(), 1, correlationId, correlationId,
                Activity.Current?.Id, new UserRegistered(account.Id)), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
