using System.Security.Claims;
using Axis.Shared.Infrastructure.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Axis.Shared.Infrastructure.Tests.Tenancy;

public class HttpTenantContextTests
{
    private static IHttpContextAccessor BuildAccessor(Guid orgId)
    {
        var claims = new[] { new Claim("org_id", orgId.ToString()) };
        var identity = new ClaimsIdentity(claims, "Bearer");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    [Fact]
    public void HttpTenantContext_WhenOrgIdClaimPresent_ReadsOrganizationId()
    {
        var orgId = Guid.NewGuid();
        var sut = new HttpTenantContext(BuildAccessor(orgId));

        sut.OrganizationId.Should().Be(orgId);
    }

    [Fact]
    public void HttpTenantContext_WhenOrgIdClaimPresent_DerivesSchemaName()
    {
        var orgId = Guid.NewGuid();
        var sut = new HttpTenantContext(BuildAccessor(orgId));

        sut.SchemaName.Should().Be($"tenant_{orgId:N}");
    }

    [Fact]
    public void HttpTenantContext_WhenHttpContextIsNull_Throws()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var sut = new HttpTenantContext(accessor);

        var act = () => _ = sut.OrganizationId;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HTTP context*");
    }

    [Fact]
    public void HttpTenantContext_WhenOrgIdClaimIsMissing_Throws()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([], "Bearer"))
        };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = new HttpTenantContext(accessor);

        var act = () => _ = sut.OrganizationId;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*org_id*");
    }
}
