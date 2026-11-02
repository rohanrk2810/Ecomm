using System.ComponentModel.DataAnnotations;
using Commerce.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Commerce.Identity.Application;

public sealed class RegistrationSettings
{
    public int MinimumPasswordLength { get; set; } = 12;
    public int MaximumPasswordLength { get; set; } = 128;
    public bool RequireDigit { get; set; } = true;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireNonAlphanumeric { get; set; } = true;
    public int RequestsPerMinute { get; set; } = 20;
}
public enum RegistrationOutcome { Accepted, InvalidEmail, InvalidPassword }
public interface IAccountRegistrationStore
{
    Task RegisterAsync(string email, string password, string correlationId, CancellationToken cancellationToken);
}
public sealed class RegistrationService(IAccountRegistrationStore store, IOptions<RegistrationSettings> options)
{
    public async Task<RegistrationOutcome> RegisterAsync(RegistrationRequest request, string correlationId,
        CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim();
        if (string.IsNullOrEmpty(email) || email.Length > 254 || email.Any(char.IsControl) ||
            !new EmailAddressAttribute().IsValid(email)) return RegistrationOutcome.InvalidEmail;
        var password = request.Password;
        var rules = options.Value;
        if (string.IsNullOrEmpty(password) || password.Length < rules.MinimumPasswordLength ||
            password.Length > rules.MaximumPasswordLength ||
            (rules.RequireDigit && !password.Any(char.IsDigit)) ||
            (rules.RequireUppercase && !password.Any(char.IsUpper)) ||
            (rules.RequireLowercase && !password.Any(char.IsLower)) ||
            (rules.RequireNonAlphanumeric && password.All(char.IsLetterOrDigit)))
            return RegistrationOutcome.InvalidPassword;
        await store.RegisterAsync(email, password, correlationId, cancellationToken);
        return RegistrationOutcome.Accepted;
    }
}
