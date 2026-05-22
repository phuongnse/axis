using Axis.FormBuilder.Application.Handlers;
using Axis.FormBuilder.Application.Messages;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Axis.FormBuilder.Application.Tests.Handlers;

public class ExpireFormSubmissionHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid FormId = Guid.NewGuid();
    private static readonly Guid ExecutionId = Guid.NewGuid();
    private static readonly Guid StepId = Guid.NewGuid();

    private readonly IFormSubmissionRepository _submissionRepo = Substitute.For<IFormSubmissionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ExpireFormSubmissionHandler _handler;

    public ExpireFormSubmissionHandlerTests() =>
        _handler = new ExpireFormSubmissionHandler(
            _submissionRepo,
            _uow,
            NullLogger<ExpireFormSubmissionHandler>.Instance);

    [Fact]
    public async Task Handle_WhenSubmissionNotFound_DoesNotSave()
    {
        Guid submissionId = Guid.NewGuid();
        _submissionRepo
            .GetByIdAsync(submissionId, OrgId, Arg.Any<CancellationToken>())
            .Returns((FormSubmission?)null);

        await _handler.Handle(new ExpireFormSubmissionMessage(submissionId, OrgId), CancellationToken.None);

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAlreadySubmitted_DoesNotSave()
    {
        FormSubmission submission = CreatePending();
        submission.Submit(Guid.NewGuid(), new Dictionary<string, object?>());

        _submissionRepo
            .GetByIdAsync(submission.Id, OrgId, Arg.Any<CancellationToken>())
            .Returns(submission);

        await _handler.Handle(
            new ExpireFormSubmissionMessage(submission.Id, OrgId),
            CancellationToken.None);

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenPendingAndPastDue_ExpiresAndSaves()
    {
        FormSubmission submission = CreatePending(DateTimeOffset.UtcNow.AddMinutes(-5));

        _submissionRepo
            .GetByIdAsync(submission.Id, OrgId, Arg.Any<CancellationToken>())
            .Returns(submission);

        await _handler.Handle(
            new ExpireFormSubmissionMessage(submission.Id, OrgId),
            CancellationToken.None);

        submission.Status.Should().Be(FormSubmissionStatus.Expired);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static FormSubmission CreatePending(DateTimeOffset? expiresAt = null)
    {
        return FormSubmission.Create(
            FormId,
            OrgId,
            ExecutionId,
            StepId,
            Guid.NewGuid(),
            null,
            expiresAt,
            "workflow-engine");
    }
}
