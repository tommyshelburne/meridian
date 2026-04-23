using System.Net.Mail;

namespace Meridian.Domain.Outreach;

public class OutboundConfiguration
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public OutboundProviderType ProviderType { get; private set; }
    public string EncryptedApiKey { get; private set; } = string.Empty;
    public string FromAddress { get; private set; } = string.Empty;
    public string FromName { get; private set; } = string.Empty;
    public string? ReplyToAddress { get; private set; }
    public string PhysicalAddress { get; private set; } = string.Empty;
    public string UnsubscribeBaseUrl { get; private set; } = string.Empty;
    public string? EncryptedWebhookSecret { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private OutboundConfiguration() { }

    public static OutboundConfiguration Create(
        Guid tenantId,
        OutboundProviderType providerType,
        string encryptedApiKey,
        string fromAddress,
        string fromName,
        string physicalAddress,
        string unsubscribeBaseUrl,
        string? replyToAddress = null)
    {
        ValidateAddress(fromAddress, nameof(fromAddress));
        if (replyToAddress is not null) ValidateAddress(replyToAddress, nameof(replyToAddress));
        if (string.IsNullOrWhiteSpace(fromName))
            throw new ArgumentException("FromName is required.", nameof(fromName));
        if (string.IsNullOrWhiteSpace(physicalAddress))
            throw new ArgumentException("PhysicalAddress is required for CAN-SPAM compliance.", nameof(physicalAddress));
        if (!Uri.TryCreate(unsubscribeBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("UnsubscribeBaseUrl must be an absolute http(s) URL.", nameof(unsubscribeBaseUrl));
        if (providerType != OutboundProviderType.Console && string.IsNullOrWhiteSpace(encryptedApiKey))
            throw new ArgumentException("API key is required for non-Console providers.", nameof(encryptedApiKey));

        var now = DateTimeOffset.UtcNow;
        return new OutboundConfiguration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProviderType = providerType,
            EncryptedApiKey = encryptedApiKey ?? string.Empty,
            FromAddress = fromAddress.Trim(),
            FromName = fromName.Trim(),
            ReplyToAddress = replyToAddress?.Trim(),
            PhysicalAddress = physicalAddress.Trim(),
            UnsubscribeBaseUrl = unsubscribeBaseUrl.Trim(),
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void UpdateProvider(OutboundProviderType providerType, string encryptedApiKey)
    {
        if (providerType != OutboundProviderType.Console && string.IsNullOrWhiteSpace(encryptedApiKey))
            throw new ArgumentException("API key is required for non-Console providers.", nameof(encryptedApiKey));

        ProviderType = providerType;
        EncryptedApiKey = encryptedApiKey ?? string.Empty;
        Touch();
    }

    public void SetWebhookSecret(string? encryptedSecret)
    {
        EncryptedWebhookSecret = string.IsNullOrWhiteSpace(encryptedSecret) ? null : encryptedSecret;
        Touch();
    }

    public void UpdateSender(string fromAddress, string fromName, string? replyToAddress = null)
    {
        ValidateAddress(fromAddress, nameof(fromAddress));
        if (replyToAddress is not null) ValidateAddress(replyToAddress, nameof(replyToAddress));
        if (string.IsNullOrWhiteSpace(fromName))
            throw new ArgumentException("FromName is required.", nameof(fromName));

        FromAddress = fromAddress.Trim();
        FromName = fromName.Trim();
        ReplyToAddress = replyToAddress?.Trim();
        Touch();
    }

    public void UpdateCompliance(string physicalAddress, string unsubscribeBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(physicalAddress))
            throw new ArgumentException("PhysicalAddress is required for CAN-SPAM compliance.", nameof(physicalAddress));
        if (!Uri.TryCreate(unsubscribeBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("UnsubscribeBaseUrl must be an absolute http(s) URL.", nameof(unsubscribeBaseUrl));

        PhysicalAddress = physicalAddress.Trim();
        UnsubscribeBaseUrl = unsubscribeBaseUrl.Trim();
        Touch();
    }

    public void Disable() { IsEnabled = false; Touch(); }
    public void Enable() { IsEnabled = true; Touch(); }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    private static void ValidateAddress(string address, string paramName)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Email address is required.", paramName);
        try
        {
            _ = new MailAddress(address);
        }
        catch (FormatException)
        {
            throw new ArgumentException($"'{address}' is not a valid email address.", paramName);
        }
    }
}
