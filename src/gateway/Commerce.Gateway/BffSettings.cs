namespace Commerce.Gateway;

public sealed class BffSettings
{
    public bool Enabled { get; set; }
    public string Authority { get; set; } = string.Empty;
    public string InternalAuthority { get; set; } = string.Empty;
    public string PublicAppOrigin { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedisConnectionString { get; set; } = string.Empty;
    public string KeyDirectory { get; set; } = string.Empty;
    public string? KeyCertificatePath { get; set; }
    public string? KeyCertificatePassword { get; set; }
}
public sealed class AuthorityHandler(Uri authority, Uri internalAuthority) : DelegatingHandler(new SocketsHttpHandler { AllowAutoRedirect = false })
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new HttpRequestException("OIDC request URI missing.");
        var origin = uri.GetLeftPart(UriPartial.Authority);
        if (origin != authority.GetLeftPart(UriPartial.Authority) && origin != internalAuthority.GetLeftPart(UriPartial.Authority))
            throw new HttpRequestException("OIDC backchannel destination is not approved.");
        request.Headers.Host = authority.Authority;
        request.RequestUri = new Uri(internalAuthority, uri.PathAndQuery); return base.SendAsync(request, cancellationToken);
    }
}
