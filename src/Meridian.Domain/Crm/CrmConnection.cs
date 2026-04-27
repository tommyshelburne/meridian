using Meridian.Domain.Common;

namespace Meridian.Domain.Crm;

public class CrmConnection
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public CrmProvider Provider { get; private set; }
    public string EncryptedAuthToken { get; private set; } = string.Empty;
    public string? EncryptedRefreshToken { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public string? ApiBaseUrl { get; private set; }
    public string? DefaultPipelineId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private CrmConnection() { }

    public static CrmConnection Create(
        Guid tenantId,
        CrmProvider provider,
        string encryptedAuthToken,
        string? encryptedRefreshToken = null,
        DateTimeOffset? expiresAt = null,
        string? apiBaseUrl = null,
        string? defaultPipelineId = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (provider == CrmProvider.None)
            throw new ArgumentException("CrmProvider.None is not a valid connection provider.", nameof(provider));
        if (string.IsNullOrWhiteSpace(encryptedAuthToken))
            throw new ArgumentException("Auth token is required.", nameof(encryptedAuthToken));

        var now = DateTimeOffset.UtcNow;
        return new CrmConnection
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = provider,
            EncryptedAuthToken = encryptedAuthToken,
            EncryptedRefreshToken = NormalizeNullable(encryptedRefreshToken),
            ExpiresAt = expiresAt,
            ApiBaseUrl = NormalizeNullable(apiBaseUrl),
            DefaultPipelineId = NormalizeNullable(defaultPipelineId),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void RotateAuthToken(
        string encryptedAuthToken,
        string? encryptedRefreshToken = null,
        DateTimeOffset? expiresAt = null,
        string? apiBaseUrl = null)
    {
        if (string.IsNullOrWhiteSpace(encryptedAuthToken))
            throw new ArgumentException("Auth token is required.", nameof(encryptedAuthToken));

        EncryptedAuthToken = encryptedAuthToken;
        EncryptedRefreshToken = NormalizeNullable(encryptedRefreshToken);
        ExpiresAt = expiresAt;
        if (apiBaseUrl is not null)
            ApiBaseUrl = NormalizeNullable(apiBaseUrl);
        Touch();
    }

    public void ChangeProvider(
        CrmProvider provider,
        string encryptedAuthToken,
        string? encryptedRefreshToken = null,
        DateTimeOffset? expiresAt = null,
        string? apiBaseUrl = null)
    {
        if (provider == CrmProvider.None)
            throw new ArgumentException("CrmProvider.None is not a valid connection provider.", nameof(provider));
        if (string.IsNullOrWhiteSpace(encryptedAuthToken))
            throw new ArgumentException("Auth token is required.", nameof(encryptedAuthToken));

        Provider = provider;
        EncryptedAuthToken = encryptedAuthToken;
        EncryptedRefreshToken = NormalizeNullable(encryptedRefreshToken);
        ExpiresAt = expiresAt;
        ApiBaseUrl = NormalizeNullable(apiBaseUrl);
        DefaultPipelineId = null;
        Touch();
    }

    public void SetDefaultPipelineId(string? pipelineId)
    {
        DefaultPipelineId = string.IsNullOrWhiteSpace(pipelineId) ? null : pipelineId.Trim();
        Touch();
    }

    public void Activate() { IsActive = true; Touch(); }
    public void Deactivate() { IsActive = false; Touch(); }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt.HasValue && ExpiresAt.Value <= now;

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
