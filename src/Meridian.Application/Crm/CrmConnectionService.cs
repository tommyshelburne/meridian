using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Crm;

namespace Meridian.Application.Crm;

public record CrmConnectionSummary(
    Guid Id,
    Guid TenantId,
    CrmProvider Provider,
    string? DefaultPipelineId,
    bool IsActive,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public class CrmConnectionService
{
    private readonly ICrmConnectionRepository _repo;
    private readonly ISecretProtector _protector;

    public CrmConnectionService(ICrmConnectionRepository repo, ISecretProtector protector)
    {
        _repo = repo;
        _protector = protector;
    }

    public async Task<CrmConnectionSummary?> GetSummaryAsync(Guid tenantId, CancellationToken ct)
    {
        var connection = await _repo.GetByTenantIdAsync(tenantId, ct);
        return connection is null ? null : ToSummary(connection);
    }

    public async Task<CrmConnectionContext?> GetContextAsync(Guid tenantId, CancellationToken ct)
    {
        var connection = await _repo.GetByTenantIdAsync(tenantId, ct);
        if (connection is null || !connection.IsActive)
            return null;

        var authToken = _protector.Unprotect(connection.EncryptedAuthToken);
        var refreshToken = connection.EncryptedRefreshToken is null
            ? null
            : _protector.Unprotect(connection.EncryptedRefreshToken);

        return new CrmConnectionContext(
            connection.TenantId,
            connection.Provider,
            authToken,
            refreshToken,
            connection.ExpiresAt,
            connection.DefaultPipelineId);
    }

    public async Task<ServiceResult<Guid>> ConnectAsync(
        Guid tenantId,
        CrmProvider provider,
        string authToken,
        string? refreshToken = null,
        DateTimeOffset? expiresAt = null,
        string? defaultPipelineId = null,
        CancellationToken ct = default)
    {
        if (provider == CrmProvider.None)
            return ServiceResult<Guid>.Fail("Provider is required.");
        if (string.IsNullOrWhiteSpace(authToken))
            return ServiceResult<Guid>.Fail("Auth token is required.");

        var encryptedAuth = _protector.Protect(authToken.Trim());
        var encryptedRefresh = string.IsNullOrWhiteSpace(refreshToken)
            ? null
            : _protector.Protect(refreshToken.Trim());

        var existing = await _repo.GetByTenantIdAsync(tenantId, ct);
        if (existing is null)
        {
            var connection = CrmConnection.Create(
                tenantId, provider, encryptedAuth, encryptedRefresh, expiresAt, defaultPipelineId);
            await _repo.AddAsync(connection, ct);
            await _repo.SaveChangesAsync(ct);
            return ServiceResult<Guid>.Ok(connection.Id);
        }

        if (existing.Provider == provider)
            existing.RotateAuthToken(encryptedAuth, encryptedRefresh, expiresAt);
        else
            existing.ChangeProvider(provider, encryptedAuth, encryptedRefresh, expiresAt);

        if (!string.IsNullOrWhiteSpace(defaultPipelineId))
            existing.SetDefaultPipelineId(defaultPipelineId);
        existing.Activate();

        await _repo.SaveChangesAsync(ct);
        return ServiceResult<Guid>.Ok(existing.Id);
    }

    public async Task<ServiceResult> SetDefaultPipelineIdAsync(
        Guid tenantId, string? pipelineId, CancellationToken ct)
    {
        var connection = await _repo.GetByTenantIdAsync(tenantId, ct);
        if (connection is null)
            return ServiceResult.Fail("No CRM connection configured for this tenant.");

        connection.SetDefaultPipelineId(pipelineId);
        await _repo.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeactivateAsync(Guid tenantId, CancellationToken ct)
    {
        var connection = await _repo.GetByTenantIdAsync(tenantId, ct);
        if (connection is null)
            return ServiceResult.Fail("No CRM connection configured for this tenant.");

        connection.Deactivate();
        await _repo.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    private static CrmConnectionSummary ToSummary(CrmConnection c) => new(
        c.Id, c.TenantId, c.Provider, c.DefaultPipelineId, c.IsActive,
        c.ExpiresAt, c.CreatedAt, c.UpdatedAt);
}
