namespace Commerce.Identity.Infrastructure;

public sealed class EmailChallenge
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset ResendAfterUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
}
public sealed class VerificationEmail
{
    public string Kind { get; set; } = "verification";
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string ProtectedPayload { get; set; } = string.Empty;
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public DateTimeOffset? AbandonedAtUtc { get; set; }
    public int Attempts { get; set; }
}
public sealed record VerificationEmailContent(string Recipient, string Link);
