using Axis.ControlPlane.SignUp;

namespace Axis.ControlPlane.Tests;

/// <summary>The rule that compares sign-up emails, see <c>docs/reference/sign-up.md#bots-and-abuse</c>.</summary>
public sealed class SignUpEmailTests
{
    [Theory]
    [InlineData("Owner+Test@Example.com", "owner@example.com")]
    [InlineData("a+b+c@x.org", "a@x.org")]
    [InlineData("Owner@Example.COM", "owner@example.com")]
    [InlineData("owner@example.com", "owner@example.com")]
    [InlineData("owner@exa+mple.com", "owner@exa+mple.com")]
    public void The_whole_address_is_lower_case_and_the_tag_of_the_local_part_is_removed(string email, string expected) =>
        Assert.Equal(expected, SignUpEmail.Normalise(email));
}
