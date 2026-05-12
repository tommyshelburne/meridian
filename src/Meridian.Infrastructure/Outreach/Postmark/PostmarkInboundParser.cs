using System.Globalization;
using System.Text.Json;
using Meridian.Application.Outreach;

namespace Meridian.Infrastructure.Outreach.Postmark;

public record PostmarkInboundEnvelope(
    string MailboxHash,
    string ToAddress,
    DetectedReply Reply);

public static class PostmarkInboundParser
{
    public static PostmarkInboundEnvelope? Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException) { return null; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var toAddress = ExtractToAddress(root);
            if (string.IsNullOrEmpty(toAddress)) return null;

            var mailboxHash = ExtractMailboxHash(root);

            var from = GetString(root, "From") ?? string.Empty;
            var subject = GetString(root, "Subject") ?? string.Empty;
            var receivedAt = ExtractDate(root);
            var inReplyTo = ExtractInReplyTo(root);
            var textBody = ExtractTextBody(root);

            var reply = new DetectedReply(inReplyTo, subject, receivedAt, from) { Body = textBody };
            return new PostmarkInboundEnvelope(mailboxHash, toAddress, reply);
        }
    }

    private static string ExtractToAddress(JsonElement root)
    {
        if (root.TryGetProperty("ToFull", out var toFull)
            && toFull.ValueKind == JsonValueKind.Array
            && toFull.GetArrayLength() > 0)
        {
            var first = toFull[0];
            var email = GetString(first, "Email");
            if (!string.IsNullOrEmpty(email)) return email;
        }
        return GetString(root, "To") ?? string.Empty;
    }

    private static string ExtractMailboxHash(JsonElement root)
    {
        var top = GetString(root, "MailboxHash");
        if (!string.IsNullOrEmpty(top)) return top;

        if (root.TryGetProperty("ToFull", out var toFull)
            && toFull.ValueKind == JsonValueKind.Array
            && toFull.GetArrayLength() > 0)
        {
            return GetString(toFull[0], "MailboxHash") ?? string.Empty;
        }
        return string.Empty;
    }

    private static DateTimeOffset ExtractDate(JsonElement root)
    {
        var dateStr = GetString(root, "Date");
        if (string.IsNullOrEmpty(dateStr)) return DateTimeOffset.UtcNow;
        return DateTimeOffset.TryParse(
            dateStr, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var ts)
            ? ts
            : DateTimeOffset.UtcNow;
    }

    private static string ExtractInReplyTo(JsonElement root)
    {
        if (!root.TryGetProperty("Headers", out var headers)
            || headers.ValueKind != JsonValueKind.Array)
            return string.Empty;

        foreach (var header in headers.EnumerateArray())
        {
            var name = GetString(header, "Name");
            if (string.Equals(name, "In-Reply-To", StringComparison.OrdinalIgnoreCase))
                return StripAngleBrackets(GetString(header, "Value") ?? string.Empty);
        }

        foreach (var header in headers.EnumerateArray())
        {
            var name = GetString(header, "Name");
            if (string.Equals(name, "References", StringComparison.OrdinalIgnoreCase))
            {
                var value = GetString(header, "Value") ?? string.Empty;
                // References is space-separated chain; the first entry is the message we're replying to.
                var first = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                return StripAngleBrackets(first ?? string.Empty);
            }
        }
        return string.Empty;
    }

    private static string ExtractTextBody(JsonElement root)
    {
        var stripped = GetString(root, "StrippedTextReply");
        if (!string.IsNullOrEmpty(stripped)) return stripped;
        return GetString(root, "TextBody") ?? string.Empty;
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string StripAngleBrackets(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('<') && trimmed.EndsWith('>') && trimmed.Length >= 2)
            return trimmed[1..^1].Trim();
        return trimmed;
    }
}
