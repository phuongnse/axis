using System.Security.Claims;
using Axis.Shared.Infrastructure.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Axis.Shared.Infrastructure.Tests.Tenancy;

public class HttpTenantContextTests
{
    private static IHttpContextAccessor BuildAccessor(Guid teamAccountId)
    {
        Claim[] claims = new[] { new Claim("team_account_id", teamAccountId.ToString()) };
        ClaimsIdentity identity = new ClaimsIdentity(claims, "Bearer");
        ClaimsPrincipal principal = new ClaimsPrincipal(identity);
        DefaultHttpContext httpContext = new DefaultHttpContext { User = principal };
        IHttpContextAccessor accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    [Fact]
    public void HttpTenantContext_WhenTeamAccountIdClaimPresent_ReadsTeamAccountId()
    {
        Guid teamAccountId = Guid.NewGuid();
        HttpTenantContext sut = new HttpTenantContext(BuildAccessor(teamAccountId));

        sut.TeamAccountId.Should().Be(teamAccountId);
    }

    [Fact]
    public void HttpTenantContext_WhenTeamAccountIdClaimPresent_DerivesSchemaName()
    {
        Guid teamAccountId = Guid.NewGuid();
        HttpTenantContext sut = new HttpTenantContext(BuildAccessor(teamAccountId));

        sut.SchemaName.Should().Be($"tenant_{teamAccountId:N}");
    }

    [Fact]
    public void HttpTenantContext_WhenHttpContextIsNull_Throws()
    {
        IHttpContextAccessor accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        HttpTenantContext sut = new HttpTenantContext(accessor);

        Func<Guid> act = () => _ = sut.TeamAccountId;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HTTP context*");
    }

    [Fact]
    public void HttpTenantContext_WhenTeamAccountIdClaimIsMissing_Throws()
    {
        DefaultHttpContext httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([], "Bearer"))
        };
        IHttpContextAccessor accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        HttpTenantContext sut = new HttpTenantContext(accessor);

        Func<Guid> act = () => _ = sut.TeamAccountId;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*team_account_id*");
    }
}
