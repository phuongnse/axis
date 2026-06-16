using Axis.WorkflowBuilder.Application.Commands.BulkExportWorkflows;
using Axis.WorkflowBuilder.Application.Queries.ExportWorkflow;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests;

public class BulkExportWorkflowsHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly BulkExportWorkflowsHandler _handler;

    public BulkExportWorkflowsHandlerTests() => _handler = new BulkExportWorkflowsHandler(_repo);

    [Fact]
    public async Task Handle_WhenTenantHasWorkflows_ReturnsExportDtoForEach()
    {
        WorkflowDefinition wf1 = WorkflowDefinition.Create("Workflow A", null, TenantId, "user");
        WorkflowDefinition wf2 = WorkflowDefinition.Create("Workflow B", null, TenantId, "user");
        _repo.GetAllAsync(TenantId, Arg.Any<CancellationToken>()).Returns([wf1, wf2]);

        IReadOnlyList<WorkflowExportDto> result = await _handler.Handle(
            new BulkExportWorkflowsCommand(TenantId), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().Contain(d => d.Name == "Workflow A");
        result.Should().Contain(d => d.Name == "Workflow B");
    }

    [Fact]
    public async Task Handle_WhenTenantHasNoWorkflows_ReturnsEmptyList()
    {
        _repo.GetAllAsync(TenantId, Arg.Any<CancellationToken>()).Returns([]);

        IReadOnlyList<WorkflowExportDto> result = await _handler.Handle(
            new BulkExportWorkflowsCommand(TenantId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenWorkflowHasSensitiveData_RedactsInAllExports()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("API Workflow", null, TenantId, "user");
        wf.AddStep("HTTP", StepType.HttpRequest,
            new Dictionary<string, object?> { ["token"] = "secret", ["url"] = "https://example.com" });
        _repo.GetAllAsync(TenantId, Arg.Any<CancellationToken>()).Returns([wf]);

        IReadOnlyList<WorkflowExportDto> result = await _handler.Handle(
            new BulkExportWorkflowsCommand(TenantId), CancellationToken.None);

        StepExportDto httpStep = result[0].Steps.Single(s => s.Name == "HTTP");
        httpStep.Config!["token"].Should().Be("[REDACTED]");
        httpStep.Config["url"].Should().Be("https://example.com");
    }
}
