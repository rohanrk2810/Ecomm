using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Commerce.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Commerce.Identity.IntegrationTests;

public sealed class OidcTests(IdentityFixture factory) : IClassFixture<IdentityFixture>
{
    private const string Redirect = "http://localhost:4200/signin-oidc";
    private static readonly CancellationToken Token = CancellationToken.None;
    private HttpClient Browser() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private async Task<(Account User, string Password)> UserAsync(bool confirmed = true)
    {
        using var scope = factory.Services.CreateScope(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        var user = new Account { Id = Guid.NewGuid(), UserName = Guid.NewGuid().ToString("N"), Email = "oidc-" + Guid.NewGuid().ToString("N") + "@example.invalid", EmailConfirmed = confirmed };
        var password = "Synthetic!" + Guid.NewGuid().ToString("N") + "8";
        Assert.True((await manager.CreateAsync(user, password)).Succeeded); return (user, password);
    }
    private string AuthorizeUrl(string verifier, string? redirect = null, bool pkce = true) => QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
    {
        ["client_id"] = factory.ClientId,
        ["redirect_uri"] = redirect ?? Redirect,
        ["response_type"] = "code",
        ["scope"] = "openid",
        ["state"] = Guid.NewGuid().ToString("N"),
        ["nonce"] = Guid.NewGuid().ToString("N"),
        ["code_challenge"] = pkce ? WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) : null,
        ["code_challenge_method"] = pkce ? "S256" : null
    });
    private static Dictionary<string, string> Inputs(string html)
    {
        var values = new Dictionary<string, string>();
        foreach (Match input in Regex.Matches(html, "<input[^>]+>"))
        {
            var name = Regex.Match(input.Value, "name=\"([^\"]+)\""); var value = Regex.Match(input.Value, "value=\"([^\"]*)\"");
            if (name.Success) values[name.Groups[1].Value] = WebUtility.HtmlDecode(value.Groups[1].Value);
        }
        return values;
    }
    private async Task<Dictionary<string, string>> LoginFormAsync(HttpClient client, string verifier)
    {
        using var authorize = await client.GetAsync(AuthorizeUrl(verifier), Token); Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        using var page = await client.GetAsync(authorize.Headers.Location, Token); Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        return Inputs(await page.Content.ReadAsStringAsync(Token));
    }
    private async Task<(string Code, string Verifier, Guid UserId)> CodeAsync(HttpClient client)
    {
        var account = await UserAsync(); var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var fields = await LoginFormAsync(client, verifier); fields["Email"] = account.User.Email ?? string.Empty; fields["Password"] = account.Password;
        using var login = await client.PostAsync("/account/login", new FormUrlEncodedContent(fields), Token); Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var authorization = await client.GetAsync(login.Headers.Location, Token); Assert.Equal(HttpStatusCode.Redirect, authorization.StatusCode);
        var location = authorization.Headers.Location ?? throw new InvalidOperationException("Missing callback.");
        Assert.Equal(new Uri(Redirect).GetLeftPart(UriPartial.Authority), location.GetLeftPart(UriPartial.Authority));
        var code = QueryHelpers.ParseQuery(location.Query)["code"].ToString(); Assert.NotEmpty(code); return (code, verifier, account.User.Id);
    }
    private Task<HttpResponseMessage> ExchangeAsync(HttpClient client, string code, string verifier) => client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
    { ["grant_type"] = "authorization_code", ["client_id"] = factory.ClientId, ["client_secret"] = factory.ClientSecret, ["code"] = code, ["code_verifier"] = verifier, ["redirect_uri"] = Redirect }), Token);
    private async Task<bool> IntrospectAsync(HttpClient client, string accessToken)
    {
        using var response = await client.PostAsync("/connect/introspect", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["client_id"] = factory.ClientId, ["client_secret"] = factory.ClientSecret, ["token"] = accessToken }), Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)); return data.RootElement.GetProperty("active").GetBoolean();
    }
    [Fact]
    public async Task CodeFlowRequiresPkceAndRejectsCodeReplay()
    {
        using var client = Browser(); var code = await CodeAsync(client);
        using var response = await ExchangeAsync(client, code.Code, code.Verifier); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.NotEmpty(data.RootElement.GetProperty("access_token").GetString() ?? string.Empty);
        Assert.NotEmpty(data.RootElement.GetProperty("id_token").GetString() ?? string.Empty);
        Assert.False(data.RootElement.TryGetProperty("refresh_token", out _));
        using var replay = await ExchangeAsync(client, code.Code, code.Verifier); Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }
    [Fact]
    public async Task IncorrectCodeVerifierCannotRedeemCode()
    {
        using var client = Browser(); var code = await CodeAsync(client);
        using var result = await ExchangeAsync(client, code.Code, WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48)));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }
    [Fact]
    public async Task DurableSessionRevocationMakesIntrospectionInactive()
    {
        using var client = Browser(); var code = await CodeAsync(client); using var response = await ExchangeAsync(client, code.Code, code.Verifier);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var access = data.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("No access token.");
        Assert.True(await IntrospectAsync(client, access));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>(); var session = await db.CustomerSessions.SingleAsync(x => x.UserId == code.UserId, Token);
            session.RevokedAtUtc = DateTimeOffset.UtcNow; await db.SaveChangesAsync(Token);
        }
        Assert.False(await IntrospectAsync(client, access));
    }
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task UnverifiedAccountsAndWrongPasswordsDoNotSignIn(bool confirmed, bool correctPassword)
    {
        var user = await UserAsync(confirmed); using var client = Browser(); var fields = await LoginFormAsync(client, WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48)));
        fields["Email"] = user.User.Email ?? string.Empty; fields["Password"] = correctPassword ? user.Password : "Incorrect!Password88";
        using var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(fields), Token); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope(); Assert.False(await scope.ServiceProvider.GetRequiredService<IdentityDatabase>().CustomerSessions.AnyAsync(x => x.UserId == user.User.Id, Token));
    }
    [Fact]
    public async Task PasswordPostRequiresAntiforgery()
    {
        var user = await UserAsync(); using var client = Browser();
        using var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Email"] = user.User.Email ?? string.Empty, ["Password"] = user.Password, ["ReturnUrl"] = AuthorizeUrl(new string('a', 64)) }), Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task MfaEnabledAccountCannotBypassSecondFactor()
    {
        var user = await UserAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
            var account = await users.FindByIdAsync(user.User.Id.ToString()) ?? throw new InvalidOperationException("Test account missing.");
            Assert.True((await users.SetTwoFactorEnabledAsync(account, true)).Succeeded);
        }
        using var client = Browser();
        var fields = await LoginFormAsync(client, WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48)));
        fields["Email"] = user.User.Email ?? string.Empty; fields["Password"] = user.Password;
        using var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(fields), Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var check = factory.Services.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<IdentityDatabase>().CustomerSessions.AnyAsync(x => x.UserId == user.User.Id, Token));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnrolledMfaAllowsOneCodeAndRejectsReplay(bool recovery)
    {
        var user = await UserAsync(); string code;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
            var account = (await users.FindByIdAsync(user.User.Id.ToString()))!;
            Assert.True((await users.ResetAuthenticatorKeyAsync(account)).Succeeded);
            Assert.True((await users.SetTwoFactorEnabledAsync(account, true)).Succeeded);
            if (recovery) code = (await users.GenerateNewTwoFactorRecoveryCodesAsync(account, 2))!.First();
            else
            {
                var encoded = (await users.GetAuthenticatorKeyAsync(account))!;
                var bytes = new List<byte>(); int buffer = 0, bits = 0;
                foreach (var character in encoded.TrimEnd('='))
                {
                    buffer = (buffer << 5) | "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(character); bits += 5;
                    if (bits >= 8) { bits -= 8; bytes.Add((byte)(buffer >> bits)); }
                }
                var counter = new byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
                var hash = HMACSHA1.HashData(bytes.ToArray(), counter); int offset = hash[^1] & 15;
                code = ((System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff) % 1000000).ToString("D6");
            }
        }
        using var client = Browser();
        var fields = await LoginFormAsync(client, WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48)));
        fields["Email"] = user.User.Email!; fields["Password"] = user.Password;
        fields[recovery ? "RecoveryCode" : "AuthenticatorCode"] = code;
        using var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(fields), Token);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var other = Browser();
        var replay = await LoginFormAsync(other, WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48)));
        replay["Email"] = user.User.Email!; replay["Password"] = user.Password;
        replay[recovery ? "RecoveryCode" : "AuthenticatorCode"] = code;
        using var rejected = await other.PostAsync("/account/login", new FormUrlEncodedContent(replay), Token);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }
    [Fact]
    public async Task MissingPkceChallengeIsRejected()
    {
        using var client = Browser();
        using var response = await client.GetAsync(AuthorizeUrl(new string('a', 64), pkce: false), Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }
    [Fact]
    public async Task UnregisteredRedirectCannotReceiveAnAuthorizationResponse()
    {
        using var client = Browser(); using var result = await client.GetAsync(AuthorizeUrl(new string('a', 64), "https://untrusted.invalid/callback"), Token);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode); Assert.Null(result.Headers.Location);
    }
}
