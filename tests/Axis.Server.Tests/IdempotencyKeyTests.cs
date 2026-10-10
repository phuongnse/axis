using Axis.Server.Processes;
using Microsoft.Extensions.Primitives;

namespace Axis.Server.Tests;

public sealed class IdempotencyKeyTests
{
    [Fact]
    public void No_header_is_no_key()
    {
        Assert.True(ProcessStartEndpoints.TryReadIdempotencyKey(StringValues.Empty, out var key));
        Assert.Null(key);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(255)]
    public void One_value_of_1_to_255_characters_is_the_key(int length)
    {
        var value = new string('k', length);

        Assert.True(ProcessStartEndpoints.TryReadIdempotencyKey(new StringValues(value), out var key));
        Assert.Equal(value, key);
    }

    [Fact]
    public void An_empty_value_is_invalid() =>
        Assert.False(ProcessStartEndpoints.TryReadIdempotencyKey(new StringValues(""), out _));

    [Fact]
    public void A_value_over_255_characters_is_invalid() =>
        Assert.False(ProcessStartEndpoints.TryReadIdempotencyKey(new StringValues(new string('k', 256)), out _));

    [Fact]
    public void Two_values_are_invalid() =>
        Assert.False(ProcessStartEndpoints.TryReadIdempotencyKey(new StringValues(["first", "second"]), out _));
}
