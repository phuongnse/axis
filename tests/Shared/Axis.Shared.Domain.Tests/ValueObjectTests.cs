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
    public void Value_objects_with_same_components_are_equal()
    {
        var a = new Money(100, "USD");
        var b = new Money(100, "USD");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Value_objects_with_different_components_are_not_equal()
    {
        var a = new Money(100, "USD");
        var b = new Money(200, "USD");

        a.Should().NotBe(b);
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void Value_objects_with_different_currency_are_not_equal()
    {
        var a = new Money(100, "USD");
        var b = new Money(100, "EUR");

        a.Should().NotBe(b);
    }

    [Fact]
    public void Value_object_is_not_equal_to_null()
    {
        var a = new Money(100, "USD");

        a.Equals(null).Should().BeFalse();
        (a == null!).Should().BeFalse();
    }

    [Fact]
    public void Value_objects_with_same_components_have_same_hash_code()
    {
        var a = new Money(100, "USD");
        var b = new Money(100, "USD");

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Value_objects_of_different_types_are_not_equal()
    {
        var money = new Money(100, "USD");
        var single = new SingleValue("100");

        money.Equals(single).Should().BeFalse();
    }
}
