using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Commerce.Identity.Contracts;
using Commerce.Identity.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Commerce.Identity.IntegrationTests;

public sealed class IdentityFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        var connection = Environment.GetEnvironmentVariable("IdentityTestConnection")
            ?? throw new InvalidOperationException("Inject IdentityTestConnection for real PostgreSQL integration tests.");
        if (new NpgsqlConnectionStringBuilder(connection).Database != "commerce_identity_tests")
            throw new InvalidOperationException("Only the dedicated synthetic test database is allowed.");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Identity"] = connection,
            ["Messaging:PublisherEnabled"] = "false",
            ["Registration:RequestsPerMinute"] = "1000"
        }));
    }
    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
        if (db.Database.GetDbConnection().Database != "commerce_identity_tests")
            throw new InvalidOperationException("Resolved database must be the isolated synthetic test database.");
        await db.Database.MigrateAsync();
    }
    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;
}
public sealed class RegistrationTests(IdentityFixture factory) : IClassFixture<IdentityFixture>
{
    private static readonly CancellationToken Token = CancellationToken.None;
    private static string Password => "Synthetic!" + Guid.NewGuid().ToString("N") + "7";
    private static string Email => "synthetic-" + Guid.NewGuid().ToString("N") + "@example.invalid";

    [Fact]
    public async Task RegistrationHashesPasswordAndCommitsOneOutboxEvent()
    {
        var email = Email;
        var password = Password;
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/identity/registrations", new RegistrationRequest(email, password), Token);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("pending_verification", (await response.Content.ReadFromJsonAsync<RegistrationResponse>(Token))?.Status);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        var account = await users.FindByEmailAsync(email);
        Assert.NotNull(account);
        Assert.False(account.EmailConfirmed);
        Assert.NotEqual(password, account.PasswordHash);
        Assert.True(await users.CheckPasswordAsync(account, password));
        var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
        var payloads = await db.Outbox.Select(x => x.Payload).ToListAsync(Token);
        var payload = Assert.Single(payloads, x => x.Contains(account.Id.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(email, payload);
        Assert.DoesNotContain(password, payload);
        using var document = JsonDocument.Parse(payload);
        Assert.Equal("UserRegistered", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ConcurrentCaseInsensitiveDuplicatesReturnSameStateAndCreateOneAccount()
    {
        var email = Email;
        using var client = factory.CreateClient();
        var password = Password;
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => client.PostAsJsonAsync(
            "/api/v1/identity/registrations", new RegistrationRequest(i % 2 == 0 ? email : email.ToUpperInvariant(), password), Token)));
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("pending_verification", (await response.Content.ReadFromJsonAsync<RegistrationResponse>(Token))?.Status);
            response.Dispose();
        }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
        var account = Assert.Single(await db.Users.Where(x => x.NormalizedEmail == email.ToUpperInvariant()).ToListAsync(Token));
        var events = await db.Outbox.Select(x => x.Payload).ToListAsync(Token);
        Assert.Single(events, x => x.Contains(account.Id.ToString(), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("bad-address", "LongEnough!Password7")]
    [InlineData("synthetic@example.invalid", "short")]
    [InlineData(null, null)]
    public async Task InvalidInputDoesNotCreateAnAccount(string? email, string? password)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/identity/registrations", new RegistrationRequest(email, password), Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CallerCannotAssignRolesOrConfirmationFlags()
    {
        var email = Email;
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/identity/registrations",
            new { email, password = Password, role = "administrator", emailConfirmed = true }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IdentityDatabase>()
            .Users.AnyAsync(x => x.NormalizedEmail == email.ToUpperInvariant(), Token));
    }

    [Fact]
    public async Task FailedOutboxWriteRollsBackAccountCreation()
    {
        await using var failingFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<IdentityDatabase>(options => options.AddInterceptors(new FailingOutboxInterceptor()))));
        using var client = failingFactory.CreateClient();
        var email = Email;
        using var response = await client.PostAsJsonAsync("/api/v1/identity/registrations", new RegistrationRequest(email, Password), Token);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
        Assert.False(await db.Users.AnyAsync(x => x.NormalizedEmail == email.ToUpperInvariant(), Token));
    }

    [Fact]
    public async Task UnknownAndPrivilegedRoutesAreDenied()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/identity/users", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegistrationQuotaRejectsAdditionalAttempts()
    {
        await using var limitedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Registration:RequestsPerMinute"] = "1" })));
        using var client = limitedFactory.CreateClient();
        var request = new RegistrationRequest(Email, Password);
        using var first = await client.PostAsJsonAsync("/api/v1/identity/registrations", request, Token);
        using var second = await client.PostAsJsonAsync("/api/v1/identity/registrations", request, Token);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }
}

public sealed class FailingOutboxInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context?.ChangeTracker.Entries<OutboxMessage>().Any(x => x.State == EntityState.Added) == true)
            throw new InvalidOperationException("Synthetic outbox storage failure.");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
