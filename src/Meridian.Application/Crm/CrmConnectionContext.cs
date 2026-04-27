using Meridian.Domain.Common;

namespace Meridian.Application.Crm;

// Decrypted, ready-to-use snapshot of a tenant's CrmConnection. Adapters receive
// this rather than the encrypted aggregate so the application layer owns
// secret-protector access and adapters stay stateless strategies.
//
// ApiBaseUrl is the per-tenant CRM instance URL (Pipedrive's `api_domain`,
// Salesforce's `instance_url`, etc.) — null for providers with a fixed host.
public record CrmConnectionContext(
    Guid TenantId,
    CrmProvider Provider,
    string AuthToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    string? ApiBaseUrl,
    string? DefaultPipelineId)
{
    public static CrmConnectionContext None(Guid tenantId) => new(
        tenantId, CrmProvider.None, string.Empty, null, null, null, null);
}
