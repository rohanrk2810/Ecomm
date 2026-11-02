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

public sealed class VerificationIssuer(IdentityDatabase db, UserManager<Account> users,
    IDataProtectionProvider protection, IOptions<VerificationSettings> settings, TimeProvider clock)
{
    public const string MailPurpose = "Commerce.Identity.VerificationEmail.v1";
    public async Task IssueAsync(Account account, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync(account)));
        var challenge = await db.EmailChallenges.FindAsync([account.Id], cancellationToken);
        if (challenge is null) { challenge = new EmailChallenge { UserId = account.Id }; db.EmailChallenges.Add(challenge); }
        challenge.TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        challenge.ExpiresAtUtc = now.AddMinutes(settings.Value.LifetimeMinutes);
        challenge.ResendAfterUtc = now.AddSeconds(settings.Value.ResendCooldownSeconds);
        challenge.ConsumedAtUtc = null;
        await db.VerificationEmails.Where(x => x.UserId == account.Id && x.Kind == "verification" && x.DeliveredAtUtc == null && x.CancelledAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.CancelledAtUtc, now), cancellationToken);
        var link = settings.Value.StorefrontBaseUri.TrimEnd('/') + "/verify-email#user=" + account.Id + "&token=" + token;
        var payload = JsonSerializer.Serialize(new VerificationEmailContent(account.Email
            ?? throw new InvalidOperationException("Verified delivery address is missing."), link));
        db.VerificationEmails.Add(new VerificationEmail
        {
            Id = Guid.NewGuid(),
            UserId = account.Id,
            NextAttemptAtUtc = now,
            ExpiresAtUtc = challenge.ExpiresAtUtc,
            ProtectedPayload = protection.CreateProtector(MailPurpose).Protect(payload)
        });
    }
}
