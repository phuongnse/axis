using Axis.Server.Tenancy;
using Axis.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Server.Tests;

/// <summary>The quota refusal is a <c>403</c> problem with the reason, the quota, the limit and the use, and no tenant in its title.</summary>
public sealed class QuotaRefusalTests
{
    [Fact]
    public void The_applications_refusal_is_a_403_with_the_reason_the_quota_the_limit_and_the_use()
    {
        var result = QuotaRefusal.Problem(QuotaKeys.Applications, 10, 12);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        var problem = Assert.IsAssignableFrom<ProblemDetails>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
        Assert.Equal("This tenant has reached its applications quota.", problem.Title);
        Assert.Equal(
            new Dictionary<string, object?> { ["reason"] = "quota-exceeded", ["quota"] = "applications", ["limit"] = 10L, ["used"] = 12L },
            problem.Extensions);
    }

    [Theory]
    [InlineData(QuotaKeys.Storage, "storage")]
    [InlineData(QuotaKeys.Users, "users")]
    [InlineData(QuotaKeys.Applications, "applications")]
    public void The_title_names_the_quota_in_words_and_no_tenant(string quota, string words)
    {
        var problem = Assert.IsAssignableFrom<ProblemDetails>(
            Assert.IsAssignableFrom<IValueHttpResult>(QuotaRefusal.Problem(quota, 1, 1)).Value);

        Assert.Equal($"This tenant has reached its {words} quota.", problem.Title);
        Assert.Null(problem.Detail);
    }

    [Theory]
    [InlineData(QuotaKeys.EmailsPerDay)]
    [InlineData(QuotaKeys.ConcurrentImports)]
    [InlineData(QuotaKeys.WorkerWeight)]
    [InlineData("unknown")]
    public void A_quota_that_does_not_refuse_has_no_refusal(string quota) =>
        Assert.Throws<ArgumentException>(() => QuotaRefusal.Problem(quota, 1, 1));
}
