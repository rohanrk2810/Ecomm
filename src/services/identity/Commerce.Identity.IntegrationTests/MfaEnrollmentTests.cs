using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Commerce.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Commerce.Identity.IntegrationTests;

public sealed class MfaEnrollmentTests(IdentityFixture factory) : IClassFixture<IdentityFixture>
{
    private static Dictionary<string, string> Fields(string page) => new() { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value) };
    internal static string Otp(string encoded)
    {
        var bytes = new List<byte>(); int buffer = 0, bits = 0;
        foreach (var c in encoded.TrimEnd('=')) { buffer = (buffer << 5) | "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(c); bits += 5; if (bits >= 8) { bits -= 8; bytes.Add((byte)(buffer >> bits)); } }
        var counter = new byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(bytes.ToArray(), counter); int offset = hash[^1] & 15;
        return ((System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff) % 1000000).ToString("D6");
    }
    [Fact]
    public async Task EnrollmentUsesCsrfReauthenticationProtectedKeysAndOneTimeCodes()
    {
        string email = Guid.NewGuid() + "@example.invalid", password = "Synthetic!" + Guid.NewGuid().ToString("N") + "9";
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
            var user = new Account { Id = Guid.NewGuid(), UserName = Guid.NewGuid().ToString("N"), Email = email, EmailConfirmed = true }; id = user.Id;
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
        }
        using var client = factory.CreateClient();
        using (var rejected = await client.PostAsync("/account/mfa?handler=Begin", new FormUrlEncodedContent(new Dictionary<string, string> { { "Email", email }, { "Password", password } }))) Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var fields = Fields(await client.GetStringAsync("/account/mfa")); fields["Email"] = email; fields["Password"] = "Wrong!Password888";
        using (var rejected = await client.PostAsync("/account/mfa?handler=Begin", new FormUrlEncodedContent(fields))) Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        fields["Password"] = password;
        using var begin = await client.PostAsync("/account/mfa?handler=Begin", new FormUrlEncodedContent(fields)); Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        Assert.True(begin.Headers.CacheControl?.NoStore);
        var page = await begin.Content.ReadAsStringAsync(); var key = Regex.Match(page, "id=\"setup-key\">([^<]+)").Groups[1].Value; Assert.NotEmpty(key);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
            Assert.False((await db.Users.SingleAsync(x => x.Id == id)).TwoFactorEnabled);
            var saved = await db.UserTokens.SingleAsync(x => x.UserId == id && x.Name == "AuthenticatorKey");
            Assert.StartsWith("protected:v1:", saved.Value); Assert.DoesNotContain(key, saved.Value!);
        }
        fields = Fields(page); fields["Email"] = email; fields["Password"] = password; fields["Code"] = "wrong";
        using (var bad = await client.PostAsync("/account/mfa?handler=Confirm", new FormUrlEncodedContent(fields))) Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        fields["Code"] = Otp(key);
        using var confirmed = await client.PostAsync("/account/mfa?handler=Confirm", new FormUrlEncodedContent(fields)); Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var confirmation = await confirmed.Content.ReadAsStringAsync();
        var codes = Regex.Matches(confirmation, "<li><code>([^<]+)</code></li>").Select(x => WebUtility.HtmlDecode(x.Groups[1].Value)).ToArray(); Assert.Equal(10, codes.Length);
        Assert.DoesNotContain(key, confirmation); Assert.DoesNotContain(codes[0], await client.GetStringAsync("/account/mfa"));
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<Account>>(); var user = (await users.FindByIdAsync(id.ToString()))!;
            Assert.True(user.TwoFactorEnabled); var db = scope.ServiceProvider.GetRequiredService<IdentityDatabase>();
            var stored = await db.UserTokens.SingleAsync(x => x.UserId == id && x.Name == "RecoveryCodes"); Assert.StartsWith("protected:v1:", stored.Value); Assert.DoesNotContain(codes[0], stored.Value!);
            Assert.False(await scope.ServiceProvider.GetRequiredService<MfaGate>().VerifyAsync(user, fields["Code"], null, CancellationToken.None));
        }
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<Account>>(); var user = (await users.FindByIdAsync(id.ToString()))!;
            Assert.True(await scope.ServiceProvider.GetRequiredService<MfaGate>().VerifyAsync(user, null, codes[0], CancellationToken.None));
            Assert.False(await scope.ServiceProvider.GetRequiredService<MfaGate>().VerifyAsync(user, null, codes[0], CancellationToken.None));
        }
    }
}
