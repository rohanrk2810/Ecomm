using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
namespace Commerce.Gateway;

public static class BffConfiguration
{
    public const string CookieScheme = "BffCookie";
    public static void AddBff(this WebApplicationBuilder builder)
    {
        builder.Services.AddExceptionHandler<BffDependencyExceptionHandler>();
        builder.Services.AddHealthChecks().AddCheck<BffReadiness>("bff-sessions", tags: ["ready"]);
        builder.Services.AddOptions<BffSettings>().BindConfiguration("Bff")
         .Validate(x => !x.Enabled || (ValidOrigin(x.Authority, builder.Environment.IsDevelopment()) && ValidOrigin(x.PublicAppOrigin, builder.Environment.IsDevelopment()) &&
            Uri.TryCreate(x.InternalAuthority, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || (builder.Environment.IsDevelopment() && uri.Scheme == "http")) && string.IsNullOrEmpty(uri.UserInfo)), "Invalid BFF origins")
         .Validate(x => !x.Enabled || (!string.IsNullOrWhiteSpace(x.ClientId) && x.ClientSecret.Length >= 32 && !string.IsNullOrWhiteSpace(x.RedisConnectionString) &&
            !string.IsNullOrWhiteSpace(x.KeyDirectory) && (builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(x.KeyCertificatePath))), "BFF credentials, Redis and protected keys are required").ValidateOnStart();
        builder.Services.AddStackExchangeRedisCache(_ => { });
        builder.Services.AddOptions<RedisCacheOptions>().Configure<IOptions<BffSettings>>((options, settings) => options.Configuration = settings.Value.RedisConnectionString);
        builder.Services.AddSingleton<RedisTicketStore>(); builder.Services.AddScoped<IdentitySessionClient>();
        var protection = builder.Services.AddDataProtection().SetApplicationName("Commerce.Gateway");
        builder.Services.AddOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>()
         .Configure<IOptions<BffSettings>, ILoggerFactory>((options, settings, logs) =>
         {
             if (settings.Value.Enabled) options.XmlRepository = new Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository(new DirectoryInfo(settings.Value.KeyDirectory), logs);
         });
        var configured = builder.Configuration.GetSection("Bff").Get<BffSettings>();
        if (!string.IsNullOrWhiteSpace(configured?.KeyCertificatePath)) protection.ProtectKeysWithCertificate(
          X509CertificateLoader.LoadPkcs12FromFile(configured.KeyCertificatePath, configured.KeyCertificatePassword));
        builder.Services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "commerce.bff.csrf"; options.FormFieldName = "__RequestVerificationToken";
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        builder.Services.AddAuthentication(CookieScheme).AddCookie(CookieScheme).AddOpenIdConnect("oidc", _ => { });
        builder.Services.AddSingleton<IAuthenticationSchemeProvider, BffSchemeProvider>();
        builder.Services.AddOptions<CookieAuthenticationOptions>(CookieScheme).Configure<IOptions<BffSettings>, RedisTicketStore>((options, settings, tickets) =>
        {
            options.Cookie.Name = "commerce.bff.session"; options.Cookie.HttpOnly = true; options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.SlidingExpiration = false;
            if (settings.Value.Enabled) options.SessionStore = tickets;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
            options.Events.OnValidatePrincipal = async context =>
       {
           if (!settings.Value.Enabled) { context.RejectPrincipal(); return; }
           var access = context.Properties.GetTokenValue("access_token");
           if (string.IsNullOrEmpty(access) || !await context.HttpContext.RequestServices.GetRequiredService<IdentitySessionClient>()
          .ActiveAsync(access, context.Principal?.FindFirstValue("sub"), context.HttpContext.RequestAborted))
           { context.RejectPrincipal(); await context.HttpContext.SignOutAsync(CookieScheme); }
       };
        });
        builder.Services.AddOptions<OpenIdConnectOptions>("oidc").Configure<IOptions<BffSettings>>((options, settings) =>
        {
            var value = settings.Value; if (!value.Enabled) return;
            options.SignInScheme = CookieScheme; options.Authority = value.Authority; options.ClientId = value.ClientId; options.ClientSecret = value.ClientSecret;
            options.ResponseType = OpenIdConnectResponseType.Code; options.ResponseMode = OpenIdConnectResponseMode.Query; options.UsePkce = true;
            options.Scope.Clear(); options.Scope.Add("openid"); options.SaveTokens = true; options.MapInboundClaims = false; options.UseTokenLifetime = true;
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            options.Backchannel = new HttpClient(new AuthorityHandler(new Uri(value.Authority), new Uri(value.InternalAuthority))) { Timeout = TimeSpan.FromSeconds(10) };
            options.TokenValidationParameters.ValidIssuer = new Uri(value.Authority).AbsoluteUri; options.TokenValidationParameters.NameClaimType = "sub";
            options.NonceCookie.SameSite = SameSiteMode.Lax; options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.NonceCookie.SecurePolicy = options.CorrelationCookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToIdentityProvider = context =>
       { context.ProtocolMessage.RedirectUri = value.PublicAppOrigin.TrimEnd('/') + "/signin-oidc"; context.ProtocolMessage.Prompt = "login"; return Task.CompletedTask; };
            options.Events.OnRedirectToIdentityProviderForSignOut = context =>
       {
           context.ProtocolMessage.IdTokenHint = null; context.ProtocolMessage.ClientId = value.ClientId;
           context.ProtocolMessage.PostLogoutRedirectUri = value.PublicAppOrigin.TrimEnd('/') + "/signout-callback-oidc"; return Task.CompletedTask;
       };
            options.Events.OnTokenValidated = context =>
       {
           var sub = context.Principal?.FindFirstValue("sub");
           if (context.Principal?.Identity is ClaimsIdentity identity && !string.IsNullOrEmpty(sub)) identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, sub));
           return Task.CompletedTask;
       };
            options.Events.OnRemoteFailure = context =>
       { context.HandleResponse(); return Results.Problem(statusCode: 400, title: "Sign-in response could not be validated.").ExecuteAsync(context.HttpContext); };
        });
    }
    public static void MapBff(this WebApplication app)
    {
        app.MapGet("/bff/login", (IOptions<BffSettings> settings) => settings.Value.Enabled
           ? Results.Challenge(new AuthenticationProperties { RedirectUri = settings.Value.PublicAppOrigin.TrimEnd('/') + "/account" }, ["oidc"]) : Results.StatusCode(503)).AllowAnonymous();
        app.MapGet("/bff/session", (HttpContext context, IOptions<BffSettings> settings) => Results.Ok(new
        {
            available = settings.Value.Enabled,
            authenticated = context.User.Identity?.IsAuthenticated == true,
            userId = context.User.Identity?.IsAuthenticated == true ? context.User.FindFirstValue("sub") : null
        })).AllowAnonymous();
        app.MapGet("/bff/antiforgery", (HttpContext context, IAntiforgery antiforgery) => Results.Ok(new { requestToken = antiforgery.GetAndStoreTokens(context).RequestToken })).AllowAnonymous();
        app.MapPost("/bff/logout", async Task<IResult> (HttpContext context, IAntiforgery antiforgery, IOptions<BffSettings> settings) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.Problem(statusCode: 400, title: "Invalid request verification token."); }
            return Results.SignOut(new AuthenticationProperties { RedirectUri = settings.Value.PublicAppOrigin.TrimEnd('/') + "/" }, [CookieScheme, "oidc"]);
        }).RequireAuthorization();
    }
    private static bool ValidOrigin(string value, bool development) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
      (uri.Scheme == "https" || (development && uri.Scheme == "http" && uri.IsLoopback)) && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}
