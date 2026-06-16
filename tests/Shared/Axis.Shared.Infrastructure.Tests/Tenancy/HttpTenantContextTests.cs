using System.Security.Claims;
using Axis.Shared.Infrastructure.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Axis.Shared.Infrastructure.Tests.Tenancy;

public class HttpTenantContextTests
{
    private static IHttpContextAccessor BuildAccessor(Guid TenantId)
    {
        Claim[] claims = new[] { new Claim("tenant_id", TenantId.ToString()) };
        ClaimsIdentity identity = new ClaimsIdentity(claims, "Bearer");
        ClaimsPrincipal principal = new ClaimsPrincipal(identity);
        DefaultHttpContext httpContext = new DefaultHttpContext { User = principal };
        IHttpContextAccessor accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    [Fact]
    public void HttpTenantContext_WhenTenantIdClaimPresent_ReadstenantId()
    {
        Guid TenantId = Guid.NewGuid();
        HttpTenantContext sut = new HttpTenantContext(BuildAccessor(TenantId));

        sut.tenantId.Should().Be(TenantId);
    }

    [Fact]
    public void HttpTenantContext_WhenTenantIdClaimPresent_DerivesSchemaName()
    {
        Guid TenantId = Guid.NewGuid();
        HttpTenantContext sut = new HttpTenantContext(BuildAccessor(TenantId));

        sut.SchemaName.Should().Be($"tenant_{TenantId:N}");
    }

    [Fact]
    public void HttpTenantContext_WhenHttpContextIsNull_Throws()
    {
        IHttpContextAccessor accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        HttpTenantContext sut = new HttpTenantContext(accessor);

        Func<Guid> act = () => _ = sut.tenantId;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HTTP context*");
    }

    [Fact]
    public void HttpTenantContext_WhenTenantIdClaimIsMissing_Throws()
    {
        DefaultHttpContext httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([], "Bearer"))
        };
        IHttpContextAccessor accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        HttpTenantContext sut = new HttpTenantContext(accessor);

        Func<Guid> act = () => _ = sut.tenantId;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*tenant_id*");
    }
}
