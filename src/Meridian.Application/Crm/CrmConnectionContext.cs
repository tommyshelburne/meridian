using Meridian.Domain.Common;

namespace Meridian.Application.Crm;

// Decrypted, ready-to-use snapshot of a tenant's CrmConnection. Adapters receive
// this rather than the encrypted aggregate so the application layer owns
// secret-protector access and adapters stay stateless strategies.
public record CrmConnectionContext(
    Guid TenantId,
    CrmProvider Provider,
    string AuthToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    string? DefaultPipelineId)
{
    public static CrmConnectionContext None(Guid tenantId) => new(
        tenantId, CrmProvider.None, string.Empty, null, null, null);
}
