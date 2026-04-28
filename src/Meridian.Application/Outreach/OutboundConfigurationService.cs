using Meridian.Application.Common;
using Meridian.Application.Ports;
using Meridian.Domain.Outreach;

namespace Meridian.Application.Outreach;

public record UpsertOutboundRequest(
    OutboundProviderType ProviderType,
    string? ApiKey,
    string FromAddress,
    string FromName,
    string? ReplyToAddress,
    string PhysicalAddress,
    string UnsubscribeBaseUrl,
    string? WebhookSecret,
    int? DailyCap);

public record OutboundConfigurationSummary(
    OutboundProviderType ProviderType,
    bool HasApiKey,
    string FromAddress,
    string FromName,
    string? ReplyToAddress,
    string PhysicalAddress,
    string UnsubscribeBaseUrl,
    bool HasWebhookSecret,
    int? DailyCap,
    bool IsEnabled,
    DateTimeOffset UpdatedAt);

public class OutboundConfigurationService
{
    private readonly IOutboundConfigurationRepository _repo;
    private readonly ISecretProtector _protector;

    public OutboundConfigurationService(
        IOutboundConfigurationRepository repo,
        ISecretProtector protector)
    {
        _repo = repo;
        _protector = protector;
    }

    public async Task<OutboundConfigurationSummary?> GetSummaryAsync(Guid tenantId, CancellationToken ct)
    {
        var config = await _repo.GetByTenantIdAsync(tenantId, ct);
        if (config is null) return null;

        return new OutboundConfigurationSummary(
            config.ProviderType,
            HasApiKey: !string.IsNullOrEmpty(config.EncryptedApiKey),
            config.FromAddress,
            config.FromName,
            config.ReplyToAddress,
            config.PhysicalAddress,
            config.UnsubscribeBaseUrl,
            HasWebhookSecret: !string.IsNullOrEmpty(config.EncryptedWebhookSecret),
            config.DailyCap,
            config.IsEnabled,
            config.UpdatedAt);
    }

    public async Task<ServiceResult> UpsertAsync(
        Guid tenantId, UpsertOutboundRequest request, CancellationToken ct)
    {
        var existing = await _repo.GetByTenantIdAsync(tenantId, ct);

        // For new Resend configs, require an API key. For updates that switch *to* Resend
        // without supplying one, the existing key only counts if the prior provider was
        // also Resend (otherwise the stored ciphertext is for a different vendor).
        var providingApiKey = !string.IsNullOrWhiteSpace(request.ApiKey);
        var apiKeyAlreadyValid = existing is not null
                                  && existing.ProviderType == request.ProviderType
                                  && !string.IsNullOrEmpty(existing.EncryptedApiKey);

        if (request.ProviderType != OutboundProviderType.Console
            && !providingApiKey
            && !apiKeyAlreadyValid)
            return ServiceResult.Fail("API key is required when switching to this provider.");

        try
        {
            var encryptedKey = providingApiKey
                ? _protector.Protect(request.ApiKey!.Trim())
                : existing?.EncryptedApiKey ?? string.Empty;

            string? encryptedWebhook;
            if (!string.IsNullOrWhiteSpace(request.WebhookSecret))
                encryptedWebhook = _protector.Protect(request.WebhookSecret.Trim());
            else
                encryptedWebhook = existing?.EncryptedWebhookSecret;

            if (existing is null)
            {
                var created = OutboundConfiguration.Create(
                    tenantId, request.ProviderType, encryptedKey,
                    request.FromAddress, request.FromName,
                    request.PhysicalAddress, request.UnsubscribeBaseUrl,
                    request.ReplyToAddress);
                created.SetWebhookSecret(encryptedWebhook);
                created.SetDailyCap(request.DailyCap);
                await _repo.AddAsync(created, ct);
            }
            else
            {
                existing.UpdateProvider(request.ProviderType, encryptedKey);
                existing.UpdateSender(request.FromAddress, request.FromName, request.ReplyToAddress);
                existing.UpdateCompliance(request.PhysicalAddress, request.UnsubscribeBaseUrl);
                existing.SetWebhookSecret(encryptedWebhook);
                existing.SetDailyCap(request.DailyCap);
            }

            await _repo.SaveChangesAsync(ct);
            return ServiceResult.Ok();
        }
        catch (ArgumentException ex)
        {
            return ServiceResult.Fail(ex.Message);
        }
    }
}
