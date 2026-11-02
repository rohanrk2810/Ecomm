using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
namespace Commerce.Gateway;
// An intentionally disabled BFF must not initialize an unconfigured remote OIDC handler.
public sealed class BffSchemeProvider : AuthenticationSchemeProvider
{
    public BffSchemeProvider(IOptions<AuthenticationOptions> options, IOptions<BffSettings> settings) : base(options)
    {
        if (!settings.Value.Enabled) RemoveScheme("oidc");
    }
}
