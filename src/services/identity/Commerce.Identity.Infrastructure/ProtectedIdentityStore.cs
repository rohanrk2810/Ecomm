using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
namespace Commerce.Identity.Infrastructure;

// Preserve maintained Identity token/recovery behavior while protecting MFA material at rest.
public sealed class ProtectedIdentityStore(IdentityDatabase db, IDataProtectionProvider protection)
    : UserStore<Account, IdentityRole<Guid>, IdentityDatabase, Guid>(db)
{
    private IDataProtector Protector(Account user, string provider, string name) =>
        protection.CreateProtector("Commerce.Identity.MfaMaterial.v1", user.Id.ToString(), provider, name);
    private static bool Sensitive(string provider, string name) => provider == "[AspNetUserStore]" &&
        name is "AuthenticatorKey" or "RecoveryCodes";
    public override Task SetTokenAsync(Account user, string loginProvider, string name, string? value, CancellationToken cancellationToken = default) =>
        base.SetTokenAsync(user, loginProvider, name, value is not null && Sensitive(loginProvider, name)
            ? "protected:v1:" + Protector(user, loginProvider, name).Protect(value) : value, cancellationToken);
    public override async Task<string?> GetTokenAsync(Account user, string loginProvider, string name, CancellationToken cancellationToken = default)
    {
        var value = await base.GetTokenAsync(user, loginProvider, name, cancellationToken);
        return value is not null && Sensitive(loginProvider, name) && value.StartsWith("protected:v1:", StringComparison.Ordinal)
            ? Protector(user, loginProvider, name).Unprotect(value[13..]) : value;
    }
}
