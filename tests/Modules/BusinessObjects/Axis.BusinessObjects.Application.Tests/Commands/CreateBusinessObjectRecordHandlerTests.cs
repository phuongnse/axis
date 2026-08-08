using Axis.Authorization.Contracts;
using Axis.BusinessObjects.Application.Commands.CreateBusinessObjectRecord;
using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Application.Services;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Identity.Contracts;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using DomainSubjectReference = Axis.BusinessObjects.Domain.ValueObjects.SubjectReference;
using IdentitySubjectKind = Axis.Identity.Contracts.SubjectKind;
using IdentitySubjectReference = Axis.Identity.Contracts.SubjectReference;

namespace Axis.BusinessObjects.Application.Tests.Commands;

public sealed class CreateBusinessObjectRecordHandlerTests
{
    [Fact]
    public async Task CreateRecord_WhenServiceHasExactOwnAction_StampsServiceOwnerServerSide()
    {
        Guid serviceId = Guid.Parse("55555555-5555-4555-8555-555555555555");
        BusinessObjectDefinition definition =
            BusinessObjectRecordHandlerTestContext.CreatePublishedDefinition();
        IBusinessObjectDefinitionRepository definitions = Substitute.For<IBusinessObjectDefinitionRepository>();
        IBusinessObjectRecordRepository records = Substitute.For<IBusinessObjectRecordRepository>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        IProductAuthorizationService authorization = BusinessObjectRecordHandlerTestContext.AllowedAuthorization();
        BusinessObjectRecord? persisted = null;
        definitions.GetByKeyForWorkspaceAsync(
                definition.Key,
                BusinessObjectRecordHandlerTestContext.WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);
        records.AddAsync(
                Arg.Do<BusinessObjectRecord>(record => persisted = record),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        BusinessObjectRecordHandlerTestContext.FakeCurrentSubject subject = new()
        {
            Subject = IdentitySubjectReference.Service(serviceId),
        };
        CreateBusinessObjectRecordHandler sut = new(
            new BusinessObjectRecordHandlerTestContext.FakeCurrentUser(),
            subject,
            authorization,
            definitions,
            records,
            unitOfWork);

        Result<BusinessObjectRecordDetailDto> result = await sut.Handle(
            new CreateBusinessObjectRecordCommand(
                definition.Key.Value,
                "service-record",
                new Dictionary<string, IReadOnlyList<string>> { ["display_name"] = ["Service record"] },
                "create-service-record"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        persisted!.Owner.Should().Be(DomainSubjectReference.Service(serviceId));
        result.Value.CreatedBySubject.Should().Be(new SubjectReferenceDto(IdentitySubjectKind.Service, serviceId));
        await authorization.Received(1).AuthorizeAsync(
            Arg.Is<ProductAuthorizationRequest>(request =>
                request.ActionKey == BusinessObjectProductActions.RecordCreate
                && request.ResourceType == BusinessObjectProductActions.RecordResourceType
                && request.ResourceKey == definition.Key.Value
                && request.Subject == IdentitySubjectReference.Service(serviceId)
                && request.CorrelationId == "create-service-record"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateRecord_WhenValueArrayIsNull_ReturnsInvalidInputBeforeRepositoryAccess()
    {
        IBusinessObjectDefinitionRepository definitions = Substitute.For<IBusinessObjectDefinitionRepository>();
        IBusinessObjectRecordRepository records = Substitute.For<IBusinessObjectRecordRepository>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        CreateBusinessObjectRecordHandler sut = new(
            new BusinessObjectRecordHandlerTestContext.FakeCurrentUser(),
            new BusinessObjectRecordHandlerTestContext.FakeCurrentSubject(),
            BusinessObjectRecordHandlerTestContext.AllowedAuthorization(),
            definitions,
            records,
            unitOfWork);

        Result<BusinessObjectRecordDetailDto> result = await sut.Handle(
            new CreateBusinessObjectRecordCommand(
                "customer",
                "record-null-values",
                new Dictionary<string, IReadOnlyList<string>> { ["quantity"] = null! }),
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
        await records.DidNotReceive().FindByIdempotencyKeyAsync(
            Arg.Any<Guid>(),
            Arg.Any<BusinessObjectDefinitionKey>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

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
                "record-1",
                Arg.Any<CancellationToken>())
            .Returns((BusinessObjectRecord?)null, null, null);

        CreateBusinessObjectRecordHandler sut = new(
            new BusinessObjectRecordHandlerTestContext.FakeCurrentUser(),
            new BusinessObjectRecordHandlerTestContext.FakeCurrentSubject(),
            BusinessObjectRecordHandlerTestContext.AllowedAuthorization(),
            definitions,
            records,
            unitOfWork);
        Result<BusinessObjectRecordDetailDto> created = await sut.Handle(
            new CreateBusinessObjectRecordCommand(
                definition.Key.Value,
                "record-1",
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["quantity"] = ["0012"],
                }),
            TestContext.Current.CancellationToken);

        created.IsSuccess.Should().BeTrue();
        persisted.Should().NotBeNull();
        BusinessObjectRecord savedRecord = persisted!;
        savedRecord.SaveDraft(
            expectedRevision: 1,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["quantity"] = ["13"],
            },
            DomainSubjectReference.Human(BusinessObjectRecordHandlerTestContext.UserId),
            BusinessObjectRecordHandlerTestContext.Now.AddMinutes(1)).IsSuccess.Should().BeTrue();

        records.FindByIdempotencyKeyAsync(
                BusinessObjectRecordHandlerTestContext.WorkspaceId,
                definition.Key,
                "record-1",
                Arg.Any<CancellationToken>())
            .Returns(savedRecord, savedRecord);

        Result<BusinessObjectRecordDetailDto> retry = await sut.Handle(
            new CreateBusinessObjectRecordCommand(
                definition.Key.Value,
                "record-1",
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["quantity"] = ["0012"],
                }),
            TestContext.Current.CancellationToken);
        Result<BusinessObjectRecordDetailDto> conflict = await sut.Handle(
            new CreateBusinessObjectRecordCommand(
                definition.Key.Value,
                "record-1",
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["quantity"] = ["0013"],
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
