using Commerce.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Commerce.Identity.IntegrationTests;

public sealed class DemoStaffTests(IdentityFixture factory) : IClassFixture<IdentityFixture>
{
    private static DemoStaffSettings Settings(string email) => new()
    {
        Email = email,
        Password = "Synthetic!" + Guid.NewGuid().ToString("N") + "8",
        RoleName = "fixture-" + Guid.NewGuid().ToString("N"),
        RoleId = Guid.NewGuid(),
        BranchId = Guid.NewGuid(),
        Permissions = ["fixture.read." + Guid.NewGuid().ToString("N")]
    };
    [Theory]
    [InlineData(false, "demo@example.invalid")]
    [InlineData(true, "person@example.com")]
    public async Task SeederRejectsProductionAndRealEmailDomains(bool development, string email)
    {
        using var scope = factory.Services.CreateScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<DemoStaffSeeder>()
            .SeedAsync(Settings(email), development, CancellationToken.None));
    }
    [Fact]
    public async Task DemoIsAuditedAndDoesNotResetCredentialsFactorsOrRevokedGrants()
    {
        using var scope = factory.Services.CreateScope();
        var settings = Settings("demo-" + Guid.NewGuid().ToString("N") + "@example.invalid");
        var seeder = scope.ServiceProvider.GetRequiredService<DemoStaffSeeder>();
        var id = await seeder.SeedAsync(settings, true, CancellationToken.None);
        var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        var user = (await users.FindByIdAsync(id.ToString()))!;
        Assert.True(user.EmailConfirmed); Assert.False(user.TwoFactorEnabled);
        Assert.False(await scope.ServiceProvider.GetRequiredService<StaffAccessReader>().AllowedAsync(id.ToString(), Guid.NewGuid().ToString(), CancellationToken.None));
        Assert.Equal(settings.BranchId, (await db.StaffBranchRoles.SingleAsync(x => x.UserId == id)).BranchId);
        Assert.Equal(2, (await db.Outbox.AsNoTracking().ToListAsync()).Count(x => x.Payload.Contains(id.ToString(), StringComparison.Ordinal)));
        Assert.Equal(1, await db.AuthenticationAudits.CountAsync(x => x.UserId == id && x.Action == "DemoStaffProvisioned"));
        Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded);
        Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
        var key = await users.GetAuthenticatorKeyAsync(user);
        await db.StaffMemberships.Where(x => x.UserId == id).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false));
        await db.StaffBranchRoles.Where(x => x.UserId == id).ExecuteDeleteAsync();
        Assert.Equal(id, await seeder.SeedAsync(settings, true, CancellationToken.None));
        await db.Entry(user).ReloadAsync(); Assert.True(user.TwoFactorEnabled);
        Assert.True(key == await users.GetAuthenticatorKeyAsync(user)); Assert.True(await users.CheckPasswordAsync(user, settings.Password));
        Assert.False(await db.StaffMemberships.AsNoTracking().Where(x => x.UserId == id).Select(x => x.IsActive).SingleAsync());
        Assert.False(await db.StaffBranchRoles.AnyAsync(x => x.UserId == id));
        Assert.Equal(2, (await db.Outbox.AsNoTracking().ToListAsync()).Count(x => x.Payload.Contains(id.ToString(), StringComparison.Ordinal)));
        var wrong = Settings(settings.Email);
        await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync(wrong, true, CancellationToken.None));
    }
}
