using FluentAssertions;
using Meridian.Infrastructure.Auth;

namespace Meridian.Unit.Infrastructure.Auth;

public class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_then_verify_succeeds()
    {
        var hash = _hasher.Hash("correct horse battery staple");
        _hasher.Verify("correct horse battery staple", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_rejects_wrong_password()
    {
        var hash = _hasher.Hash("password1");
        _hasher.Verify("password2", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_returns_false_for_garbage_hash()
    {
        _hasher.Verify("any", "not-a-bcrypt-hash").Should().BeFalse();
    }

    [Fact]
    public void Hash_rejects_empty_password()
    {
        var act = () => _hasher.Hash("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NeedsRehash_is_false_for_fresh_hash()
    {
        var hash = _hasher.Hash("password1");
        _hasher.NeedsRehash(hash).Should().BeFalse();
    }
}
