using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Commerce.Identity.Api;
using Commerce.Identity.Application;
using Commerce.Identity.Contracts;
using Commerce.Identity.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
if (args.Contains("--prepare-oidc", StringComparer.Ordinal))
{
    if (!builder.Environment.IsDevelopment()) throw new InvalidOperationException("Certificate generation is development-only.");
    DevelopmentOidcCertificates.Create(builder.Configuration.GetSection("Oidc").Get<OidcSettings>()
        ?? throw new InvalidOperationException("Inject local OIDC configuration."));
    return;
}
builder.AddCustomerOidc();
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
builder.Logging.AddFilter("OpenIddict", LogLevel.Warning);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddOpenApi();
builder.Services.AddOptions<VerificationSettings>().BindConfiguration("Verification")
    .Validate(x => x.LifetimeMinutes is >= 5 and <= 1440 && x.ResendCooldownSeconds is >= 30 and <= 3600, "Invalid verification lifetime or cooldown")
    .Validate(x => Uri.TryCreate(x.StorefrontBaseUri, UriKind.Absolute, out var uri) &&
        (uri.Scheme == "https" || (builder.Environment.IsDevelopment() && uri.Scheme == "http" && uri.IsLoopback)) &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment), "Configure an approved storefront origin")
    .Validate(x => !string.IsNullOrWhiteSpace(x.KeyDirectory) && (builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(x.KeyCertificatePath)), "Persistent protected key configuration required")
    .ValidateOnStart();
builder.Services.AddOptions<VerificationDeliverySettings>().BindConfiguration("VerificationDelivery")
    .Validate(x => !x.Enabled || (!string.IsNullOrWhiteSpace(x.Host) && x.Port is > 0 and <= 65535 && MimeKit.MailboxAddress.TryParse(x.FromAddress, out _)), "Configure verification SMTP delivery")
    .Validate(x => builder.Environment.IsDevelopment() || x.StartTls, "SMTP TLS is required outside development")
    .Validate(x => string.IsNullOrEmpty(x.Username) || !string.IsNullOrEmpty(x.Password), "SMTP credentials are incomplete")
    .Validate(x => x.MaxAttempts is >= 1 and <= 10, "Invalid delivery attempt limit").ValidateOnStart();
var protection = builder.Services.AddDataProtection().SetApplicationName("Commerce.Identity");
builder.Services.AddOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>()
    .Configure<Microsoft.Extensions.Options.IOptions<VerificationSettings>, ILoggerFactory>((options, configured, logs) =>
    {
        options.XmlRepository = new Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository(
            new DirectoryInfo(configured.Value.KeyDirectory), logs);
    });
var verificationConfiguration = builder.Configuration.GetSection("Verification").Get<VerificationSettings>() ?? new();
if (!string.IsNullOrWhiteSpace(verificationConfiguration.KeyCertificatePath))
    protection.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(verificationConfiguration.KeyCertificatePath, verificationConfiguration.KeyCertificatePassword));
builder.Services.AddOptions<DataProtectionTokenProviderOptions>().Configure<Microsoft.Extensions.Options.IOptions<VerificationSettings>>(
    (options, configured) => options.TokenLifespan = TimeSpan.FromMinutes(configured.Value.LifetimeMinutes));
builder.Services.AddOptions<RegistrationSettings>().BindConfiguration("Registration")
    .Validate(x => x.MinimumPasswordLength >= 12 && x.MaximumPasswordLength <= 128 &&
        x.MaximumPasswordLength >= x.MinimumPasswordLength && x.RequestsPerMinute > 0, "Invalid registration settings")
    .ValidateOnStart();
builder.Services.AddOptions<MessagingSettings>().BindConfiguration("Messaging")
    .Validate(x => !x.PublisherEnabled || (!string.IsNullOrWhiteSpace(x.BootstrapServers) &&
        !string.IsNullOrWhiteSpace(x.RegistrationTopic)), "Kafka settings are required")
    .Validate(x => x.PollIntervalMilliseconds >= 100, "Polling interval too short").ValidateOnStart();
builder.Services.AddDbContext<IdentityDatabase>((services, options) => options.UseNpgsql(
    services.GetRequiredService<IConfiguration>().GetConnectionString("Identity")
        ?? throw new InvalidOperationException("Inject the Identity service database connection.")).UseOpenIddict());
builder.Services.AddIdentityCore<Account>().AddEntityFrameworkStores<IdentityDatabase>().AddDefaultTokenProviders().AddSignInManager();
builder.Services.AddOptions<IdentityOptions>().Configure<Microsoft.Extensions.Options.IOptions<RegistrationSettings>>((options, registration) =>
{
    var security = registration.Value;
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = security.MinimumPasswordLength;
    options.Password.RequiredUniqueChars = 1;
    options.Password.RequireDigit = security.RequireDigit;
    options.Password.RequireUppercase = security.RequireUppercase;
    options.Password.RequireLowercase = security.RequireLowercase;
    options.Password.RequireNonAlphanumeric = security.RequireNonAlphanumeric;
    options.SignIn.RequireConfirmedEmail = true;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAccountRegistrationStore, AccountRegistrationStore>();
builder.Services.AddScoped<RegistrationService>();
builder.Services.AddScoped<VerificationIssuer>();
builder.Services.AddScoped<PasswordRecovery>();
builder.Services.AddScoped<MfaGate>();
builder.Services.AddOptions<PasswordRecoverySettings>().BindConfiguration("PasswordRecovery").Validate(x => x.LifetimeMinutes is >= 5 and <= 30 && x.CooldownSeconds is >= 30 and <= 3600, "Invalid password recovery policy").ValidateOnStart();
builder.Services.AddScoped<IEmailVerificationStore, EmailVerificationStore>();
builder.Services.AddScoped<VerificationWorkflow>();
builder.Services.AddHostedService<VerificationEmailDispatcher>();
builder.Services.AddHostedService<OutboxPublisher>();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 8192);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("registration", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<RegistrationSettings>>().Value.RequestsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});
builder.Services.AddOpenTelemetry().ConfigureResource(resource => resource.AddService("commerce.identity"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation(options => options.Filter = context =>
            !context.Request.Path.StartsWithSegments("/connect") && !context.Request.Path.StartsWithSegments("/account"));
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])) tracing.AddOtlpExporter();
    }).WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])) metrics.AddOtlpExporter();
    });
var app = builder.Build();
if (args.Contains("--provision-oidc", StringComparer.Ordinal))
{
    using var scope = app.Services.CreateScope();
    await OidcConfiguration.ProvisionClientAsync(scope.ServiceProvider, CancellationToken.None);
    return;
}
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<IdentityDatabase>().Database.MigrateAsync();
    return;
}
app.Use(async (context, next) =>
{
    context.TraceIdentifier = Guid.NewGuid().ToString("N");
    context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'none'; frame-ancestors 'none'; base-uri 'self'; object-src 'none'";
    using var scope = app.Logger.BeginScope(new Dictionary<string, object>
    {
        ["Service"] = "commerce.identity",
        ["CorrelationId"] = context.TraceIdentifier
    });
    await next(context);
});
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages(); app.MapControllers();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/api/v1/identity/registration-policy", (Microsoft.Extensions.Options.IOptions<RegistrationSettings> settings) =>
{
    var policy = settings.Value;
    return new RegistrationPolicy(policy.MinimumPasswordLength, policy.MaximumPasswordLength,
        policy.RequireDigit, policy.RequireUppercase, policy.RequireLowercase, policy.RequireNonAlphanumeric);
}).WithName("GetRegistrationPolicy");
app.MapGet("/health/ready", async (IdentityDatabase db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken) &&
    !(await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any()
        ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapPost("/api/v1/identity/registrations", async (RegistrationRequest request,
    RegistrationService service, HttpContext context, CancellationToken cancellationToken) =>
{
    var result = await service.RegisterAsync(request, context.TraceIdentifier, cancellationToken);
    return result switch
    {
        RegistrationOutcome.InvalidEmail => Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Enter a valid email address."] }),
        RegistrationOutcome.InvalidPassword => Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = ["Password does not meet the configured security requirements."] }),
        _ => Results.Json(new RegistrationResponse("pending_verification"), statusCode: 202)
    };
}).RequireRateLimiting("registration").WithName("RegisterCustomer")
  .Produces<RegistrationResponse>(202).ProducesValidationProblem();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapPost("/api/v1/identity/email-verifications", async (ConfirmEmailRequest request, VerificationWorkflow workflow,
    HttpContext context, CancellationToken cancellationToken) =>
    await workflow.ConfirmAsync(request, context.TraceIdentifier, cancellationToken)
        ? Results.Ok(new RegistrationResponse("verified"))
        : Results.Problem(statusCode: 400, title: "This verification link is invalid or has expired."))
    .RequireRateLimiting("registration").WithName("ConfirmCustomerEmail");
app.MapPost("/api/v1/identity/email-verification-requests", async (ResendEmailRequest request, VerificationWorkflow workflow,
    CancellationToken cancellationToken) =>
    {
        await workflow.ResendAsync(request, cancellationToken);
        return Results.Json(new RegistrationResponse("pending_verification"), statusCode: 202);
    }).RequireRateLimiting("registration").WithName("RequestCustomerEmailVerification");
app.MapFallback(() => Results.Problem(statusCode: 401, title: "Authentication is not available for this operation."));
app.MapPost("/api/v1/identity/password-reset-requests", async (PasswordResetRequest request, PasswordRecovery recovery, CancellationToken ct) =>
{
    await recovery.RequestAsync(request.Email, ct); return Results.Json(new { status = "request_received" }, statusCode: 202);
}).RequireRateLimiting("registration");
app.MapPost("/api/v1/identity/password-resets", async (PasswordReset request, PasswordRecovery recovery, HttpContext context, CancellationToken ct) =>
    await recovery.ResetAsync(request, context.TraceIdentifier, ct) ? Results.Ok(new { status = "password_reset" }) : Results.Problem(statusCode: 400, title: "Reset link or password is invalid. Check the password policy or request a new link."))
    .RequireRateLimiting("registration");
app.Run();
public partial class Program;
