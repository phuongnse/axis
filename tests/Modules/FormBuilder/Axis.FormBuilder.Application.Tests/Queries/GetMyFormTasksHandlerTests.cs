using Axis.FormBuilder.Application.Queries.GetMyFormTasks;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.FormBuilder.Application.Tests.Queries;

public class GetMyFormTasksHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid FormId = Guid.NewGuid();
    private static readonly Guid ExecutionId = Guid.NewGuid();
    private static readonly Guid StepId = Guid.NewGuid();

    private readonly IFormSubmissionRepository _submissionRepo = Substitute.For<IFormSubmissionRepository>();
    private readonly IFormRepository _formRepo = Substitute.For<IFormRepository>();
    private readonly GetMyFormTasksHandler _handler;

    public GetMyFormTasksHandlerTests() =>
        _handler = new GetMyFormTasksHandler(_submissionRepo, _formRepo);

    [Fact]
    public async Task Handle_WhenPending_ReturnsSummariesFromPendingRepository()
    {
        FormSubmission submission = CreateSubmission(FormSubmissionStatus.Pending);
        FormDefinition form = FormDefinition.Create("Leave Request", null, OrgId, "user");

        _submissionRepo
            .GetPendingForUserAsync(UserId, OrgId, Arg.Any<CancellationToken>())
            .Returns(new[] { submission });
        _formRepo
            .GetByIdAsync(submission.FormDefinitionId, OrgId, Arg.Any<CancellationToken>())
            .Returns(form);

        IReadOnlyList<FormTaskSummaryDto> result = await _handler.Handle(
            new GetMyFormTasksQuery(UserId, OrgId, FormSubmissionStatus.Pending),
            CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(submission.Id);
        result[0].FormName.Should().Be("Leave Request");
        result[0].Status.Should().Be(FormSubmissionStatus.Pending.ToString());
        result[0].AccessToken.Should().Be(submission.AccessToken);

        await _submissionRepo.DidNotReceive()
            .GetByUserAndStatusAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<FormSubmissionStatus>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenSubmitted_UsesStatusRepository()
    {
        FormSubmission submission = CreateSubmission(FormSubmissionStatus.Submitted);

        _submissionRepo
            .GetByUserAndStatusAsync(UserId, OrgId, FormSubmissionStatus.Submitted, Arg.Any<CancellationToken>())
            .Returns(new[] { submission });
        _formRepo
            .GetByIdAsync(submission.FormDefinitionId, OrgId, Arg.Any<CancellationToken>())
            .Returns((FormDefinition?)null);

        IReadOnlyList<FormTaskSummaryDto> result = await _handler.Handle(
            new GetMyFormTasksQuery(UserId, OrgId, FormSubmissionStatus.Submitted),
            CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].FormName.Should().Be("Unknown form");
        result[0].Status.Should().Be(FormSubmissionStatus.Submitted.ToString());

        await _submissionRepo.DidNotReceive()
            .GetPendingForUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenNoSubmissions_ReturnsEmptyList()
    {
        _submissionRepo
            .GetPendingForUserAsync(UserId, OrgId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<FormSubmission>());

        IReadOnlyList<FormTaskSummaryDto> result = await _handler.Handle(
            new GetMyFormTasksQuery(UserId, OrgId, FormSubmissionStatus.Pending),
            CancellationToken.None);

        result.Should().BeEmpty();
    }

    private static FormSubmission CreateSubmission(FormSubmissionStatus status)
    {
        FormSubmission submission = FormSubmission.Create(
            FormId,
            OrgId,
            ExecutionId,
            StepId,
            UserId,
            null,
            null,
            "workflow-engine");

        if (status == FormSubmissionStatus.Submitted)
            submission.Submit(UserId, new Dictionary<string, object?>());

        return submission;
    }
}
