using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.BusinessObjects.Application.Tests;

internal static class BusinessObjectRecordHandlerTestContext
{
    public static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid UserId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static readonly Guid RecordId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    public static readonly Guid BindingId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    public static readonly DateTime Now = new(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);

    public static BusinessObjectDefinitionVersion PublishedDefinition(
        BusinessObjectFieldType fieldType = BusinessObjectFieldType.Text,
        bool includeRule = false,
        int bindingRevision = 1,
        string objectKey = "loan_application")
        => CreatePublishedDefinition(fieldType, includeRule, bindingRevision, objectKey).Versions.Single();

    public static BusinessObjectDefinition CreatePublishedDefinition(
        BusinessObjectFieldType fieldType = BusinessObjectFieldType.Text,
        bool includeRule = false,
        int bindingRevision = 1,
        string objectKey = "loan_application")
    {
        BusinessObjectDefinition definition = BusinessObjectDefinitionHandlerTestContext.CreateUnpublished(
            "Loan application",
            objectKey);
        BusinessObjectFieldRuleSpec[] rules = includeRule
            ? [new(BindingId, BindingRevision: bindingRevision)]
            : [];
        string fieldKey = fieldType == BusinessObjectFieldType.Integer ? "requested_amount" : "applicant_name";
        string fieldLabel = fieldType == BusinessObjectFieldType.Integer ? "Requested amount" : "Applicant name";
        definition.SaveUnpublished(
            "Loan application",
            [new BusinessObjectFieldDefinitionSpec(fieldKey, fieldLabel, 0, fieldType, rules)],
            expectedRevision: 1,
            Now).IsSuccess.Should().BeTrue();
        definition.Publish(2, UserId, Now).IsSuccess.Should().BeTrue();
        return definition;
    }

    public static BusinessObjectRecord DraftRecord(
        BusinessObjectDefinitionVersion definition,
        IReadOnlyDictionary<string, IReadOnlyList<string>> values,
        string idempotencyKey = "record-1",
        string payloadHash = "hash-1")
    {
        Result<BusinessObjectRecord> result = BusinessObjectRecord.CreateDraft(
            WorkspaceId,
            definition.Id,
            definition.VersionNumber,
            definition.Key,
            idempotencyKey,
            payloadHash,
            values,
            UserId,
            Now);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    public sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid? UserId => BusinessObjectRecordHandlerTestContext.UserId;
        public Guid? workspaceId => BusinessObjectRecordHandlerTestContext.WorkspaceId;
    }

    public static void ConfigureRecord(
        IBusinessObjectRecordRepository records,
        IBusinessObjectDefinitionRepository definitions,
        BusinessObjectDefinitionVersion definition,
        BusinessObjectRecord record)
    {
        records.GetByIdForWorkspaceAsync(
                Arg.Any<BusinessObjectRecordId>(),
                WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(record);
        definitions.GetPublishedVersionByIdForWorkspaceAsync(
                definition.Id,
                WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
    }
}
