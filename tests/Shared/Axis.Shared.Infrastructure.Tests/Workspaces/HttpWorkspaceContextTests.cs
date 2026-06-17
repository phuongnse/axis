using System.Security.Claims;
using Axis.Shared.Infrastructure.Workspaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Axis.Shared.Infrastructure.Tests.Workspaces;

public class HttpWorkspaceContextTests
{
    private static IHttpContextAccessor BuildAccessor(Guid WorkspaceId)
    {
        Claim[] claims = new[] { new Claim("workspace_id", WorkspaceId.ToString()) };
        ClaimsIdentity identity = new ClaimsIdentity(claims, "Bearer");
        ClaimsPrincipal principal = new ClaimsPrincipal(identity);
        DefaultHttpContext httpContext = new DefaultHttpContext { User = principal };
        IHttpContextAccessor accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    [Fact]
    public void HttpWorkspaceContext_WhenWorkspaceIdClaimPresent_ReadsworkspaceId()
    {
        Guid WorkspaceId = Guid.NewGuid();
        HttpWorkspaceContext sut = new HttpWorkspaceContext(BuildAccessor(WorkspaceId));

        sut.workspaceId.Should().Be(WorkspaceId);
    }

    [Fact]
    public void HttpWorkspaceContext_WhenWorkspaceIdClaimPresent_DerivesSchemaName()
    {
        Guid WorkspaceId = Guid.NewGuid();
        HttpWorkspaceContext sut = new HttpWorkspaceContext(BuildAccessor(WorkspaceId));

        sut.SchemaName.Should().Be($"workspace_{WorkspaceId:N}");
    }

    [Fact]
    public void HttpWorkspaceContext_WhenHttpContextIsNull_Throws()
    {
        IHttpContextAccessor accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        HttpWorkspaceContext sut = new HttpWorkspaceContext(accessor);

        Func<Guid> act = () => _ = sut.workspaceId;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HTTP context*");
    }

    [Fact]
    public void HttpWorkspaceContext_WhenWorkspaceIdClaimIsMissing_Throws()
    {
        DefaultHttpContext httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([], "Bearer"))
        };
        IHttpContextAccessor accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        HttpWorkspaceContext sut = new HttpWorkspaceContext(accessor);

        Func<Guid> act = () => _ = sut.workspaceId;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*workspace_id*");
    }
}
