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
        Claim[] claims = new[] { new Claim("org_id", orgId.ToString()) };
        ClaimsIdentity identity = new ClaimsIdentity(claims, "Bearer");
        ClaimsPrincipal principal = new ClaimsPrincipal(identity);
        DefaultHttpContext httpContext = new DefaultHttpContext { User = principal };
        IHttpContextAccessor accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    [Fact]
    public void HttpTenantContext_WhenOrgIdClaimPresent_ReadsOrganizationId()
    {
        Guid orgId = Guid.NewGuid();
        HttpTenantContext sut = new HttpTenantContext(BuildAccessor(orgId));

        sut.OrganizationId.Should().Be(orgId);
    }

    [Fact]
    public void HttpTenantContext_WhenOrgIdClaimPresent_DerivesSchemaName()
    {
        Guid orgId = Guid.NewGuid();
        HttpTenantContext sut = new HttpTenantContext(BuildAccessor(orgId));

        sut.SchemaName.Should().Be($"tenant_{orgId:N}");
    }

    [Fact]
    public void HttpTenantContext_WhenHttpContextIsNull_Throws()
    {
        IHttpContextAccessor accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        HttpTenantContext sut = new HttpTenantContext(accessor);

        Func<Guid> act = () => _ = sut.OrganizationId;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HTTP context*");
    }

    [Fact]
    public void HttpTenantContext_WhenOrgIdClaimIsMissing_Throws()
    {
        DefaultHttpContext httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([], "Bearer"))
        };
        IHttpContextAccessor accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        HttpTenantContext sut = new HttpTenantContext(accessor);

        Func<Guid> act = () => _ = sut.OrganizationId;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*org_id*");
    }
}
