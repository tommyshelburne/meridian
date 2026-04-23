using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Infrastructure.Outreach.Resend;

namespace Meridian.Unit.Infrastructure.Outreach.Resend;

public class SvixSignatureVerifierTests
{
    private readonly SvixSignatureVerifier _verifier = new();
    // 32 bytes -> "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=" (43-char base64); use a real one
    private static readonly byte[] SecretBytes = Convert.FromBase64String("dGVzdC1zZWNyZXQtZm9yLW1lcmlkaWFuLXdlYmhvb2tzMTIz");
    private static readonly string Secret = "whsec_" + Convert.ToBase64String(SecretBytes);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-04-22T15:00:00Z");

    private static (string id, string ts, string sig) Sign(string body, byte[] keyBytes, DateTimeOffset at)
    {
        var id = "msg_2gZQF1tQqBaQ7";
        var ts = at.ToUnixTimeSeconds().ToString();
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{id}.{ts}.{body}"));
        var sig = "v1," + Convert.ToBase64String(hash);
        return (id, ts, sig);
    }

    [Fact]
    public void Valid_signature_within_window_passes()
    {
        var body = "{\"type\":\"email.bounced\"}";
        var (id, ts, sig) = Sign(body, SecretBytes, Now);

        _verifier.Verify(Secret, id, ts, sig, body, Now).Should().BeTrue();
    }

    [Fact]
    public void Tampered_body_fails()
    {
        var body = "{\"type\":\"email.bounced\"}";
        var (id, ts, sig) = Sign(body, SecretBytes, Now);

        _verifier.Verify(Secret, id, ts, sig, body + "tamper", Now).Should().BeFalse();
    }

    [Fact]
    public void Wrong_secret_fails()
    {
        var body = "{\"x\":1}";
        var (id, ts, sig) = Sign(body, SecretBytes, Now);

        var otherKey = Convert.FromBase64String("b3RoZXItc2VjcmV0LWJ5dGVzLWZvci10ZXN0aW5nLW9ubHkx");
        var otherSecret = "whsec_" + Convert.ToBase64String(otherKey);
        _verifier.Verify(otherSecret, id, ts, sig, body, Now).Should().BeFalse();
    }

    [Fact]
    public void Outside_tolerance_window_fails()
    {
        var body = "{\"x\":1}";
        var staleAt = Now.AddMinutes(-10);
        var (id, ts, sig) = Sign(body, SecretBytes, staleAt);

        _verifier.Verify(Secret, id, ts, sig, body, Now).Should().BeFalse();
    }

    [Fact]
    public void Multiple_signatures_in_header_during_rotation_match_any()
    {
        var body = "{\"x\":1}";
        var (id, ts, validSig) = Sign(body, SecretBytes, Now);
        var combined = $"v1,bogus {validSig}";

        _verifier.Verify(Secret, id, ts, combined, body, Now).Should().BeTrue();
    }

    [Fact]
    public void Unknown_signature_version_is_rejected()
    {
        var body = "{\"x\":1}";
        var (id, ts, _) = Sign(body, SecretBytes, Now);
        var v2Header = "v2,abcd1234";

        _verifier.Verify(Secret, id, ts, v2Header, body, Now).Should().BeFalse();
    }

    [Theory]
    [InlineData("", "ts", "sig", "body")]
    [InlineData("id", "", "sig", "body")]
    [InlineData("id", "ts", "", "body")]
    public void Missing_required_inputs_fail(string id, string ts, string sig, string body)
    {
        _verifier.Verify(Secret, id, ts, sig, body, Now).Should().BeFalse();
    }

    [Fact]
    public void Malformed_secret_fails_gracefully()
    {
        var body = "{\"x\":1}";
        var (id, ts, sig) = Sign(body, SecretBytes, Now);

        _verifier.Verify("whsec_not-base-64!!!", id, ts, sig, body, Now).Should().BeFalse();
    }
}
