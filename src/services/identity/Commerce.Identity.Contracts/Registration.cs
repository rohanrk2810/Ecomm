namespace Commerce.Identity.Contracts;

public sealed record RegistrationRequest(string? Email, string? Password);
public sealed record RegistrationResponse(string Status);
public sealed record RegistrationPolicy(int MinimumPasswordLength, int MaximumPasswordLength,
    bool RequireDigit, bool RequireUppercase, bool RequireLowercase, bool RequireNonAlphanumeric);
public sealed record UserRegistered(Guid UserId);
public sealed record EventEnvelope(Guid EventId, string Type, int Version, DateTimeOffset OccurredAtUtc,
    string Producer, string AggregateId, long AggregateVersion, string CorrelationId,
    string CausationId, string? TraceParent, UserRegistered Data);
