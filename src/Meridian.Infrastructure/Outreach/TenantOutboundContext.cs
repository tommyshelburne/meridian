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
    string UnsubscribeBaseUrl,
    int? DailyCap);

public class TenantOutboundContext
{
    private readonly IOutboundConfigurationRepository _repo;
    private readonly ITenantRepository _tenantRepo;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector _protector;
    private TenantOutboundSettings? _cached;
    private bool _loaded;

    public TenantOutboundContext(
        IOutboundConfigurationRepository repo,
        ITenantRepository tenantRepo,
        ITenantContext tenantContext,
        ISecretProtector protector)
    {
        _repo = repo;
        _tenantRepo = tenantRepo;
        _tenantContext = tenantContext;
        _protector = protector;
    }

    public virtual async Task<TenantOutboundSettings?> GetAsync(CancellationToken ct)
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

        var tenant = await _tenantRepo.GetByIdAsync(_tenantContext.TenantId, ct);
        var effectiveReplyTo = OutboundReplyAddress.Compose(
            config.InboundDomain,
            tenant?.Slug ?? string.Empty,
            config.ReplyToAddress);

        _cached = new TenantOutboundSettings(
            config.ProviderType,
            apiKey,
            config.FromAddress,
            config.FromName,
            effectiveReplyTo,
            config.PhysicalAddress,
            config.UnsubscribeBaseUrl,
            config.DailyCap);

        return _cached;
    }
}
