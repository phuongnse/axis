using Axis.FormBuilder.Application.Queries.GetFormTaskByToken;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.FormBuilder.Application.Tests.Queries;

public class GetFormTaskByTokenHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid FormId = Guid.NewGuid();
    private static readonly Guid ExecutionId = Guid.NewGuid();
    private static readonly Guid StepId = Guid.NewGuid();
    private static readonly Guid AssigneeId = Guid.NewGuid();

    private readonly IFormSubmissionRepository _submissionRepo = Substitute.For<IFormSubmissionRepository>();
    private readonly IFormRepository _formRepo = Substitute.For<IFormRepository>();
    private readonly GetFormTaskByTokenHandler _handler;

    public GetFormTaskByTokenHandlerTests() =>
        _handler = new GetFormTaskByTokenHandler(_submissionRepo, _formRepo);

    [Fact]
    public async Task Handle_WhenSubmissionNotFound_ReturnsNull()
    {
        Guid token = Guid.NewGuid();
        _submissionRepo.GetByAccessTokenAsync(token, Arg.Any<CancellationToken>())
            .Returns((FormSubmission?)null);

        FormTaskByTokenDto? dto = await _handler.Handle(
            new GetFormTaskByTokenQuery(token), CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenFormNotFound_ReturnsNull()
    {
        FormSubmission submission = CreatePendingSubmission();
        _submissionRepo
            .GetByAccessTokenAsync(submission.AccessToken, Arg.Any<CancellationToken>())
            .Returns(submission);
        _formRepo
            .GetByIdAsync(submission.FormDefinitionId, submission.OrganizationId, Arg.Any<CancellationToken>())
            .Returns((FormDefinition?)null);

        FormTaskByTokenDto? dto = await _handler.Handle(
            new GetFormTaskByTokenQuery(submission.AccessToken), CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenValid_ReturnsDtoWithFieldsOrderedByDisplayOrder()
    {
        FormSubmission submission = CreatePendingSubmission();

        FormDefinition form = FormDefinition.Create("Onboarding", "desc", OrgId, "user");
        form.AddField("last_name", "Last Name", FormFieldType.Text, true, null);
        form.AddField("first_name", "First Name", FormFieldType.Text, true, null);

        _submissionRepo
            .GetByAccessTokenAsync(submission.AccessToken, Arg.Any<CancellationToken>())
            .Returns(submission);
        _formRepo
            .GetByIdAsync(submission.FormDefinitionId, submission.OrganizationId, Arg.Any<CancellationToken>())
            .Returns(form);

        FormTaskByTokenDto? dto = await _handler.Handle(
            new GetFormTaskByTokenQuery(submission.AccessToken), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.SubmissionId.Should().Be(submission.Id);
        dto.Status.Should().Be(FormSubmissionStatus.Pending.ToString());
        dto.FormDefinitionId.Should().Be(form.Id);
        dto.FormName.Should().Be("Onboarding");
        dto.FormDescription.Should().Be("desc");
        dto.Fields.Should().HaveCount(2);
        dto.Fields.Select(f => f.Key).Should().ContainInOrder("last_name", "first_name");
        dto.ExpiresAt.Should().Be(submission.ExpiresAt);
    }

    private static FormSubmission CreatePendingSubmission()
    {
        return FormSubmission.Create(
            FormId,
            OrgId,
            ExecutionId,
            StepId,
            AssigneeId,
            null,
            null,
            "workflow-engine");
    }
}
