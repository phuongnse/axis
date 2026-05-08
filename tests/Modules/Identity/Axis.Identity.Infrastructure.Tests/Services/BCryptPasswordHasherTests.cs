using Axis.Identity.Infrastructure.Services;
using FluentAssertions;

namespace Axis.Identity.Infrastructure.Tests.Services;

public class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _sut = new();

    [Fact]
    public void Hash_returns_non_empty_string()
    {
        var hash = _sut.Hash("secret123");
        hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Hash_produces_different_hash_each_call()
    {
        var h1 = _sut.Hash("secret123");
        var h2 = _sut.Hash("secret123");
        h1.Should().NotBe(h2); // bcrypt includes random salt
    }

    [Fact]
    public void Verify_returns_true_for_correct_password()
    {
        var hash = _sut.Hash("correct-password");
        _sut.Verify("correct-password", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_returns_false_for_wrong_password()
    {
        var hash = _sut.Hash("correct-password");
        _sut.Verify("wrong-password", hash).Should().BeFalse();
    }
}
