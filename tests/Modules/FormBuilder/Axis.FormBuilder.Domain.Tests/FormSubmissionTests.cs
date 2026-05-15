using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.Events;
using FluentAssertions;

namespace Axis.FormBuilder.Domain.Tests;

public class FormSubmissionTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid FormDefinitionId = Guid.NewGuid();
    private static readonly Guid ExecutionId = Guid.NewGuid();
    private static readonly Guid ExecutionStepId = Guid.NewGuid();
    private static readonly Guid AssigneeUserId = Guid.NewGuid();
    private const string CreatedBy = "system";

    private static FormSubmission CreatePending(
        Guid? assigneeUserId = null,
        Guid? assigneeRoleId = null,
        DateTimeOffset? expiresAt = null) =>
        FormSubmission.Create(
            FormDefinitionId,
            OrgId,
            ExecutionId,
            ExecutionStepId,
            assigneeUserId ?? AssigneeUserId,
            assigneeRoleId,
            expiresAt,
            CreatedBy);

    private static IReadOnlyDictionary<string, object?> SomeData() =>
        new Dictionary<string, object?> { ["name"] = "Alice", ["approved"] = true };

    // ─── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void FormSubmission_WhenCreated_SetsPropertiesAndPendingStatus()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(24);
        var submission = CreatePending(expiresAt: expiry);

        submission.FormDefinitionId.Should().Be(FormDefinitionId);
        submission.OrganizationId.Should().Be(OrgId);
        submission.ExecutionId.Should().Be(ExecutionId);
        submission.ExecutionStepId.Should().Be(ExecutionStepId);
        submission.AssigneeUserId.Should().Be(AssigneeUserId);
        submission.AssigneeRoleId.Should().BeNull();
        submission.ExpiresAt.Should().Be(expiry);
        submission.Status.Should().Be(FormSubmissionStatus.Pending);
        submission.SubmittedAt.Should().BeNull();
        submission.SubmittedByUserId.Should().BeNull();
        submission.SubmittedData.Should().BeEmpty();
    }

    [Fact]
    public void FormSubmission_WhenCreated_GeneratesNonEmptyAccessToken()
    {
        var submission = CreatePending();

        submission.AccessToken.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void FormSubmission_WhenCreatedTwice_GeneratesDifferentAccessTokens()
    {
        var a = CreatePending();
        var b = CreatePending();

        a.AccessToken.Should().NotBe(b.AccessToken);
    }

    [Fact]
    public void FormSubmission_WhenCreated_SetsCreatedAtAndCreatedBy()
    {
        var before = DateTimeOffset.UtcNow;
        var submission = CreatePending();

        submission.CreatedAt.Should().BeOnOrAfter(before);
        submission.CreatedBy.Should().Be(CreatedBy);
    }

    [Fact]
    public void FormSubmission_WhenCreated_RaisesFormTaskCreatedEvent()
    {
        var submission = CreatePending();

        submission.DomainEvents.Should().ContainSingle(e => e is FormTaskCreated);
    }

    [Fact]
    public void FormSubmission_WhenCreated_FormTaskCreatedEventContainsAccessToken()
    {
        var submission = CreatePending();

        var evt = submission.DomainEvents.OfType<FormTaskCreated>().Single();
        evt.AccessToken.Should().Be(submission.AccessToken);
        evt.FormSubmissionId.Should().Be(submission.Id);
        evt.FormDefinitionId.Should().Be(FormDefinitionId);
        evt.OrganizationId.Should().Be(OrgId);
        evt.ExecutionId.Should().Be(ExecutionId);
    }

    [Fact]
    public void FormSubmission_WhenCreatedWithNoExpiry_HasNullExpiresAt()
    {
        var submission = CreatePending(expiresAt: null);
        submission.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void FormSubmission_WhenCreatedWithRoleAssignee_SetsRoleId()
    {
        var roleId = Guid.NewGuid();
        var submission = FormSubmission.Create(
            FormDefinitionId, OrgId, ExecutionId, ExecutionStepId,
            assigneeUserId: null, assigneeRoleId: roleId, expiresAt: null, CreatedBy);

        submission.AssigneeUserId.Should().BeNull();
        submission.AssigneeRoleId.Should().Be(roleId);
    }

    // ─── Submit ───────────────────────────────────────────────────────────────

    [Fact]
    public void Submit_WhenPending_TransitionsToSubmittedWithData()
    {
        var submission = CreatePending();
        var submittedBy = Guid.NewGuid();
        var data = SomeData();
        submission.Submit(submittedBy, data);

        submission.Status.Should().Be(FormSubmissionStatus.Submitted);
        submission.SubmittedAt.Should().NotBeNull();
        submission.SubmittedByUserId.Should().Be(submittedBy);
        submission.SubmittedData.Should().BeEquivalentTo(data);
        submission.DomainEvents.Should().Contain(e => e is FormTaskSubmitted);
    }

    [Fact]
    public void Submit_RaisesEventWithCorrectPayload()
    {
        var submission = CreatePending();
        var submittedBy = Guid.NewGuid();
        var data = SomeData();
        submission.Submit(submittedBy, data);

        var evt = submission.DomainEvents.OfType<FormTaskSubmitted>().Single();
        evt.FormSubmissionId.Should().Be(submission.Id);
        evt.ExecutionId.Should().Be(ExecutionId);
        evt.ExecutionStepId.Should().Be(ExecutionStepId);
        evt.OrganizationId.Should().Be(OrgId);
        evt.SubmittedData.Should().BeEquivalentTo(data);
    }

    [Theory]
    [InlineData(FormSubmissionStatus.Submitted)]
    [InlineData(FormSubmissionStatus.Expired)]
    [InlineData(FormSubmissionStatus.Cancelled)]
    public void Submit_WhenNotPending_Throws(FormSubmissionStatus status)
    {
        var submission = CreatePending();
        BringToStatus(submission, status);

        var act = () => submission.Submit(Guid.NewGuid(), SomeData());
        act.Should().Throw<InvalidOperationException>().WithMessage("*already*");
    }

    // ─── Expire ───────────────────────────────────────────────────────────────

    [Fact]
    public void Expire_WhenPending_TransitionsToExpired()
    {
        var submission = CreatePending(expiresAt: DateTimeOffset.UtcNow.AddHours(1));
        submission.Expire();

        submission.Status.Should().Be(FormSubmissionStatus.Expired);
        submission.DomainEvents.Should().Contain(e => e is FormTaskExpired);
    }

    [Fact]
    public void Expire_RaisesEventWithCorrectPayload()
    {
        var submission = CreatePending(expiresAt: DateTimeOffset.UtcNow.AddHours(1));
        submission.Expire();

        var evt = submission.DomainEvents.OfType<FormTaskExpired>().Single();
        evt.FormSubmissionId.Should().Be(submission.Id);
        evt.ExecutionId.Should().Be(ExecutionId);
        evt.ExecutionStepId.Should().Be(ExecutionStepId);
        evt.OrganizationId.Should().Be(OrgId);
    }

    [Theory]
    [InlineData(FormSubmissionStatus.Submitted)]
    [InlineData(FormSubmissionStatus.Expired)]
    [InlineData(FormSubmissionStatus.Cancelled)]
    public void Expire_WhenNotPending_Throws(FormSubmissionStatus status)
    {
        var submission = CreatePending();
        BringToStatus(submission, status);

        var act = () => submission.Expire();
        act.Should().Throw<InvalidOperationException>().WithMessage("*Pending*");
    }

    // ─── Cancel ───────────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_WhenPending_TransitionsToCancelled()
    {
        var submission = CreatePending();
        submission.Cancel();

        submission.Status.Should().Be(FormSubmissionStatus.Cancelled);
        submission.DomainEvents.Should().Contain(e => e is FormTaskCancelled);
    }

    [Fact]
    public void Cancel_RaisesEventWithCorrectPayload()
    {
        var submission = CreatePending();
        submission.Cancel();

        var evt = submission.DomainEvents.OfType<FormTaskCancelled>().Single();
        evt.FormSubmissionId.Should().Be(submission.Id);
        evt.ExecutionId.Should().Be(ExecutionId);
        evt.OrganizationId.Should().Be(OrgId);
    }

    [Theory]
    [InlineData(FormSubmissionStatus.Submitted)]
    [InlineData(FormSubmissionStatus.Expired)]
    [InlineData(FormSubmissionStatus.Cancelled)]
    public void Cancel_WhenNotPending_Throws(FormSubmissionStatus status)
    {
        var submission = CreatePending();
        BringToStatus(submission, status);

        var act = () => submission.Cancel();
        act.Should().Throw<InvalidOperationException>().WithMessage("*Pending*");
    }

    // ─── Create guards ────────────────────────────────────────────────────────

    [Fact]
    public void Create_WhenFormDefinitionIdIsEmpty_Throws()
    {
        Action act = () => FormSubmission.Create(Guid.Empty, OrgId, ExecutionId, ExecutionStepId, null, null, null, CreatedBy);
        act.Should().Throw<ArgumentException>().WithParameterName("formDefinitionId");
    }

    [Fact]
    public void Create_WhenOrganizationIdIsEmpty_Throws()
    {
        Action act = () => FormSubmission.Create(FormDefinitionId, Guid.Empty, ExecutionId, ExecutionStepId, null, null, null, CreatedBy);
        act.Should().Throw<ArgumentException>().WithParameterName("organizationId");
    }

    [Fact]
    public void Create_WhenExecutionIdIsEmpty_Throws()
    {
        Action act = () => FormSubmission.Create(FormDefinitionId, OrgId, Guid.Empty, ExecutionStepId, null, null, null, CreatedBy);
        act.Should().Throw<ArgumentException>().WithParameterName("executionId");
    }

    [Fact]
    public void Create_WhenExecutionStepIdIsEmpty_Throws()
    {
        Action act = () => FormSubmission.Create(FormDefinitionId, OrgId, ExecutionId, Guid.Empty, null, null, null, CreatedBy);
        act.Should().Throw<ArgumentException>().WithParameterName("executionStepId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenCreatedByIsBlank_Throws(string createdBy)
    {
        Action act = () => FormSubmission.Create(FormDefinitionId, OrgId, ExecutionId, ExecutionStepId, null, null, null, createdBy);
        act.Should().Throw<ArgumentException>().WithParameterName("createdBy");
    }

    // ─── Submit — defensive copy ──────────────────────────────────────────────

    [Fact]
    public void Submit_WhenCallerMutatesSourceDictionary_SubmittedDataIsUnaffected()
    {
        var submission = CreatePending();
        Dictionary<string, object?> mutableData = new() { ["field"] = "original" };
        submission.Submit(Guid.NewGuid(), mutableData);

        mutableData["field"] = "mutated";

        submission.SubmittedData["field"].Should().Be("original");
        FormTaskSubmitted evt = submission.DomainEvents.OfType<FormTaskSubmitted>().Single();
        evt.SubmittedData["field"].Should().Be("original");
    }

    // ─── Expire — non-idempotent domain guard (idempotency at job level) ──────

    [Fact]
    public void Expire_WhenAlreadyExpired_Throws()
    {
        var submission = CreatePending(expiresAt: DateTimeOffset.UtcNow.AddHours(1));
        submission.Expire();

        var act = () => submission.Expire();
        act.Should().Throw<InvalidOperationException>().WithMessage("*Pending*");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static void BringToStatus(FormSubmission submission, FormSubmissionStatus target)
    {
        switch (target)
        {
            case FormSubmissionStatus.Pending:
                break;
            case FormSubmissionStatus.Submitted:
                submission.Submit(Guid.NewGuid(), SomeData());
                break;
            case FormSubmissionStatus.Expired:
                submission.Expire();
                break;
            case FormSubmissionStatus.Cancelled:
                submission.Cancel();
                break;
        }
    }
}
