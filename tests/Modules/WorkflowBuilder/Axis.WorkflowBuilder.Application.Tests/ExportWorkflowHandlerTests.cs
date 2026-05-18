using Axis.WorkflowBuilder.Application.Queries.ExportWorkflow;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Entities;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests;

public class ExportWorkflowHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly ExportWorkflowHandler _handler;

    public ExportWorkflowHandlerTests() => _handler = new ExportWorkflowHandler(_repo);

    [Fact]
    public async Task Handle_WhenWorkflowExists_ReturnsExportDtoWithAllData()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", "desc", OrgId, "user");
        wf.AddTrigger(TriggerType.Manual, null);
        WorkflowStep step = wf.AddStep("HTTP Call", StepType.HttpRequest,
            new Dictionary<string, object?> { ["url"] = "https://api.example.com", ["token"] = "secret-value" });
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);

        WorkflowExportDto? dto = await _handler.Handle(new ExportWorkflowQuery(wf.Id, OrgId), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Invoice Approval");
        dto.Steps.Should().HaveCount(3); // Start + HTTP Call + End
        dto.Triggers.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WhenWorkflowHasSensitiveConfig_RedactsSensitiveValues()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("API Workflow", null, OrgId, "user");
        wf.AddStep("HTTP Call", StepType.HttpRequest,
            new Dictionary<string, object?>
            {
                ["url"] = "https://api.example.com",
                ["token"] = "my-secret-token",
                ["api_key"] = "sk-1234",
                ["method"] = "POST"
            });
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);

        WorkflowExportDto? dto = await _handler.Handle(new ExportWorkflowQuery(wf.Id, OrgId), CancellationToken.None);

        StepExportDto httpStep = dto!.Steps.Single(s => s.Name == "HTTP Call");
        httpStep.Config!["url"].Should().Be("https://api.example.com");
        httpStep.Config["token"].Should().Be("[REDACTED]");
        httpStep.Config["api_key"].Should().Be("[REDACTED]");
        httpStep.Config["method"].Should().Be("POST");
    }

    [Fact]
    public async Task Handle_WhenConfigHasVariantSensitiveKey_RedactsPatternMatch()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("API Workflow", null, OrgId, "user");
        wf.AddStep("HTTP Call", StepType.HttpRequest,
            new Dictionary<string, object?>
            {
                ["bearer_token"] = "abc",
                ["my_api_key"] = "sk-xyz",
                ["webhook_secret"] = "hmac",
                ["url"] = "https://example.com"
            });
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);

        WorkflowExportDto? dto = await _handler.Handle(new ExportWorkflowQuery(wf.Id, OrgId), CancellationToken.None);

        StepExportDto step = dto!.Steps.Single(s => s.Name == "HTTP Call");
        step.Config!["bearer_token"].Should().Be("[REDACTED]");
        step.Config["my_api_key"].Should().Be("[REDACTED]");
        step.Config["webhook_secret"].Should().Be("[REDACTED]");
        step.Config["url"].Should().Be("https://example.com");
    }

    [Fact]
    public async Task Handle_WhenConfigHasNestedDictionary_RecursivelyScrubs()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("API Workflow", null, OrgId, "user");
        wf.AddStep("HTTP Call", StepType.HttpRequest,
            new Dictionary<string, object?>
            {
                ["headers"] = new Dictionary<string, object?> { ["Authorization"] = "Bearer abc", ["Content-Type"] = "application/json" },
                ["url"] = "https://example.com"
            });
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);

        WorkflowExportDto? dto = await _handler.Handle(new ExportWorkflowQuery(wf.Id, OrgId), CancellationToken.None);

        StepExportDto step = dto!.Steps.Single(s => s.Name == "HTTP Call");
        IReadOnlyDictionary<string, object?> headers = (IReadOnlyDictionary<string, object?>)step.Config!["headers"]!;
        headers["Authorization"].Should().Be("[REDACTED]");
        headers["Content-Type"].Should().Be("application/json");
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNull()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), OrgId, Arg.Any<CancellationToken>()).Returns((WorkflowDefinition?)null);

        WorkflowExportDto? dto = await _handler.Handle(
            new ExportWorkflowQuery(Guid.NewGuid(), OrgId), CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenWorkflowBelongsToAnotherOrg_ReturnsNull()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", null, OrgId, "user");

        Guid otherOrgId = Guid.NewGuid();
        _repo.GetByIdAsync(wf.Id, otherOrgId, Arg.Any<CancellationToken>())
            .Returns((WorkflowDefinition?)null);
        WorkflowExportDto? dto = await _handler.Handle(
            new ExportWorkflowQuery(wf.Id, otherOrgId), CancellationToken.None);

        dto.Should().BeNull();
        await _repo.Received(1).GetByIdAsync(wf.Id, otherOrgId, Arg.Any<CancellationToken>());
    }
}
