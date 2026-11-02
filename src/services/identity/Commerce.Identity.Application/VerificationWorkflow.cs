using System.ComponentModel.DataAnnotations;
using Commerce.Identity.Contracts;
namespace Commerce.Identity.Application;

public sealed class VerificationSettings
{
    public int LifetimeMinutes { get; set; } = 30;
    public int ResendCooldownSeconds { get; set; } = 60;
    public string StorefrontBaseUri { get; set; } = string.Empty;
    public string KeyDirectory { get; set; } = string.Empty;
    public string? KeyCertificatePath { get; set; }
    public string? KeyCertificatePassword { get; set; }
}
public interface IEmailVerificationStore
{
    Task<bool> ConfirmAsync(Guid userId, string token, string correlationId, CancellationToken cancellationToken);
    Task ResendAsync(string email, CancellationToken cancellationToken);
}
public sealed class VerificationWorkflow(IEmailVerificationStore store)
{
    public Task<bool> ConfirmAsync(ConfirmEmailRequest request, string correlationId, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty || string.IsNullOrEmpty(request.Token) || request.Token.Length > 4096 ||
            request.Token.Any(x => !char.IsAsciiLetterOrDigit(x) && x != '-' && x != '_')) return Task.FromResult(false);
        return store.ConfirmAsync(request.UserId, request.Token, correlationId, cancellationToken);
    }
    public async Task ResendAsync(ResendEmailRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim();
        // Deliberately return the same response for missing, malformed, confirmed or unknown addresses.
        if (string.IsNullOrEmpty(email) || email.Length > 254 || !new EmailAddressAttribute().IsValid(email)) return;
        await store.ResendAsync(email, cancellationToken);
    }
}
