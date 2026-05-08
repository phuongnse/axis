using System.Security.Claims;
using Axis.Shared.Infrastructure.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Axis.Shared.Infrastructure.Tests.Tenancy;

public class HttpTenantContextTests
{
    private static IHttpContextAccessor BuildAccessor(Guid orgId, string orgSlug)
    {
        var claims = new[]
        {
            new Claim("org_id", orgId.ToString()),
            new Claim("org_slug", orgSlug)
        };
        var identity = new ClaimsIdentity(claims, "Bearer");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    [Fact]
    public void OrganizationId_reads_org_id_claim()
    {
        var orgId = Guid.NewGuid();
        var sut = new HttpTenantContext(BuildAccessor(orgId, "acme"));

        sut.OrganizationId.Should().Be(orgId);
    }

    [Fact]
    public void OrganizationSlug_reads_org_slug_claim()
    {
        var sut = new HttpTenantContext(BuildAccessor(Guid.NewGuid(), "my-company"));

        sut.OrganizationSlug.Should().Be("my-company");
    }

    [Fact]
    public void SchemaName_derives_tenant_prefix()
    {
        var sut = new HttpTenantContext(BuildAccessor(Guid.NewGuid(), "acme"));

        sut.SchemaName.Should().Be("tenant_acme");
    }

    [Fact]
    public void Throws_when_http_context_is_null()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var sut = new HttpTenantContext(accessor);

        var act = () => _ = sut.OrganizationId;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HTTP context*");
    }

    [Fact]
    public void Throws_when_org_id_claim_is_missing()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("org_slug", "acme")], "Bearer"))
        };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = new HttpTenantContext(accessor);

        var act = () => _ = sut.OrganizationId;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*org_id*");
    }
}
