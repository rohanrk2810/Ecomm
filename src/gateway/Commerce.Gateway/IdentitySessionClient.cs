using System.Text.Json;
using Microsoft.Extensions.Options;
namespace Commerce.Gateway;

public sealed class IdentitySessionClient(IOptions<BffSettings> settings) : IDisposable
{
    private readonly BffSettings options = settings.Value;
    private readonly HttpClient http = new(new AuthorityHandler(new Uri(settings.Value.Authority), new Uri(settings.Value.InternalAuthority))) { Timeout = TimeSpan.FromSeconds(10) };
    public async Task<bool> ActiveAsync(string accessToken, string? subject, bool requireMfa, CancellationToken cancellationToken)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        { ["token"] = accessToken, ["client_id"] = options.ClientId, ["client_secret"] = options.ClientSecret });
        using var response = await http.PostAsync(new Uri(new Uri(options.Authority), "connect/introspect"), body, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Identity session validation unavailable.");
        using var result = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (requireMfa && (!result.RootElement.TryGetProperty("amr", out var methods) || methods.ValueKind != JsonValueKind.Array || !methods.EnumerateArray().Any(x => x.GetString() == "mfa"))) return false;
        return result.RootElement.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True &&
           result.RootElement.TryGetProperty("sub", out var sub) && sub.GetString() == subject;
    }
    public void Dispose() => http.Dispose();
}
