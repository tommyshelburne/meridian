using System.Text.Json;
using Meridian.Application.Outreach;

namespace Meridian.Infrastructure.Outreach.Resend;

public static class ResendWebhookParser
{
    /// <summary>
    /// Parses a Resend webhook payload into a BounceEvent if the event type is one we
    /// care about. Returns null for delivered/opened/clicked/sent (no-op for us).
    /// </summary>
    public static BounceEvent? Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp)) return null;
        var type = typeProp.GetString();
        if (string.IsNullOrEmpty(type)) return null;

        var kind = type switch
        {
            "email.bounced" => BounceEventKind.HardBounce,
            "email.complained" => BounceEventKind.SpamComplaint,
            _ => (BounceEventKind?)null
        };
        if (kind is null) return null;

        if (!root.TryGetProperty("data", out var data)) return null;
        var email = ExtractFirstRecipient(data);
        if (string.IsNullOrEmpty(email)) return null;

        // Resend marks transient bounces with bounce.type = "Transient"; downgrade to soft
        if (kind == BounceEventKind.HardBounce
            && data.TryGetProperty("bounce", out var bounce)
            && bounce.TryGetProperty("type", out var bounceType)
            && string.Equals(bounceType.GetString(), "Transient", StringComparison.OrdinalIgnoreCase))
        {
            kind = BounceEventKind.SoftBounce;
        }

        var providerReason = ExtractBounceMessage(data);
        var occurredAt = ExtractCreatedAt(root) ?? DateTimeOffset.UtcNow;

        return new BounceEvent(email, kind.Value, providerReason, occurredAt);
    }

    private static string? ExtractFirstRecipient(JsonElement data)
    {
        if (!data.TryGetProperty("to", out var to)) return null;
        return to.ValueKind switch
        {
            JsonValueKind.Array when to.GetArrayLength() > 0 => to[0].GetString(),
            JsonValueKind.String => to.GetString(),
            _ => null
        };
    }

    private static string? ExtractBounceMessage(JsonElement data)
    {
        if (!data.TryGetProperty("bounce", out var bounce)) return null;
        if (bounce.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
            return msg.GetString();
        if (bounce.TryGetProperty("subType", out var sub) && sub.ValueKind == JsonValueKind.String)
            return sub.GetString();
        return null;
    }

    private static DateTimeOffset? ExtractCreatedAt(JsonElement root)
    {
        if (!root.TryGetProperty("created_at", out var created)) return null;
        if (created.ValueKind != JsonValueKind.String) return null;
        return DateTimeOffset.TryParse(created.GetString(),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var ts) ? ts : null;
    }
}
