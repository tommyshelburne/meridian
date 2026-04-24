namespace Meridian.Application.Auth;

public static class OidcSchemeNames
{
    public const string Prefix = "oidc:";

    public static string Format(Guid tenantId, string providerKey)
        => $"{Prefix}{tenantId:D}:{providerKey.Trim().ToLowerInvariant()}";

    public static bool TryParse(string? scheme, out Guid tenantId, out string providerKey)
    {
        tenantId = Guid.Empty;
        providerKey = string.Empty;
        if (string.IsNullOrEmpty(scheme) || !scheme.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var parts = scheme[Prefix.Length..].Split(':');
        if (parts.Length != 2) return false;
        if (!Guid.TryParse(parts[0], out tenantId)) return false;
        if (string.IsNullOrWhiteSpace(parts[1])) return false;

        providerKey = parts[1];
        return true;
    }
}
