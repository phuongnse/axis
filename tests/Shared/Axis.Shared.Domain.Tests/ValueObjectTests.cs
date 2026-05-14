using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Shared.Domain.Tests;

public class ValueObjectTests
{
    private class Money(decimal amount, string currency) : ValueObject
    {
        public decimal Amount { get; } = amount;
        public string Currency { get; } = currency;

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    private class SingleValue(string value) : ValueObject
    {
        public string Value { get; } = value;

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Value;
        }
    }

    [Fact]
    public void ValueObject_WhenComponentsAreIdentical_AreEqual()
    {
        var a = new Money(100, "USD");
        var b = new Money(100, "USD");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void ValueObject_WhenComponentsDiffer_AreNotEqual()
    {
        var a = new Money(100, "USD");
        var b = new Money(200, "USD");

        a.Should().NotBe(b);
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void ValueObject_WhenCurrencyDiffers_AreNotEqual()
    {
        var a = new Money(100, "USD");
        var b = new Money(100, "EUR");

        a.Should().NotBe(b);
    }

    [Fact]
    public void ValueObject_WhenComparedToNull_IsNotEqual()
    {
        var a = new Money(100, "USD");

        a.Equals(null).Should().BeFalse();
        (a == null!).Should().BeFalse();
    }

    [Fact]
    public void ValueObject_WhenComponentsAreIdentical_HaveSameHashCode()
    {
        var a = new Money(100, "USD");
        var b = new Money(100, "USD");

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void ValueObject_WhenTypesAreDifferent_AreNotEqual()
    {
        var money = new Money(100, "USD");
        var single = new SingleValue("100");

        money.Equals(single).Should().BeFalse();
    }
}
