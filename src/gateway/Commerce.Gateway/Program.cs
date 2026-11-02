using Commerce.Gateway;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck("gateway", () => HealthCheckResult.Healthy(), tags: ["ready"]);
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Proxy routes require a validated BFF session unless explicitly marked anonymous.
builder.AddBff();
builder.Services.AddAuthorization(options => options.FallbackPolicy =
    new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().Build());
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("commerce.gateway"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation(options => options.Filter = context =>
            !context.Request.Path.StartsWithSegments("/bff") && context.Request.Path != "/signin-oidc" && context.Request.Path != "/signout-callback-oidc")
            .AddHttpClientInstrumentation(options => options.FilterHttpRequestMessage = request =>
                request.RequestUri is not null && !request.RequestUri.AbsolutePath.StartsWith("/connect/", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            tracing.AddOtlpExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            metrics.AddOtlpExporter();
        }
    });
builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeScopes = true;
    if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
    {
        options.AddOtlpExporter();
    }
});

var app = builder.Build();
app.UseMiddleware<CorrelationMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/bff") || context.Request.Path == "/signin-oidc" || context.Request.Path == "/signout-callback-oidc")
        context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.MapBff();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();
app.MapGet("/api/v1/platform/status", () => new PlatformStatus("foundation", false))
    .AllowAnonymous().WithName("GetPlatformStatus");
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}
app.MapReverseProxy();
app.Run();

public sealed record PlatformStatus(string Stage, bool CommerceAvailable);
public partial class Program;
