using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Distributed;
namespace Commerce.Gateway;

public sealed class RedisTicketStore(IDistributedCache cache, IDataProtectionProvider protection) : ITicketStore
{
    private const string Prefix = "commerce:bff:v1:session:";
    private readonly IDataProtector protector = protection.CreateProtector("Commerce.Gateway.Tickets.v1");
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)); await RenewAsync(key, ticket); return key;
    }
    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var expiry = ticket.Properties.ExpiresUtc ?? throw new InvalidOperationException("A session requires absolute expiry.");
        await cache.SetAsync(Prefix + key, protector.Protect(TicketSerializer.Default.Serialize(ticket)), new DistributedCacheEntryOptions { AbsoluteExpiration = expiry });
    }
    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var value = await cache.GetAsync(Prefix + key); if (value is null) return null;
        try
        {
            var ticket = TicketSerializer.Default.Deserialize(protector.Unprotect(value));
            if (ticket?.Properties.ExpiresUtc > DateTimeOffset.UtcNow) return ticket;
        }
        catch (CryptographicException) { /* Invalid/obsolete protected tickets fail closed. */ }
        await RemoveAsync(key); return null;
    }
    public Task RemoveAsync(string key) => cache.RemoveAsync(Prefix + key);
}
