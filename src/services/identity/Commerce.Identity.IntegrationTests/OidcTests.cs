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
    private async Task<(string Code, string Verifier, Guid UserId)> CodeAsync(HttpClient client, bool mfa = false)
    {
        var account = await UserAsync(); var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var fields = await LoginFormAsync(client, verifier); fields["Email"] = account.User.Email ?? string.Empty; fields["Password"] = account.Password;
        if (mfa)
        {
            using var scope = factory.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
            var enrolled = (await users.FindByIdAsync(account.User.Id.ToString()))!;
            Assert.True((await users.ResetAuthenticatorKeyAsync(enrolled)).Succeeded); Assert.True((await users.SetTwoFactorEnabledAsync(enrolled, true)).Succeeded);
            fields["AuthenticatorCode"] = MfaEnrollmentTests.Otp((await users.GetAuthenticatorKeyAsync(enrolled))!);
        }
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
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var fields = await LoginFormAsync(client, verifier);
        fields["Email"] = user.User.Email!; fields["Password"] = user.Password;
        fields[recovery ? "RecoveryCode" : "AuthenticatorCode"] = code;
        using var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(fields), Token);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var authorized = await client.GetAsync(response.Headers.Location, Token);
        var authorizationCode = QueryHelpers.ParseQuery(authorized.Headers.Location!.Query)["code"].ToString();
        using var exchange = await ExchangeAsync(client, authorizationCode, verifier);
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        using var tokens = JsonDocument.Parse(await exchange.Content.ReadAsStringAsync(Token));
        using var claims = JsonDocument.Parse(WebEncoders.Base64UrlDecode(tokens.RootElement.GetProperty("id_token").GetString()!.Split('.')[1]));
        Assert.Contains(claims.RootElement.GetProperty("amr").EnumerateArray(), x => x.GetString() == "mfa");
        var access = tokens.RootElement.GetProperty("access_token").GetString()!;
        using var introspection = await client.PostAsync("/connect/introspect", new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = factory.ClientId, ["client_secret"] = factory.ClientSecret, ["token"] = access }), Token);
        using var introspected = JsonDocument.Parse(await introspection.Content.ReadAsStringAsync(Token));
        Assert.Contains(introspected.RootElement.GetProperty("amr").EnumerateArray(), x => x.GetString() == "mfa");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
            var session = await db.CustomerSessions.SingleAsync(x => x.UserId == user.User.Id, Token);
            Assert.NotNull(session.MfaVerifiedAtUtc);
            session.MfaVerifiedAtUtc = null; await db.SaveChangesAsync(Token);
        }
        Assert.False(await IntrospectAsync(client, access));
        using var other = Browser();
        var replay = await LoginFormAsync(other, WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48)));
        replay["Email"] = user.User.Email!; replay["Password"] = user.Password;
        replay[recovery ? "RecoveryCode" : "AuthenticatorCode"] = code;
        using var rejected = await other.PostAsync("/account/login", new FormUrlEncodedContent(replay), Token);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }
    [Fact]
    public async Task StaffAccessRequiresBearerMfaMembershipAndReadsOnlyOwnBranches()
    {
        using var anonymous = Browser();
        using var anonymousDenied = await anonymous.GetAsync("/api/v1/identity/staff-access", Token);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousDenied.StatusCode);
        Assert.Equal("application/problem+json", anonymousDenied.Content.Headers.ContentType?.MediaType);
        anonymous.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "forged");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/identity/staff-access", Token)).StatusCode);
        using var customer = Browser(); var plain = await CodeAsync(customer);
        using var plainExchange = await ExchangeAsync(customer, plain.Code, plain.Verifier);
        using var plainTokens = JsonDocument.Parse(await plainExchange.Content.ReadAsStringAsync(Token));
        customer.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", plainTokens.RootElement.GetProperty("access_token").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/identity/staff-access", Token)).StatusCode);
        using var staff = Browser(); var verified = await CodeAsync(staff, true);
        using var exchanged = await ExchangeAsync(staff, verified.Code, verified.Verifier);
        using var tokens = JsonDocument.Parse(await exchanged.Content.ReadAsStringAsync(Token));
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/v1/identity/staff-access", Token)).StatusCode);
        staff.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.RootElement.GetProperty("access_token").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/identity/staff-access", Token)).StatusCode);
        var branch = Guid.NewGuid(); var otherBranch = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>(); var role = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = "synthetic-" + Guid.NewGuid().ToString("N") };
            db.Roles.Add(role); db.StaffMemberships.AddRange(new StaffMembership { UserId = verified.UserId, IsActive = true }, new StaffMembership { UserId = plain.UserId, IsActive = true });
            db.StaffPermissions.Add(new StaffPermission { Code = "synthetic." + role.Id });
            db.StaffRolePermissions.Add(new StaffRolePermission { RoleId = role.Id, PermissionCode = "synthetic." + role.Id });
            db.StaffBranchRoles.AddRange(new StaffBranchRole { UserId = verified.UserId, RoleId = role.Id, BranchId = branch }, new StaffBranchRole { UserId = plain.UserId, RoleId = role.Id, BranchId = otherBranch }); await db.SaveChangesAsync(Token);
        }
        // Membership alone cannot upgrade a password-only session.
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/identity/staff-access", Token)).StatusCode);
        using var allowed = await staff.GetAsync("/api/v1/identity/staff-access?userId=" + plain.UserId, Token);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var grants = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync(Token));
        Assert.Equal(verified.UserId, grants.RootElement.GetProperty("userId").GetGuid());
        Assert.Equal(branch, Assert.Single(grants.RootElement.GetProperty("branches").EnumerateArray()).GetProperty("branchId").GetGuid());
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IdentityDatabase>().StaffMemberships.Where(x => x.UserId == verified.UserId).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false), Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/identity/staff-access", Token)).StatusCode);
    }
    [Fact]
    public async Task BranchRoleChangesEnforceDelegationAndAreAuditedIdempotent()
    {
        using var admin = Browser(); var actor = await CodeAsync(admin, true);
        using var exchanged = await ExchangeAsync(admin, actor.Code, actor.Verifier);
        using var tokens = JsonDocument.Parse(await exchanged.Content.ReadAsStringAsync(Token));
        admin.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.RootElement.GetProperty("access_token").GetString());
        using var targetBrowser = Browser(); var target = await CodeAsync(targetBrowser, true);
        var branch = Guid.NewGuid(); var adminRole = Guid.NewGuid(); var readRole = Guid.NewGuid(); var highRole = Guid.NewGuid();
        var readPermission = "fixture.read." + Guid.NewGuid().ToString("N"); var highPermission = "fixture.high." + Guid.NewGuid().ToString("N");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
            foreach (var id in new[] { adminRole, readRole, highRole }) db.Roles.Add(new IdentityRole<Guid> { Id = id, Name = "fixture-" + id });
            if (!await db.StaffPermissions.AnyAsync(x => x.Code == StaffGrantWriter.Permission, Token)) db.StaffPermissions.Add(new StaffPermission { Code = StaffGrantWriter.Permission });
            db.StaffPermissions.AddRange(new StaffPermission { Code = readPermission }, new StaffPermission { Code = highPermission });
            db.StaffRolePermissions.AddRange(new StaffRolePermission { RoleId = adminRole, PermissionCode = StaffGrantWriter.Permission }, new StaffRolePermission { RoleId = adminRole, PermissionCode = readPermission }, new StaffRolePermission { RoleId = readRole, PermissionCode = readPermission }, new StaffRolePermission { RoleId = highRole, PermissionCode = highPermission });
            db.StaffMemberships.Add(new StaffMembership { UserId = actor.UserId, IsActive = true }); db.StaffBranchRoles.Add(new StaffBranchRole { UserId = actor.UserId, BranchId = branch, RoleId = adminRole }); await db.SaveChangesAsync(Token);
        }
        using (var forbiddenCatalog = await admin.GetAsync($"/api/v1/identity/staff/branches/{Guid.NewGuid()}/roles", Token)) Assert.Equal(HttpStatusCode.Forbidden, forbiddenCatalog.StatusCode);
        using (var catalog = await admin.GetAsync($"/api/v1/identity/staff/branches/{branch}/roles", Token))
        {
            Assert.Equal(HttpStatusCode.OK, catalog.StatusCode); var choices = await catalog.Content.ReadFromJsonAsync<Commerce.Identity.Contracts.StaffRoleChoice[]>(Token);
            Assert.Contains(choices!, x => x.Id == readRole); Assert.DoesNotContain(choices!, x => x.Id == highRole);
        }
        string targetEmail;
        using (var scope = factory.Services.CreateScope()) targetEmail = (await scope.ServiceProvider.GetRequiredService<IdentityDatabase>().Users.SingleAsync(x => x.Id == target.UserId, Token)).Email!;
        using (var forbiddenLookup = await admin.PostAsJsonAsync($"/api/v1/identity/staff/branches/{Guid.NewGuid()}/resolve-account", new { email = targetEmail }, Token)) Assert.Equal(HttpStatusCode.Forbidden, forbiddenLookup.StatusCode);
        using (var unknown = await admin.PostAsJsonAsync($"/api/v1/identity/staff/branches/{branch}/resolve-account", new { email = Guid.NewGuid() + "@example.invalid" }, Token)) Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        using (var lookup = await admin.PostAsJsonAsync($"/api/v1/identity/staff/branches/{branch}/resolve-account", new { email = targetEmail }, Token))
        {
            Assert.Equal(HttpStatusCode.OK, lookup.StatusCode); var resolved = await lookup.Content.ReadFromJsonAsync<Commerce.Identity.Contracts.StaffAccountChoice>(Token); Assert.Equal(target.UserId, resolved!.UserId); Assert.Empty(resolved.AssignedRoleIds);
        }
        string PathFor(Guid b, Guid r) => $"/api/v1/identity/staff/{target.UserId}/branches/{b}/roles/{r}";
        var notEnrolled = await UserAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/v1/identity/staff/{notEnrolled.User.Id}/branches/{branch}/roles/{readRole}", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsync(PathFor(Guid.NewGuid(), readRole), null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsync(PathFor(branch, highRole), null, Token)).StatusCode);
        var responses = await Task.WhenAll(admin.PutAsync(PathFor(branch, readRole), null, Token), admin.PutAsync(PathFor(branch, readRole), null, Token));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode)); foreach (var response in responses) response.Dispose();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>(); Assert.Equal(1, await db.StaffGrantAudits.CountAsync(x => x.TargetUserId == target.UserId, Token));
            Assert.Equal(1, await db.StaffMemberships.Where(x => x.UserId == target.UserId).Select(x => x.Version).SingleAsync(Token));
        }
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(PathFor(branch, readRole), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(PathFor(branch, readRole), Token)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>(); var audits = await db.StaffGrantAudits.Where(x => x.TargetUserId == target.UserId).ToListAsync(Token); Assert.Equal(2, audits.Count);
            var ids = audits.Select(x => x.Id).ToArray(); var events = await db.Outbox.Where(x => ids.Contains(x.Id)).ToListAsync(Token); Assert.Equal(2, events.Count);
            using var latest = JsonDocument.Parse(events.Single(x => x.Id == audits.Single(a => !a.Granted).Id).Payload); Assert.Equal(2, latest.RootElement.GetProperty("aggregateVersion").GetInt64()); Assert.Empty(latest.RootElement.GetProperty("data").GetProperty("access").GetProperty("branches").EnumerateArray());
            Assert.False(await db.StaffMemberships.Where(x => x.UserId == target.UserId).Select(x => x.IsActive).SingleAsync(Token));
            await db.StaffRolePermissions.Where(x => x.RoleId == adminRole && x.PermissionCode == StaffGrantWriter.Permission).ExecuteDeleteAsync(Token);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsync(PathFor(branch, readRole), null, Token)).StatusCode);
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
