using Axis.FormBuilder.Application.Commands.SubmitFormByToken;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using Axis.Shared.Application;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.FormBuilder.Application.Tests.Commands;

public class SubmitFormByTokenHandlerTests
{
    private readonly IFormSubmissionRepository _submissionRepo = Substitute.For<IFormSubmissionRepository>();
    private readonly IFormRepository _formRepo = Substitute.For<IFormRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private static readonly Guid FormId = Guid.NewGuid();
    private static readonly Guid ExecutionId = Guid.NewGuid();
    private static readonly Guid StepId = Guid.NewGuid();
    private static readonly Guid AssigneeId = Guid.NewGuid();

    private SubmitFormByTokenHandler CreateHandler() => new(_submissionRepo, _formRepo, _uow, _currentUser);

    private static FormSubmission CreatePendingSubmission(DateTimeOffset? expiresAt = null)
    {
        FormSubmission submission = FormSubmission.Create(
            FormId,
            TeamAccountId,
            ExecutionId,
            StepId,
            AssigneeId,
            null,
            expiresAt,
            "workflow-engine");

        return submission;
    }

    [Fact]
    public async Task SubmitFormByToken_WhenNotFound_ReturnsNotFound()
    {
        Guid token = Guid.NewGuid();
        _submissionRepo.GetByAccessTokenAsync(token, Arg.Any<CancellationToken>()).Returns((FormSubmission?)null);

        Result result = await CreateHandler().Handle(
            new SubmitFormByTokenCommand(token, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task SubmitFormByToken_WhenAlreadySubmitted_ReturnsBusinessRule()
    {
        FormSubmission submission = CreatePendingSubmission();
        submission.Submit(AssigneeId, new Dictionary<string, object?> { ["field"] = "value" });

        _submissionRepo
            .GetByAccessTokenAsync(submission.AccessToken, Arg.Any<CancellationToken>())
            .Returns(submission);

        Result result = await CreateHandler().Handle(
            new SubmitFormByTokenCommand(submission.AccessToken, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("already been submitted");
    }

    [Fact]
    public async Task SubmitFormByToken_WhenExpired_ReturnsBusinessRule()
    {
        FormSubmission submission = CreatePendingSubmission(DateTimeOffset.UtcNow.AddHours(-1));

        _submissionRepo
            .GetByAccessTokenAsync(submission.AccessToken, Arg.Any<CancellationToken>())
            .Returns(submission);

        Result result = await CreateHandler().Handle(
            new SubmitFormByTokenCommand(submission.AccessToken, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("expired");
    }

    [Fact]
    public async Task SubmitFormByToken_WhenPending_SucceedsAndPersists()
    {
        FormSubmission submission = CreatePendingSubmission();
        Dictionary<string, object?> data = new() { ["name"] = "Jane" };

        FormDefinition form = FormDefinition.Create("Test Form", null, TeamAccountId, "user");
        form.AddField("name", "Name", FormFieldType.Text, true, null);

        _submissionRepo
            .GetByAccessTokenAsync(submission.AccessToken, Arg.Any<CancellationToken>())
            .Returns(submission);
        _formRepo
            .GetByIdAsync(FormId, TeamAccountId, Arg.Any<CancellationToken>())
            .Returns(form);

        _currentUser.UserId.Returns(AssigneeId);

        Result result = await CreateHandler().Handle(
            new SubmitFormByTokenCommand(submission.AccessToken, data),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        submission.Status.Should().Be(FormSubmissionStatus.Submitted);
        submission.SubmittedData.Should().ContainKey("name");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitFormByToken_WhenCancelled_ReturnsBusinessRule()
    {
        FormSubmission submission = CreatePendingSubmission();
        submission.Cancel();

        _submissionRepo
            .GetByAccessTokenAsync(submission.AccessToken, Arg.Any<CancellationToken>())
            .Returns(submission);

        Result result = await CreateHandler().Handle(
            new SubmitFormByTokenCommand(submission.AccessToken, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("cancelled");
    }

    [Fact]
    public async Task SubmitFormByToken_WhenRequiredFieldMissing_ReturnsFieldValidation()
    {
        FormSubmission submission = CreatePendingSubmission();
        FormDefinition form = FormDefinition.Create("Test Form", null, TeamAccountId, "user");
        form.AddField("name", "Name", FormFieldType.Text, true, null);

        _submissionRepo
            .GetByAccessTokenAsync(submission.AccessToken, Arg.Any<CancellationToken>())
            .Returns(submission);
        _formRepo
            .GetByIdAsync(FormId, TeamAccountId, Arg.Any<CancellationToken>())
            .Returns(form);

        Result result = await CreateHandler().Handle(
            new SubmitFormByTokenCommand(submission.AccessToken, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.FieldValidation);
        result.FieldErrors.Should().ContainKey("name");
    }
}
