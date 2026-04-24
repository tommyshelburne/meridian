namespace Meridian.Domain.Auth;

public class OidcConfig
{
    public const string DefaultScopes = "openid profile email";

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string ProviderKey { get; private set; } = null!;
    public OidcProvider Provider { get; private set; }
    public string DisplayName { get; private set; } = null!;
    public string Authority { get; private set; } = null!;
    public string ClientId { get; private set; } = null!;
    public string EncryptedClientSecret { get; private set; } = null!;
    public string Scopes { get; private set; } = DefaultScopes;
    public string EmailClaim { get; private set; } = "email";
    public string NameClaim { get; private set; } = "name";
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private OidcConfig() { }

    public static OidcConfig Create(
        Guid tenantId,
        string providerKey,
        OidcProvider provider,
        string displayName,
        string authority,
        string clientId,
        string encryptedClientSecret,
        string? scopes = null,
        string? emailClaim = null,
        string? nameClaim = null)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        RequireNotBlank(providerKey, nameof(providerKey));
        RequireNotBlank(displayName, nameof(displayName));
        RequireValidAuthority(authority);
        RequireNotBlank(clientId, nameof(clientId));
        RequireNotBlank(encryptedClientSecret, nameof(encryptedClientSecret));

        var now = DateTimeOffset.UtcNow;
        return new OidcConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProviderKey = providerKey.Trim().ToLowerInvariant(),
            Provider = provider,
            DisplayName = displayName.Trim(),
            Authority = authority.Trim().TrimEnd('/'),
            ClientId = clientId.Trim(),
            EncryptedClientSecret = encryptedClientSecret,
            Scopes = string.IsNullOrWhiteSpace(scopes) ? DefaultScopes : scopes.Trim(),
            EmailClaim = string.IsNullOrWhiteSpace(emailClaim) ? "email" : emailClaim!.Trim(),
            NameClaim = string.IsNullOrWhiteSpace(nameClaim) ? "name" : nameClaim!.Trim(),
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void UpdateDetails(string displayName, string authority, string clientId, string? scopes,
        string? emailClaim, string? nameClaim)
    {
        RequireNotBlank(displayName, nameof(displayName));
        RequireValidAuthority(authority);
        RequireNotBlank(clientId, nameof(clientId));

        DisplayName = displayName.Trim();
        Authority = authority.Trim().TrimEnd('/');
        ClientId = clientId.Trim();
        Scopes = string.IsNullOrWhiteSpace(scopes) ? DefaultScopes : scopes!.Trim();
        EmailClaim = string.IsNullOrWhiteSpace(emailClaim) ? "email" : emailClaim!.Trim();
        NameClaim = string.IsNullOrWhiteSpace(nameClaim) ? "name" : nameClaim!.Trim();
        Touch();
    }

    public void RotateSecret(string newEncryptedSecret)
    {
        RequireNotBlank(newEncryptedSecret, nameof(newEncryptedSecret));
        EncryptedClientSecret = newEncryptedSecret;
        Touch();
    }

    public void Enable()
    {
        IsEnabled = true;
        Touch();
    }

    public void Disable()
    {
        IsEnabled = false;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    private static void RequireNotBlank(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} is required.", paramName);
    }

    private static void RequireValidAuthority(string authority)
    {
        if (string.IsNullOrWhiteSpace(authority))
            throw new ArgumentException("Authority is required.", nameof(authority));
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Authority must be an absolute http(s) URL.", nameof(authority));
    }
}
