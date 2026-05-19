namespace Meridian.Domain.Outreach;

public static class OutboundReplyAddress
{
    public const string LocalPart = "replies";

    public static string? Compose(string? inboundDomain, string tenantSlug, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(inboundDomain))
            return string.IsNullOrWhiteSpace(fallback) ? null : fallback;

        if (string.IsNullOrWhiteSpace(tenantSlug))
            return string.IsNullOrWhiteSpace(fallback) ? null : fallback;

        return $"{LocalPart}+{tenantSlug}@{inboundDomain.Trim()}";
    }

    public static bool IsValidDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        var trimmed = domain.Trim();
        if (trimmed.Length > 253) return false;
        if (trimmed.Contains('@') || trimmed.Contains(' ')) return false;
        if (!trimmed.Contains('.')) return false;
        if (trimmed.StartsWith('.') || trimmed.EndsWith('.')) return false;
        return true;
    }
}
