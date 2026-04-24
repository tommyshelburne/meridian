using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Auth;

namespace Meridian.Application.Auth;

public record OidcConfigSummary(
    Guid Id,
    string ProviderKey,
    OidcProvider Provider,
    string DisplayName,
    string Authority,
    string ClientId,
    string Scopes,
    string EmailClaim,
    string NameClaim,
    bool IsEnabled,
    DateTimeOffset UpdatedAt);

public record ResolvedOidcConfig(
    Guid Id,
    Guid TenantId,
    string ProviderKey,
    OidcProvider Provider,
    string DisplayName,
    string Authority,
    string ClientId,
    string ClientSecret,
    string Scopes,
    string EmailClaim,
    string NameClaim,
    bool IsEnabled);

public record CreateOidcConfigRequest(
    string ProviderKey,
    OidcProvider Provider,
    string DisplayName,
    string Authority,
    string ClientId,
    string ClientSecret,
    string? Scopes = null,
    string? EmailClaim = null,
    string? NameClaim = null);

public record UpdateOidcConfigRequest(
    string DisplayName,
    string Authority,
    string ClientId,
    string? Scopes = null,
    string? EmailClaim = null,
    string? NameClaim = null);

public class OidcConfigService
{
    private readonly IOidcConfigRepository _repo;
    private readonly ISecretProtector _protector;

    public OidcConfigService(IOidcConfigRepository repo, ISecretProtector protector)
    {
        _repo = repo;
        _protector = protector;
    }

    public async Task<IReadOnlyList<OidcConfigSummary>> ListForTenantAsync(
        Guid tenantId, CancellationToken ct)
    {
        var configs = await _repo.GetForTenantAsync(tenantId, ct);
        return configs.Select(ToSummary).ToList();
    }

    public async Task<ResolvedOidcConfig?> ResolveByProviderKeyAsync(
        Guid tenantId, string providerKey, CancellationToken ct)
    {
        var config = await _repo.GetByProviderKeyAsync(tenantId, providerKey, ct);
        if (config is null) return null;

        string clientSecret;
        try
        {
            clientSecret = _protector.Unprotect(config.EncryptedClientSecret);
        }
        catch
        {
            // Stored ciphertext can't be decrypted — most likely the data-protection key
            // ring rotated or the row was seeded with raw text. Surface as null so the
            // caller treats it as "no usable config" rather than crashing the auth flow.
            return null;
        }

        return new ResolvedOidcConfig(
            config.Id, config.TenantId, config.ProviderKey, config.Provider,
            config.DisplayName, config.Authority, config.ClientId, clientSecret,
            config.Scopes, config.EmailClaim, config.NameClaim, config.IsEnabled);
    }

    public async Task<ServiceResult<Guid>> CreateAsync(
        Guid tenantId, CreateOidcConfigRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ClientSecret))
            return ServiceResult<Guid>.Fail("Client secret is required.");

        var existing = await _repo.GetByProviderKeyAsync(tenantId, request.ProviderKey, ct);
        if (existing is not null)
            return ServiceResult<Guid>.Fail($"Provider key '{request.ProviderKey}' already exists for this tenant.");

        try
        {
            var encrypted = _protector.Protect(request.ClientSecret.Trim());
            var config = OidcConfig.Create(
                tenantId, request.ProviderKey, request.Provider,
                request.DisplayName, request.Authority,
                request.ClientId, encrypted,
                request.Scopes, request.EmailClaim, request.NameClaim);

            await _repo.AddAsync(config, ct);
            await _repo.SaveChangesAsync(ct);
            return ServiceResult<Guid>.Ok(config.Id);
        }
        catch (ArgumentException ex)
        {
            return ServiceResult<Guid>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult> UpdateAsync(
        Guid configId, UpdateOidcConfigRequest request, CancellationToken ct)
    {
        var config = await _repo.GetByIdAsync(configId, ct);
        if (config is null) return ServiceResult.Fail("Config not found.");

        try
        {
            config.UpdateDetails(
                request.DisplayName, request.Authority, request.ClientId,
                request.Scopes, request.EmailClaim, request.NameClaim);
            await _repo.SaveChangesAsync(ct);
            return ServiceResult.Ok();
        }
        catch (ArgumentException ex)
        {
            return ServiceResult.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult> RotateSecretAsync(
        Guid configId, string newSecret, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newSecret))
            return ServiceResult.Fail("New secret is required.");

        var config = await _repo.GetByIdAsync(configId, ct);
        if (config is null) return ServiceResult.Fail("Config not found.");

        var encrypted = _protector.Protect(newSecret.Trim());
        config.RotateSecret(encrypted);
        await _repo.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SetEnabledAsync(
        Guid configId, bool isEnabled, CancellationToken ct)
    {
        var config = await _repo.GetByIdAsync(configId, ct);
        if (config is null) return ServiceResult.Fail("Config not found.");

        if (isEnabled) config.Enable();
        else config.Disable();
        await _repo.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteAsync(Guid configId, CancellationToken ct)
    {
        var config = await _repo.GetByIdAsync(configId, ct);
        if (config is null) return ServiceResult.Fail("Config not found.");

        await _repo.RemoveAsync(config, ct);
        await _repo.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    private static OidcConfigSummary ToSummary(OidcConfig c) => new(
        c.Id, c.ProviderKey, c.Provider, c.DisplayName, c.Authority,
        c.ClientId, c.Scopes, c.EmailClaim, c.NameClaim, c.IsEnabled, c.UpdatedAt);
}
