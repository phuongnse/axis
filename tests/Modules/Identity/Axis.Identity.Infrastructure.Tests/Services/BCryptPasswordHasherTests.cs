using Axis.Identity.Infrastructure.Services;
using FluentAssertions;

namespace Axis.Identity.Infrastructure.Tests.Services;

public class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _sut = new();

    [Fact]
    public void Hash_WhenCalled_ReturnsNonEmptyString()
    {
        string hash = _sut.Hash("secret123");
        hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Hash_WhenCalledMultipleTimes_ProducesDifferentHashEachTime()
    {
        string h1 = _sut.Hash("secret123");
        string h2 = _sut.Hash("secret123");
        h1.Should().NotBe(h2); // bcrypt includes random salt
    }

    [Fact]
    public void Verify_WhenPasswordIsCorrect_ReturnsTrue()
    {
        string hash = _sut.Hash("correct-password");
        _sut.Verify("correct-password", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WhenPasswordIsWrong_ReturnsFalse()
    {
        string hash = _sut.Hash("correct-password");
        _sut.Verify("wrong-password", hash).Should().BeFalse();
    }
}
