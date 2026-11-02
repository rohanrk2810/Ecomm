using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Commerce.Identity.Contracts;
using Commerce.Identity.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Commerce.Identity.IntegrationTests;

public sealed class VerificationTests(IdentityFixture factory) : IClassFixture<IdentityFixture>
{
    private static readonly CancellationToken Token = CancellationToken.None;
    private const string ConfirmPath = "/api/v1/identity/email-verifications";
    private const string ResendPath = "/api/v1/identity/email-verification-requests";

    private async Task<(Guid UserId, string Token, string Email)> RegisterAsync()
    {
        var email = "verification-" + Guid.NewGuid().ToString("N") + "@example.invalid";
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/identity/registrations",
            new RegistrationRequest(email, "Synthetic!" + Guid.NewGuid().ToString("N") + "8"), Token);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
        var account = await db.Users.SingleAsync(x => x.NormalizedEmail == email.ToUpperInvariant(), Token);
        var token = await ReadTokenAsync(account.Id);
        return (account.Id, token, email);
    }
    private async Task<string> ReadTokenAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
        var row = await db.VerificationEmails.SingleAsync(x => x.UserId == userId && x.CancelledAtUtc == null, Token);
        var payload = JsonSerializer.Deserialize<VerificationEmailContent>(scope.ServiceProvider
            .GetRequiredService<IDataProtectionProvider>().CreateProtector(VerificationIssuer.MailPurpose).Unprotect(row.ProtectedPayload));
        Assert.NotNull(payload);
        Assert.DoesNotContain(payload.Recipient, row.ProtectedPayload);
        var uri = new Uri(payload.Link);
        Assert.Empty(uri.Query);
        return QueryHelpers.ParseQuery(uri.Fragment.TrimStart('#'))["token"].ToString();
    }

    [Fact]
    public async Task ConfirmationIsSingleUseAndEmitsOnePiiFreeEvent()
    {
        var account = await RegisterAsync();
        using var client = factory.CreateClient();
        var request = new ConfirmEmailRequest(account.UserId, account.Token);
        using var first = await client.PostAsJsonAsync(ConfirmPath, request, Token);
        using var second = await client.PostAsJsonAsync(ConfirmPath, request, Token);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
        Assert.True((await db.Users.SingleAsync(x => x.Id == account.UserId, Token)).EmailConfirmed);
        Assert.NotNull((await db.EmailChallenges.SingleAsync(x => x.UserId == account.UserId, Token)).ConsumedAtUtc);
        var events = await db.Outbox.Select(x => x.Payload).ToListAsync(Token);
        var payload = Assert.Single(events, x => x.Contains(account.UserId.ToString(), StringComparison.Ordinal) && x.Contains("UserEmailVerified", StringComparison.Ordinal));
        Assert.DoesNotContain(account.Email, payload);
        Assert.DoesNotContain(account.Token, payload);
    }

    [Fact]
    public async Task ExpiredChallengeCannotConfirmEmail()
    {
        var account = await RegisterAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
            (await db.EmailChallenges.SingleAsync(x => x.UserId == account.UserId, Token)).ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync(Token);
        }
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(ConfirmPath, new ConfirmEmailRequest(account.UserId, account.Token), Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TokenCannotConfirmAnotherAccount()
    {
        var first = await RegisterAsync();
        var second = await RegisterAsync();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(ConfirmPath, new ConfirmEmailRequest(second.UserId, first.Token), Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
        Assert.False((await db.Users.SingleAsync(x => x.Id == second.UserId, Token)).EmailConfirmed);
    }

    [Fact]
    public async Task ConcurrentConfirmationConsumesExactlyOneChallenge()
    {
        var account = await RegisterAsync();
        using var client = factory.CreateClient();
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync(
            ConfirmPath, new ConfirmEmailRequest(account.UserId, account.Token), Token)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, responses.Count(x => x.StatusCode == HttpStatusCode.BadRequest));
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task ResendEnforcesCooldownAndInvalidatesOldLink()
    {
        var account = await RegisterAsync();
        using var client = factory.CreateClient();
        using var cooldown = await client.PostAsJsonAsync(ResendPath, new ResendEmailRequest(account.Email), Token);
        Assert.Equal(HttpStatusCode.Accepted, cooldown.StatusCode);
        Assert.Equal(account.Token, await ReadTokenAsync(account.UserId));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
            (await db.EmailChallenges.SingleAsync(x => x.UserId == account.UserId, Token)).ResendAfterUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync(Token);
        }
        using var resend = await client.PostAsJsonAsync(ResendPath, new ResendEmailRequest(account.Email.ToUpperInvariant()), Token);
        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        var replacement = await ReadTokenAsync(account.UserId);
        Assert.NotEqual(account.Token, replacement);
        using var old = await client.PostAsJsonAsync(ConfirmPath, new ConfirmEmailRequest(account.UserId, account.Token), Token);
        using var current = await client.PostAsJsonAsync(ConfirmPath, new ConfirmEmailRequest(account.UserId, replacement), Token);
        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown@example.invalid")]
    [InlineData("not-an-email")]
    public async Task ResendDoesNotRevealAccountExistence(string? email)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(ResendPath, new ResendEmailRequest(email), Token);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("pending_verification", (await response.Content.ReadFromJsonAsync<RegistrationResponse>(Token))?.Status);
    }
}
