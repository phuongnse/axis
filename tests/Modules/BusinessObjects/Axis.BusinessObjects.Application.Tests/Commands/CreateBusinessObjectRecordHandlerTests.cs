using Axis.BusinessObjects.Application.Commands.CreateBusinessObjectRecord;
using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Application.Services;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.BusinessObjects.Application.Tests.Commands;

public sealed class CreateBusinessObjectRecordHandlerTests
{
    [Fact]
    public async Task CreateRecord_WhenDraftChangesAfterCreate_UsesOriginalIdempotencyFingerprint()
    {
        BusinessObjectDefinition definition =
            BusinessObjectRecordHandlerTestContext.CreatePublishedDefinition(BusinessObjectFieldType.Integer);
        BusinessObjectDefinitionVersion publishedVersion = definition.Versions.Single();
        IBusinessObjectDefinitionRepository definitions = Substitute.For<IBusinessObjectDefinitionRepository>();
        IBusinessObjectRecordRepository records = Substitute.For<IBusinessObjectRecordRepository>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        BusinessObjectRecord? persisted = null;
        records.AddAsync(
                Arg.Do<BusinessObjectRecord>(record => persisted = record),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        definitions.GetByKeyForWorkspaceAsync(
                definition.Key,
                BusinessObjectRecordHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        definitions.GetPublishedVersionByIdForWorkspaceAsync(
                publishedVersion.Id,
                BusinessObjectRecordHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(publishedVersion);
        records.FindByIdempotencyKeyAsync(
                BusinessObjectRecordHandlerTestContext.WorkspaceId,
                definition.Key,
                "application-1",
                Arg.Any<CancellationToken>())
            .Returns((BusinessObjectRecord?)null, null, null);

        CreateBusinessObjectRecordHandler sut = new(
            new BusinessObjectRecordHandlerTestContext.FakeCurrentUser(),
            definitions,
            records,
            unitOfWork);
        Result<BusinessObjectRecordDetailDto> created = await sut.Handle(
            new CreateBusinessObjectRecordCommand(
                definition.Key.Value,
                "application-1",
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["requested_amount"] = ["0012"],
                }),
            TestContext.Current.CancellationToken);

        created.IsSuccess.Should().BeTrue();
        persisted.Should().NotBeNull();
        BusinessObjectRecord savedRecord = persisted!;
        savedRecord.SaveDraft(
            expectedRevision: 1,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["requested_amount"] = ["13"],
            },
            BusinessObjectRecordHandlerTestContext.UserId,
            BusinessObjectRecordHandlerTestContext.Now.AddMinutes(1)).IsSuccess.Should().BeTrue();

        records.FindByIdempotencyKeyAsync(
                BusinessObjectRecordHandlerTestContext.WorkspaceId,
                definition.Key,
                "application-1",
                Arg.Any<CancellationToken>())
            .Returns(savedRecord, savedRecord);

        Result<BusinessObjectRecordDetailDto> retry = await sut.Handle(
            new CreateBusinessObjectRecordCommand(
                definition.Key.Value,
                "application-1",
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["requested_amount"] = ["0012"],
                }),
            TestContext.Current.CancellationToken);
        Result<BusinessObjectRecordDetailDto> conflict = await sut.Handle(
            new CreateBusinessObjectRecordCommand(
                definition.Key.Value,
                "application-1",
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["requested_amount"] = ["0013"],
                }),
            TestContext.Current.CancellationToken);

        retry.IsSuccess.Should().BeTrue();
        retry.Value.Id.Should().Be(created.Value.Id);
        retry.Value.Revision.Should().Be(2);
        conflict.IsFailure.Should().BeTrue();
        conflict.ErrorCode.Should().Be(ErrorCodes.Conflict);
        conflict.ProblemCode.Should().Be(BusinessObjectsProblemCodes.BusinessObjectRecordIdempotencyConflict);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
