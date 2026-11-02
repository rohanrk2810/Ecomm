using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Commerce.Identity.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Commerce.Identity.IntegrationTests;

public sealed class PasswordRecoveryTests(IdentityFixture factory) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task ResetIsSingleUseEnforcesPolicyAndRevokesSessions()
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
        var user = new Account { Id = Guid.NewGuid(), UserName = Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid", EmailConfirmed = true };
        var oldPassword = "Synthetic!" + Guid.NewGuid().ToString("N") + "7";
        Assert.True((await users.CreateAsync(user, oldPassword)).Succeeded);
        var session = new CustomerSession { Id = Guid.NewGuid(), UserId = user.Id, SecurityStamp = user.SecurityStamp!, CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30) };
        db.CustomerSessions.Add(session); await db.SaveChangesAsync();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/identity/password-reset-requests", new PasswordResetRequest(user.Email))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/identity/password-reset-requests", new PasswordResetRequest(user.Email))).StatusCode);
        var row = Assert.Single(await db.VerificationEmails.Where(x => x.UserId == user.Id && x.Kind == "password-reset").ToListAsync());
        var payload = JsonSerializer.Deserialize<VerificationEmailContent>(scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector(VerificationIssuer.MailPurpose).Unprotect(row.ProtectedPayload))!;
        var values = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(payload.Link).Fragment.TrimStart('#'));
        var token = values["token"].ToString();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/identity/password-resets", new PasswordReset(user.Id, token, "weak"))).StatusCode);
        var password = "Replacement!" + Guid.NewGuid().ToString("N") + "8";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/identity/password-resets", new PasswordReset(Guid.NewGuid(), token, password))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/identity/password-resets", new PasswordReset(user.Id, token, password))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/identity/password-resets", new PasswordReset(user.Id, token, password))).StatusCode);
        await db.Entry(user).ReloadAsync(); await db.Entry(session).ReloadAsync();
        Assert.False(await users.CheckPasswordAsync(user, oldPassword)); Assert.True(await users.CheckPasswordAsync(user, password));
        Assert.NotNull(session.RevokedAtUtc);
    }
    [Fact]
    public async Task UnknownAndUnverifiedAccountsReceiveNeutralResponseWithoutMail()
    {
        using var scope = factory.Services.CreateScope();
        var user = new Account { Id = Guid.NewGuid(), UserName = Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid" };
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<Account>>().CreateAsync(user)).Succeeded);
        using var client = factory.CreateClient();
        foreach (var email in new[] { user.Email, Guid.NewGuid() + "@example.invalid" })
        {
            var response = await client.PostAsJsonAsync("/api/v1/identity/password-reset-requests", new PasswordResetRequest(email));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        Assert.False(await scope.ServiceProvider.GetRequiredService<IdentityDatabase>().VerificationEmails.AnyAsync(x => x.UserId == user.Id));
    }
}
