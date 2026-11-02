using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
namespace Commerce.Gateway;

public sealed class BffReadiness(IDistributedCache cache, IOptions<BffSettings> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) return HealthCheckResult.Healthy();
        try { await cache.GetAsync("commerce:bff:v1:health", cancellationToken); return HealthCheckResult.Healthy(); }
        catch (Exception error) when (error is RedisException or TimeoutException) { return HealthCheckResult.Unhealthy("Session cache unavailable."); }
    }
}
public sealed class BffDependencyExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (context.RequestAborted.IsCancellationRequested || exception is not (RedisException or HttpRequestException or TimeoutException or TaskCanceledException)) return false;
        context.Response.StatusCode = 503;
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = 503, Title = "Session service is temporarily unavailable." }
        });
        return true;
    }
}
