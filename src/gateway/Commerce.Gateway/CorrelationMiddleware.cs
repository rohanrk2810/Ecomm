namespace Commerce.Gateway;

public sealed class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Untrusted caller headers never become log identifiers. W3C trace
        // propagation is handled by ASP.NET Core/OpenTelemetry independently.
        var correlationId = Guid.NewGuid().ToString("N");
        context.TraceIdentifier = correlationId;
        context.Request.Headers["X-Correlation-ID"] = correlationId;
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }
}
