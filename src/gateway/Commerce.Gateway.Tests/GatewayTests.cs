using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Commerce.Gateway.Tests;

public sealed class GatewayTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public GatewayTests(WebApplicationFactory<Program> factory)
    {
        client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpointsAreAvailable(string path)
    {
        using var response = await client.GetAsync(path, TestContextToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContextToken));
    }

    [Fact]
    public async Task StatusDoesNotClaimCommerceIsAvailable()
    {
        var status = await client.GetFromJsonAsync<PlatformStatus>("/api/v1/platform/status", TestContextToken);
        Assert.NotNull(status);
        Assert.False(status.CommerceAvailable);
    }

    [Fact]
    public async Task CallerCannotInjectCorrelationIdentifier()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-ID", "untrusted-caller-value");
        using var response = await client.SendAsync(request, TestContextToken);
        var actual = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));
        Assert.True(Guid.TryParseExact(actual, "N", out _));
        Assert.NotEqual("untrusted-caller-value", actual);
    }

    [Fact]
    public async Task UnknownApiReturnsSafeProblemDetails()
    {
        using var response = await client.GetAsync("/api/v1/not-implemented", TestContextToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContextToken);
        Assert.DoesNotContain("StackTrace", body);
        Assert.Contains("correlationId", body);
    }

    private static CancellationToken TestContextToken => CancellationToken.None;

    [Fact]
    public async Task DisabledBffDoesNotExposeTokensOrClaimAnActiveSession()
    {
        using var response = await client.GetAsync("/bff/session", TestContextToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(TestContextToken);
        Assert.False(session.GetProperty("available").GetBoolean());
        Assert.False(session.GetProperty("authenticated").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, session.GetProperty("userId").ValueKind);
        Assert.False(session.TryGetProperty("access_token", out _));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task DisabledBffCannotStartLogin()
    {
        using var response = await client.GetAsync("/bff/login", TestContextToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task UnconfiguredIdentityDeniesProxyRoutesBeforeCallingUpstream()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ReverseProxy:Routes:protected:ClusterId"] = "private",
                    ["ReverseProxy:Routes:protected:Match:Path"] = "/api/v1/protected/{**catch-all}",
                    ["ReverseProxy:Clusters:private:Destinations:one:Address"] = "http://127.0.0.1:1/"
                })));
        using var protectedClient = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/protected/resource");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "untrusted");
        using var response = await protectedClient.SendAsync(request, TestContextToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
