using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Common;
using Meridian.Domain.Crm;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Crm;

public record CrmConnectionSummary(
    Guid Id,
    Guid TenantId,
    CrmProvider Provider,
    string? DefaultPipelineId,
    string? ApiBaseUrl,
    bool IsActive,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public class CrmConnectionService
{
    // How long before ExpiresAt we proactively refresh. Avoids the boundary
    // case where a token is technically valid but expires mid-request.
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(2);

    private readonly ICrmConnectionRepository _repo;
    private readonly ISecretProtector _protector;
    private readonly ICrmOAuthBrokerFactory _brokers;
    private readonly ILogger<CrmConnectionService> _logger;

    public CrmConnectionService(
        ICrmConnectionRepository repo,
        ISecretProtector protector,
        ICrmOAuthBrokerFactory brokers,
        ILogger<CrmConnectionService> logger)
    {
        _repo = repo;
        _protector = protector;
        _brokers = brokers;
        _logger = logger;
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

        if (NeedsRefresh(connection))
        {
            var refreshed = await TryRefreshAsync(connection, ct);
            if (!refreshed)
                return null;
        }

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
            connection.ApiBaseUrl,
            connection.DefaultPipelineId);
    }

    public async Task<ServiceResult<Guid>> ConnectAsync(
        Guid tenantId,
        CrmProvider provider,
        string authToken,
        string? refreshToken = null,
        DateTimeOffset? expiresAt = null,
        string? apiBaseUrl = null,
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
                tenantId, provider, encryptedAuth, encryptedRefresh, expiresAt, apiBaseUrl, defaultPipelineId);
            await _repo.AddAsync(connection, ct);
            await _repo.SaveChangesAsync(ct);
            return ServiceResult<Guid>.Ok(connection.Id);
        }

        if (existing.Provider == provider)
            existing.RotateAuthToken(encryptedAuth, encryptedRefresh, expiresAt, apiBaseUrl);
        else
            existing.ChangeProvider(provider, encryptedAuth, encryptedRefresh, expiresAt, apiBaseUrl);

        if (!string.IsNullOrWhiteSpace(defaultPipelineId))
            existing.SetDefaultPipelineId(defaultPipelineId);
        existing.Activate();

        await _repo.SaveChangesAsync(ct);
        return ServiceResult<Guid>.Ok(existing.Id);
    }

    public Task<ServiceResult<Guid>> ConnectFromTokensAsync(
        Guid tenantId,
        CrmProvider provider,
        OAuthTokens tokens,
        string? defaultPipelineId = null,
        CancellationToken ct = default) =>
        ConnectAsync(
            tenantId, provider,
            tokens.AccessToken, tokens.RefreshToken,
            tokens.ExpiresAt, tokens.ApiBaseUrl,
            defaultPipelineId, ct);

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

    private static bool NeedsRefresh(CrmConnection connection) =>
        connection.ExpiresAt.HasValue
        && connection.EncryptedRefreshToken is not null
        && connection.ExpiresAt.Value - RefreshSkew <= DateTimeOffset.UtcNow;

    private async Task<bool> TryRefreshAsync(CrmConnection connection, CancellationToken ct)
    {
        if (!_brokers.TryResolve(connection.Provider, out var broker))
        {
            _logger.LogWarning(
                "Connection for tenant {TenantId} ({Provider}) is expired but no OAuth broker is registered.",
                connection.TenantId, connection.Provider);
            return false;
        }

        var refreshToken = _protector.Unprotect(connection.EncryptedRefreshToken!);
        var refreshResult = await broker.RefreshAsync(refreshToken, ct);
        if (!refreshResult.IsSuccess)
        {
            _logger.LogWarning(
                "Token refresh failed for tenant {TenantId} ({Provider}): {Error}",
                connection.TenantId, connection.Provider, refreshResult.Error);
            return false;
        }

        var tokens = refreshResult.Value!;
        var encryptedAuth = _protector.Protect(tokens.AccessToken);
        var encryptedRefresh = string.IsNullOrWhiteSpace(tokens.RefreshToken)
            ? null
            : _protector.Protect(tokens.RefreshToken);

        connection.RotateAuthToken(encryptedAuth, encryptedRefresh, tokens.ExpiresAt, tokens.ApiBaseUrl);
        await _repo.SaveChangesAsync(ct);
        return true;
    }

    private static CrmConnectionSummary ToSummary(CrmConnection c) => new(
        c.Id, c.TenantId, c.Provider, c.DefaultPipelineId, c.ApiBaseUrl, c.IsActive,
        c.ExpiresAt, c.CreatedAt, c.UpdatedAt);
}
