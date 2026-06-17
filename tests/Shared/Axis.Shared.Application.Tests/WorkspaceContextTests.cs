using Axis.Shared.Application.Workspaces;
using FluentAssertions;

namespace Axis.Shared.Application.Tests;

public class WorkspaceContextTests
{
    [Fact]
    public void WorkspaceContext_WhenCreatedWithWorkspaceId_DerivesSchemaNameFromWorkspaceId()
    {
        Guid WorkspaceId = Guid.NewGuid();
        WorkspaceContext context = new WorkspaceContext(WorkspaceId);

        context.SchemaName.Should().Be($"workspace_{WorkspaceId:N}");
    }

    [Fact]
    public void WorkspaceContext_WhenCreatedWithWorkspaceId_ExposesworkspaceId()
    {
        Guid WorkspaceId = Guid.NewGuid();
        WorkspaceContext context = new WorkspaceContext(WorkspaceId);

        context.workspaceId.Should().Be(WorkspaceId);
    }
}
