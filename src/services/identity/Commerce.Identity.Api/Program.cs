using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Commerce.Identity.Application;
using Commerce.Identity.Contracts;
using Commerce.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddOpenApi();
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
        ?? throw new InvalidOperationException("Inject the Identity service database connection.")));
builder.Services.AddIdentityCore<Account>().AddEntityFrameworkStores<IdentityDatabase>();
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
        tracing.AddAspNetCoreInstrumentation();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])) tracing.AddOtlpExporter();
    }).WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])) metrics.AddOtlpExporter();
    });
var app = builder.Build();
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
app.MapFallback(() => Results.Problem(statusCode: 401, title: "Authentication is not available for this operation."));
app.Run();
public partial class Program;
