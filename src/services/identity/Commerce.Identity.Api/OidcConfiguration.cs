using System.Security.Cryptography.X509Certificates;
using Commerce.Identity.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
namespace Commerce.Identity.Api;

public static class OidcConfiguration
{
    public static void AddCustomerOidc(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<OidcSettings>().BindConfiguration("Oidc")
            .Validate(x => Uri.TryCreate(x.Issuer, UriKind.Absolute, out var issuer) &&
                (issuer.Scheme == "https" || (builder.Environment.IsDevelopment() && issuer.IsLoopback && issuer.Scheme == "http")), "Configure the public OIDC issuer")
            .Validate(x => Uri.TryCreate(x.PublicAppOrigin, UriKind.Absolute, out var app) &&
                (app.Scheme == "https" || (builder.Environment.IsDevelopment() && app.IsLoopback && app.Scheme == "http")), "Configure the public application origin")
            .Validate(x => !string.IsNullOrWhiteSpace(x.ClientId) && x.ClientSecret.Length >= 32 && x.CertificatePassword.Length >= 32, "OIDC credentials are required")
            .Validate(x => x.AccessTokenMinutes is >= 5 and <= 30 && x.SessionMinutes >= x.AccessTokenMinutes && x.SessionMinutes <= 120 &&
                x.FailedAttempts is >= 3 and <= 10 && x.LockoutMinutes is >= 1 and <= 60, "Invalid session policy").ValidateOnStart();
        builder.Services.AddControllersWithViews(); builder.Services.AddRazorPages();
        builder.Services.AddAntiforgery(options => options.Cookie.Name = "commerce.identity.csrf");
        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "commerce.identity.session"; options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.LoginPath = "/account/login"; options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = context => context.HttpContext.RequestServices.GetRequiredService<CustomerSessionValidator>().ValidateCookieAsync(context);
        });
        builder.Services.AddOptions<IdentityOptions>().Configure<IOptions<OidcSettings>>((options, settings) =>
        {
            options.Lockout.AllowedForNewUsers = true; options.Lockout.MaxFailedAccessAttempts = settings.Value.FailedAttempts;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(settings.Value.LockoutMinutes);
        });
        builder.Services.AddScoped<CustomerSessionValidator>();
        builder.Services.AddOpenIddict().AddCore(options => options.UseEntityFrameworkCore().UseDbContext<IdentityDatabase>())
            .AddServer(options =>
            {
                options.SetAuthorizationEndpointUris("connect/authorize").SetTokenEndpointUris("connect/token")
                    .SetEndSessionEndpointUris("connect/logout").SetIntrospectionEndpointUris("connect/introspect");
                options.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
                options.Configure(configured => { configured.CodeChallengeMethods.Clear(); configured.CodeChallengeMethods.Add("S256"); });
                options.RegisterScopes(Scopes.OpenId);
                options.AddEventHandler<OpenIddictServerEvents.HandleIntrospectionRequestContext>(handler =>
                    handler.UseScopedHandler<SessionIntrospectionHandler>()
                        .SetOrder(OpenIddictServerHandlers.Introspection.AttachPrincipal.Descriptor.Order + 500));
                var asp = options.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough().EnableEndSessionEndpointPassthrough();
                if (builder.Environment.IsDevelopment()) asp.DisableTransportSecurityRequirement();
            });
        builder.Services.AddOptions<OpenIddictServerOptions>().Configure<IOptions<OidcSettings>>((options, configured) =>
        {
            var settings = configured.Value;
            options.Issuer = new Uri(settings.Issuer);
            // Discovery must advertise public origins even when fetched over the private backchannel.
            options.AuthorizationEndpointUris.Clear(); options.AuthorizationEndpointUris.Add(new Uri(options.Issuer, "connect/authorize"));
            options.TokenEndpointUris.Clear(); options.TokenEndpointUris.Add(new Uri(options.Issuer, "connect/token"));
            options.EndSessionEndpointUris.Clear(); options.EndSessionEndpointUris.Add(new Uri(options.Issuer, "connect/logout"));
            options.IntrospectionEndpointUris.Clear(); options.IntrospectionEndpointUris.Add(new Uri(options.Issuer, "connect/introspect"));
            options.AccessTokenLifetime = TimeSpan.FromMinutes(settings.AccessTokenMinutes);
            options.IdentityTokenLifetime = TimeSpan.FromMinutes(settings.AccessTokenMinutes);
            options.SigningCredentials.Add(new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.X509SecurityKey(X509CertificateLoader.LoadPkcs12FromFile(settings.SigningCertificatePath, settings.CertificatePassword)), Algorithms.RsaSha256));
            options.EncryptionCredentials.Add(new Microsoft.IdentityModel.Tokens.EncryptingCredentials(
                new Microsoft.IdentityModel.Tokens.X509SecurityKey(X509CertificateLoader.LoadPkcs12FromFile(settings.EncryptionCertificatePath, settings.CertificatePassword)),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.RsaOAEP, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.Aes256CbcHmacSha512));
        });
    }
    public static async Task ProvisionClientAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var options = services.GetRequiredService<IOptions<OidcSettings>>().Value;
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = options.ClientId,
            ClientSecret = options.ClientSecret,
            ClientType = ClientTypes.Confidential,
            ConsentType = ConsentTypes.Implicit,
            RedirectUris = { new Uri(options.PublicAppOrigin.TrimEnd('/') + "/signin-oidc") },
            PostLogoutRedirectUris = { new Uri(options.PublicAppOrigin.TrimEnd('/') + "/signout-callback-oidc") },
            Permissions ={Permissions.Endpoints.Authorization,Permissions.Endpoints.Token,Permissions.Endpoints.EndSession,
                Permissions.Endpoints.Introspection,Permissions.GrantTypes.AuthorizationCode,Permissions.ResponseTypes.Code},
            Requirements = { Requirements.Features.ProofKeyForCodeExchange }
        };
        var existing = await manager.FindByClientIdAsync(options.ClientId, cancellationToken);
        if (existing is null) await manager.CreateAsync(descriptor, cancellationToken);
        else await manager.UpdateAsync(existing, descriptor, cancellationToken);
    }
}
