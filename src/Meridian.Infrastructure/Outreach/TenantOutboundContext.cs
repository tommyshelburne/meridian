using Meridian.Application.Ports;
using Meridian.Domain.Outreach;
using Meridian.Domain.Tenants;

namespace Meridian.Infrastructure.Outreach;

public record TenantOutboundSettings(
    OutboundProviderType ProviderType,
    string ApiKey,
    string FromAddress,
    string FromName,
    string? ReplyToAddress,
    string PhysicalAddress,
    string UnsubscribeBaseUrl);

public class TenantOutboundContext
{
    private readonly IOutboundConfigurationRepository _repo;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector _protector;
    private TenantOutboundSettings? _cached;
    private bool _loaded;

    public TenantOutboundContext(
        IOutboundConfigurationRepository repo,
        ITenantContext tenantContext,
        ISecretProtector protector)
    {
        _repo = repo;
        _tenantContext = tenantContext;
        _protector = protector;
    }

    public async Task<TenantOutboundSettings?> GetAsync(CancellationToken ct)
    {
        if (_loaded) return _cached;

        var config = await _repo.GetByTenantIdAsync(_tenantContext.TenantId, ct);
        _loaded = true;

        if (config is null || !config.IsEnabled)
        {
            _cached = null;
            return null;
        }

        var apiKey = string.IsNullOrEmpty(config.EncryptedApiKey)
            ? string.Empty
            : _protector.Unprotect(config.EncryptedApiKey);

        _cached = new TenantOutboundSettings(
            config.ProviderType,
            apiKey,
            config.FromAddress,
            config.FromName,
            config.ReplyToAddress,
            config.PhysicalAddress,
            config.UnsubscribeBaseUrl);

        return _cached;
    }
}
