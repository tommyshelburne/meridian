using FluentAssertions;
using Meridian.Infrastructure.Auth;

namespace Meridian.Unit.Infrastructure.Auth;

public class TokenHasherTests
{
    private readonly TokenHasher _hasher = new();

    [Fact]
    public void Hash_is_deterministic()
    {
        var a = _hasher.Hash("some-token");
        var b = _hasher.Hash("some-token");
        a.Should().Be(b);
    }

    [Fact]
    public void Hash_differs_for_different_inputs()
    {
        _hasher.Hash("token-a").Should().NotBe(_hasher.Hash("token-b"));
    }

    [Fact]
    public void Hash_is_64_lowercase_hex_chars()
    {
        var h = _hasher.Hash("anything");
        h.Should().HaveLength(64);
        h.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public void GenerateToken_produces_unique_values()
    {
        var a = _hasher.GenerateToken();
        var b = _hasher.GenerateToken();
        a.Should().NotBe(b);
        a.Should().NotBeNullOrWhiteSpace();
    }
}
