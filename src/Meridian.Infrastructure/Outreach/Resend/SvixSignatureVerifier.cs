using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Meridian.Infrastructure.Outreach.Resend;

/// <summary>
/// Verifies Resend (and any other Svix-backed) webhook signatures per
/// https://docs.svix.com/receiving/verifying-payloads/how-manual.
///
/// Signing secret format: "whsec_{base64-encoded-secret-bytes}".
/// Header signature format: "v1,{base64-hmac}" — a single header may contain
/// multiple comma-separated signatures (e.g. during secret rotation); the
/// payload is valid if any of them match.
/// </summary>
public class SvixSignatureVerifier
{
    private const string SecretPrefix = "whsec_";
    private static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    public bool Verify(
        string signingSecret,
        string svixId,
        string svixTimestamp,
        string svixSignatureHeader,
        string body,
        DateTimeOffset now,
        TimeSpan? tolerance = null)
    {
        if (string.IsNullOrEmpty(signingSecret) || string.IsNullOrEmpty(svixId)
            || string.IsNullOrEmpty(svixTimestamp) || string.IsNullOrEmpty(svixSignatureHeader))
            return false;

        if (!TryDecodeSecret(signingSecret, out var keyBytes))
            return false;

        if (!long.TryParse(svixTimestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts))
            return false;

        var sentAt = DateTimeOffset.FromUnixTimeSeconds(ts);
        var window = tolerance ?? DefaultTolerance;
        if (sentAt < now - window || sentAt > now + window)
            return false;

        var signedPayload = $"{svixId}.{svixTimestamp}.{body}";
        using var hmac = new HMACSHA256(keyBytes);
        var expected = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload)));

        foreach (var part in svixSignatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // Each part is "v1,{base64}". We only support v1.
            var commaIdx = part.IndexOf(',');
            if (commaIdx <= 0) continue;
            var version = part[..commaIdx];
            if (!version.Equals("v1", StringComparison.OrdinalIgnoreCase)) continue;

            var provided = part[(commaIdx + 1)..];
            if (FixedTimeEquals(provided, expected))
                return true;
        }

        return false;
    }

    private static bool TryDecodeSecret(string signingSecret, out byte[] bytes)
    {
        var raw = signingSecret.StartsWith(SecretPrefix, StringComparison.Ordinal)
            ? signingSecret[SecretPrefix.Length..]
            : signingSecret;

        try
        {
            bytes = Convert.FromBase64String(raw);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
