using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Application.Services;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.BusinessObjects.Infrastructure.Persistence;
using Axis.BusinessObjects.Infrastructure.Repositories;
using Axis.BusinessObjects.Infrastructure.Tests.Fixtures;
using Axis.Rules.Contracts;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using FluentAssertions.Specialized;

namespace Axis.BusinessObjects.Infrastructure.Tests.Repositories;

[Collection("BusinessObjectsDb")]
public sealed class BusinessObjectDefinitionRepositoryTests(BusinessObjectsDatabaseFixture db) : IAsyncLifetime
{
    private BusinessObjectsDbContext _ctx = null!;
    private IBusinessObjectDefinitionRepository _repository = null!;
    private IUnitOfWork _unitOfWork = null!;

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _repository = new BusinessObjectDefinitionRepository(_ctx);
        _unitOfWork = new BusinessObjectsUnitOfWork(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task AddAsync_WhenDefinitionGraphIsPublished_PersistsUnpublishedAndVersionSnapshot()
    {
        Guid workspaceId = Guid.NewGuid();
        string objectKey = UniqueKey("customer");
        BusinessObjectDefinition definition = CreateUnpublished(workspaceId, "Customer", objectKey);
        definition.SaveUnpublished(
            "Customer",
            [Field("name", "Name", 0), Field("status", "Status", 1)],
            expectedRevision: 1,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        definition.Publish(2, Guid.NewGuid(), DateTime.UtcNow).IsSuccess.Should().BeTrue();

        await _repository.AddAsync(definition);
        await _unitOfWork.SaveChangesAsync();

        await using BusinessObjectsDbContext reloadContext = db.CreateContext();
        IBusinessObjectDefinitionRepository reloadRepository = new BusinessObjectDefinitionRepository(reloadContext);
        BusinessObjectDefinition? loaded = await reloadRepository.GetByIdForWorkspaceAsync(
            definition.Id,
            workspaceId);

        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(BusinessObjectDefinitionStatus.Published);
        loaded.Fields.Should().HaveCount(2);
        loaded.Fields.Single(field => field.Key.Value == "status").Label.Should().Be("Status");
        loaded.Fields.Single(field => field.Key.Value == "status").FieldType.Should().Be(BusinessObjectFieldType.Text);
        loaded.Versions.Should().ContainSingle(version => version.VersionNumber == 1);
        loaded.Versions[0].SourceDefinitionId.Should().Be(loaded.Id);
        loaded.Versions[0].Fields.Should().HaveCount(2);
        loaded.Versions[0].Fields.Single(field => field.Key.Value == "status").Label.Should().Be("Status");
        loaded.Versions[0].Fields.Should().OnlyContain(snapshot =>
            loaded.Fields.Any(source => source.Id == snapshot.SourceFieldDefinitionId));
    }

    [Fact]
    public async Task AddAsync_WhenDefinitionHasFieldRules_PersistsCurrentAndPublishedRuleSnapshots()
    {
        Guid workspaceId = Guid.NewGuid();
        string objectKey = UniqueKey("request");
        BusinessObjectDefinition definition = CreateUnpublished(workspaceId, "Request", objectKey);
        definition.SaveUnpublished(
            "Request",
            [
                Field(
                    "amount",
                    "Amount",
                    0,
                    BusinessObjectFieldType.Decimal,
                    [Rule(RuleDefinitionKeys.NumericRange, Params(("min", ["0"]), ("max", ["100000"])))]),
                Field(
                    "status",
                    "Status",
                    1,
                    BusinessObjectFieldType.Choice,
                    choiceConfiguration: Choice(
                        BusinessObjectChoiceSelectionMode.Single,
                        ("draft", "Draft"),
                        ("submitted", "Submitted"),
                        ("approved", "Approved"))),
            ],
            expectedRevision: 1,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        definition.Publish(2, Guid.NewGuid(), DateTime.UtcNow).IsSuccess.Should().BeTrue();

        await _repository.AddAsync(definition);
        await _unitOfWork.SaveChangesAsync();

        await using BusinessObjectsDbContext reloadContext = db.CreateContext();
        IBusinessObjectDefinitionRepository reloadRepository = new BusinessObjectDefinitionRepository(reloadContext);
        BusinessObjectDefinition loaded = (await reloadRepository.GetByIdForWorkspaceAsync(
            definition.Id,
            workspaceId))!;

        BusinessObjectFieldDefinition amount = loaded.Fields.Single(field => field.Key.Value == "amount");
        amount.FieldType.Should().Be(BusinessObjectFieldType.Decimal);
        amount.Rules.Should().ContainSingle();
        amount.Rules[0].DefinitionKey.Should().Be(RuleDefinitionKeys.NumericRange);
        amount.Rules[0].DefinitionVersion.Should().Be(1);
        amount.Rules[0].Parameters["min"].Should().Equal("0");
        amount.Rules[0].Parameters["max"].Should().Equal("100000");
        BusinessObjectDefinitionVersionField statusVersionField = loaded.Versions.Single().Fields
            .Single(field => field.Key.Value == "status");
        statusVersionField.FieldType.Should().Be(BusinessObjectFieldType.Choice);
        statusVersionField.ChoiceSelectionMode.Should().Be(BusinessObjectChoiceSelectionMode.Single);
        statusVersionField.ChoiceOptions.Select(option => (option.Key.Value, option.Label))
            .Should().Equal(("draft", "Draft"), ("submitted", "Submitted"), ("approved", "Approved"));
        BusinessObjectFieldDefinition status = loaded.Fields.Single(field => field.Key.Value == "status");
        statusVersionField.SourceFieldDefinitionId.Should().Be(status.Id);
        statusVersionField.ChoiceOptions.Select(option => option.SourceChoiceOptionId)
            .Should().Equal(status.ChoiceOptions.Select(option => option.Id));
    }

    [Fact]
    public async Task SaveUnpublished_WhenExistingFieldKeyRemains_ReusesStableId()
    {
        Guid workspaceId = Guid.NewGuid();
        string objectKey = UniqueKey("account");
        BusinessObjectDefinition definition = CreateUnpublished(workspaceId, "Account", objectKey);
        definition.SaveUnpublished(
            "Account",
            [Field("status", "Status", 0)],
            expectedRevision: 1,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        BusinessObjectFieldDefinitionId originalFieldId = definition.Fields.Single().Id;

        await _repository.AddAsync(definition);
        await _unitOfWork.SaveChangesAsync();

        BusinessObjectDefinition loaded = (await _repository.GetByIdForWorkspaceAsync(definition.Id, workspaceId))!;
        loaded.SaveUnpublished(
            "Account",
            [
                Field("status", "Lifecycle status", 0) with { Id = loaded.Fields.Single().Id },
            ],
            expectedRevision: 2,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        await _unitOfWork.SaveChangesAsync();

        await using BusinessObjectsDbContext reloadContext = db.CreateContext();
        IBusinessObjectDefinitionRepository reloadRepository = new BusinessObjectDefinitionRepository(reloadContext);
        BusinessObjectDefinition reloaded = (await reloadRepository.GetByIdForWorkspaceAsync(definition.Id, workspaceId))!;
        BusinessObjectFieldDefinition field = reloaded.Fields.Single();

        field.Id.Should().Be(originalFieldId);
        field.Label.Should().Be("Lifecycle status");
    }

    [Fact]
    public async Task SaveUnpublished_WhenFieldRulesChange_ReplacesCurrentRuleConfiguration()
    {
        Guid workspaceId = Guid.NewGuid();
        string objectKey = UniqueKey("application");
        BusinessObjectDefinition definition = CreateUnpublished(workspaceId, "Application", objectKey);
        definition.SaveUnpublished(
            "Application",
            [
                Field(
                    "code",
                    "Code",
                    0,
                    BusinessObjectFieldType.Text,
                    [Rule(RuleDefinitionKeys.TextLength, Params(("min", ["2"]), ("max", ["10"])))]),
            ],
            expectedRevision: 1,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        await _repository.AddAsync(definition);
        await _unitOfWork.SaveChangesAsync();

        BusinessObjectDefinition loaded = (await _repository.GetByIdForWorkspaceAsync(definition.Id, workspaceId))!;
        loaded.SaveUnpublished(
            "Application",
            [
                Field(
                    "code",
                    "Code",
                    0,
                    BusinessObjectFieldType.Text,
                    [Rule(RuleDefinitionKeys.TextPattern, Params(("pattern", ["^[A-Z]{2}[0-9]{4}$"])))]) with
                {
                    Id = loaded.Fields.Single().Id,
                },
            ],
            expectedRevision: 2,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        await _unitOfWork.SaveChangesAsync();

        await using BusinessObjectsDbContext reloadContext = db.CreateContext();
        IBusinessObjectDefinitionRepository reloadRepository = new BusinessObjectDefinitionRepository(reloadContext);
        BusinessObjectDefinition reloaded = (await reloadRepository.GetByIdForWorkspaceAsync(definition.Id, workspaceId))!;
        BusinessObjectFieldDefinition field = reloaded.Fields.Single();

        field.Rules.Should().ContainSingle();
        field.Rules[0].DefinitionKey.Should().Be(RuleDefinitionKeys.TextPattern);
        field.Rules[0].Parameters["pattern"].Should().Equal("^[A-Z]{2}[0-9]{4}$");
    }

    [Fact]
    public async Task SaveChangesAsync_WhenWorkspaceObjectKeyConflicts_ThrowsUniqueConstraintException()
    {
        Guid workspaceId = Guid.NewGuid();
        string objectKey = UniqueKey("invoice");

        await _repository.AddAsync(CreateUnpublished(workspaceId, "Invoice", objectKey));
        await _repository.AddAsync(CreateUnpublished(workspaceId, "Duplicate invoice", objectKey));

        Func<Task> act = () => _unitOfWork.SaveChangesAsync();

        ExceptionAssertions<UniqueConstraintException> exception =
            await act.Should().ThrowAsync<UniqueConstraintException>();
        exception.Which.Message.Should().Contain(
            "IX_business_object_definitions_workspace_id_object_key");
    }

    [Fact]
    public async Task ObjectKeyExistsAsync_WhenExceptIdMatchesExistingDefinition_ReturnsFalse()
    {
        Guid workspaceId = Guid.NewGuid();
        string objectKey = UniqueKey("order");
        BusinessObjectDefinition definition = CreateUnpublished(workspaceId, "Order", objectKey);
        await _repository.AddAsync(definition);
        await _unitOfWork.SaveChangesAsync();

        bool existsWithoutExcept = await _repository.ObjectKeyExistsAsync(
            workspaceId,
            BusinessObjectDefinitionKey.Create(objectKey).Value);
        bool existsWithExcept = await _repository.ObjectKeyExistsAsync(
            workspaceId,
            BusinessObjectDefinitionKey.Create(objectKey).Value,
            definition.Id);

        existsWithoutExcept.Should().BeTrue();
        existsWithExcept.Should().BeFalse();
    }

    [Fact]
    public async Task ListForWorkspaceAsync_WhenMultipleWorkspacesExist_ReturnsScopedPagedRows()
    {
        Guid workspaceId = Guid.NewGuid();
        Guid otherWorkspaceId = Guid.NewGuid();
        BusinessObjectDefinition first = CreateUnpublished(workspaceId, "Customer", UniqueKey("customer"));
        first.SaveUnpublished(
            first.Name,
            [Field("name", "Name", 0)],
            expectedRevision: 1,
            DateTime.UtcNow.AddMinutes(-2)).IsSuccess.Should().BeTrue();
        BusinessObjectDefinition second = CreateUnpublished(workspaceId, "Invoice", UniqueKey("invoice"));
        second.SaveUnpublished(
            second.Name,
            [Field("number", "Number", 0)],
            expectedRevision: 1,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();

        await _repository.AddAsync(first);
        await _repository.AddAsync(second);
        await _repository.AddAsync(CreateUnpublished(otherWorkspaceId, "Other", UniqueKey("other")));
        await _unitOfWork.SaveChangesAsync();

        int total = await _repository.CountForWorkspaceAsync(workspaceId);
        IReadOnlyList<BusinessObjectDefinition> page = await _repository.ListForWorkspaceAsync(
            workspaceId,
            page: 1,
            pageSize: 1);

        total.Should().Be(2);
        page.Should().ContainSingle();
        page[0].Id.Should().Be(second.Id);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenConcurrentUnpublishedUpdateWins_ThrowsConcurrencyException()
    {
        Guid workspaceId = Guid.NewGuid();
        string objectKey = UniqueKey("lead");
        BusinessObjectDefinition definition = CreateUnpublished(workspaceId, "Lead", objectKey);
        await _repository.AddAsync(definition);
        await _unitOfWork.SaveChangesAsync();

        await using BusinessObjectsDbContext firstContext = db.CreateContext();
        await using BusinessObjectsDbContext secondContext = db.CreateContext();
        IBusinessObjectDefinitionRepository firstRepository = new BusinessObjectDefinitionRepository(firstContext);
        IBusinessObjectDefinitionRepository secondRepository = new BusinessObjectDefinitionRepository(secondContext);
        IUnitOfWork firstUnitOfWork = new BusinessObjectsUnitOfWork(firstContext);
        IUnitOfWork secondUnitOfWork = new BusinessObjectsUnitOfWork(secondContext);

        BusinessObjectDefinition first = (await firstRepository.GetByIdForWorkspaceAsync(definition.Id, workspaceId))!;
        BusinessObjectDefinition second = (await secondRepository.GetByIdForWorkspaceAsync(definition.Id, workspaceId))!;
        first.SaveUnpublished(
            "Lead",
            [Field("name", "Name", 0)],
            expectedRevision: 1,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        second.SaveUnpublished(
            "Lead",
            [Field("company", "Company", 0)],
            expectedRevision: 1,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();

        await firstUnitOfWork.SaveChangesAsync();
        Func<Task> act = () => secondUnitOfWork.SaveChangesAsync();

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    private static BusinessObjectDefinition CreateUnpublished(Guid workspaceId, string name, string key)
    {
        Result<BusinessObjectDefinition> result = BusinessObjectDefinition.CreateUnpublished(
            workspaceId,
            name,
            BusinessObjectDefinitionKey.Create(key).Value,
            DateTime.UtcNow);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static BusinessObjectFieldDefinitionSpec Field(
        string key,
        string label,
        int order,
        BusinessObjectFieldType fieldType = BusinessObjectFieldType.Text,
        IReadOnlyList<BusinessObjectFieldRuleSpec>? rules = null,
        BusinessObjectChoiceFieldConfigurationSpec? choiceConfiguration = null) =>
        new(key, label, order, fieldType, rules, choiceConfiguration);

    private static BusinessObjectFieldRuleSpec Rule(
        string definitionKey,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? parameters = null) =>
        new(definitionKey, DefinitionVersion: 1, parameters);

    private static BusinessObjectChoiceFieldConfigurationSpec Choice(
        BusinessObjectChoiceSelectionMode selectionMode,
        params (string Key, string Label)[] options) =>
        new(
            selectionMode,
            options.Select((option, index) => new BusinessObjectChoiceOptionSpec(
                option.Key,
                option.Label,
                index)).ToArray());

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Params(
        params (string Key, string[] Values)[] parameters) =>
        parameters.ToDictionary(
            parameter => parameter.Key,
            parameter => (IReadOnlyList<string>)parameter.Values,
            StringComparer.Ordinal);

    private static string UniqueKey(string prefix) =>
        $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(63, prefix.Length + 9)];
}
