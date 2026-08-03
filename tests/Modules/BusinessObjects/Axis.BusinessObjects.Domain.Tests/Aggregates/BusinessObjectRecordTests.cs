using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.BusinessObjects.Domain.Tests.Aggregates;

public sealed class BusinessObjectRecordTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid OtherUserId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly DateTime Now = new(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateDraft_WhenContractIdentityIsValid_CreatesEditableDraftAtRevisionOne()
    {
        BusinessObjectRecord record = CreateDraft();

        record.Status.Should().Be(BusinessObjectRecordStatus.Draft);
        record.Revision.Should().Be(1);
        record.DefinitionVersionNumber.Should().Be(1);
        record.ObjectKey.Value.Should().Be("loan_application");
        record.Values.Should().ContainKey("applicant_name")
            .WhoseValue.Should().Equal("Ada Lovelace");
    }

    [Fact]
    public void SaveDraft_WhenRevisionIsCurrent_ReplacesValuesAndIncrementsRevision()
    {
        BusinessObjectRecord record = CreateDraft();

        Result result = record.SaveDraft(
            expectedRevision: 1,
            values: new Dictionary<string, IReadOnlyList<string>>
            {
                ["applicant_name"] = ["Grace Hopper"],
                ["requested_amount"] = ["12000"],
            },
            updatedByUserId: OtherUserId,
            updatedAt: Now.AddMinutes(1));

        result.IsSuccess.Should().BeTrue();
        record.Revision.Should().Be(2);
        record.UpdatedByUserId.Should().Be(OtherUserId);
        record.PayloadHash.Should().Be("hash-1");
        record.Values["applicant_name"].Should().Equal("Grace Hopper");
    }

    [Fact]
    public void SaveDraft_WhenRevisionIsStale_LeavesDraftUnchanged()
    {
        BusinessObjectRecord record = CreateDraft();
        IReadOnlyDictionary<string, IReadOnlyList<string>> originalValues = record.Values;

        Result result = record.SaveDraft(
            expectedRevision: 0,
            values: new Dictionary<string, IReadOnlyList<string>>
            {
                ["applicant_name"] = ["Grace Hopper"],
            },
            updatedByUserId: OtherUserId,
            updatedAt: Now.AddMinutes(1));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        record.Revision.Should().Be(1);
        record.Values.Should().BeEquivalentTo(originalValues);
        record.PayloadHash.Should().Be("hash-1");
    }

    [Fact]
    public void Submit_WhenEvidenceIsValid_TransitionsToSubmittedAndRejectsLaterMutation()
    {
        BusinessObjectRecord record = CreateDraft();

        Result submit = record.Submit(
            expectedRevision: 1,
            values: record.Values,
            evaluations:
            [
                new(
                    "applicant_name",
                    Guid.Parse("44444444-4444-4444-8444-444444444444"),
                    1,
                    "field.required",
                    1,
                    true,
                    [new("required-check", true)]),
            ],
            submittedByUserId: OtherUserId,
            submittedAt: Now.AddMinutes(2));

        submit.IsSuccess.Should().BeTrue();
        record.Status.Should().Be(BusinessObjectRecordStatus.Submitted);
        record.Revision.Should().Be(2);
        record.SubmittedByUserId.Should().Be(OtherUserId);
        record.SubmittedAt.Should().Be(Now.AddMinutes(2));
        record.RuleEvaluations.Should().ContainSingle();

        Result saveAfterSubmit = record.SaveDraft(
            expectedRevision: 2,
            values: record.Values,
            updatedByUserId: OtherUserId,
            updatedAt: Now.AddMinutes(3));

        saveAfterSubmit.IsFailure.Should().BeTrue();
        saveAfterSubmit.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public void CreateDraft_WhenValuesContainDuplicateOrInvalidKeys_RejectsInput()
    {
        Result<BusinessObjectRecord> result = BusinessObjectRecord.CreateDraft(
            WorkspaceId,
            BusinessObjectDefinitionVersionId.New(),
            1,
            BusinessObjectDefinitionKey.Create("loan_application").Value,
            "record-1",
            "hash-1",
            new Dictionary<string, IReadOnlyList<string>>
            {
                [" "] = ["invalid"],
            },
            UserId,
            Now);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
    }

    private static BusinessObjectRecord CreateDraft()
    {
        Result<BusinessObjectRecord> result = BusinessObjectRecord.CreateDraft(
            WorkspaceId,
            BusinessObjectDefinitionVersionId.New(),
            1,
            BusinessObjectDefinitionKey.Create("loan_application").Value,
            "record-1",
            "hash-1",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["applicant_name"] = ["Ada Lovelace"],
            },
            UserId,
            Now);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }
}
