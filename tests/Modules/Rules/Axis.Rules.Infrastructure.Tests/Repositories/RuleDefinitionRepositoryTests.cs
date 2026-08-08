using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Domain;
using Axis.Rules.Infrastructure.Persistence;
using Axis.Rules.Infrastructure.Repositories;
using Axis.Rules.Infrastructure.Tests.Fixtures;
using Axis.Shared.Application;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Axis.Rules.Infrastructure.Tests.Repositories;

[Collection("RulesDb")]
public sealed class RuleDefinitionRepositoryTests(RulesDatabaseFixture db) : IAsyncLifetime
{
    private RulesDbContext _context = null!;
    private IRuleDefinitionRepository _repository = null!;
    private IUnitOfWork _unitOfWork = null!;

    public ValueTask InitializeAsync()
    {
        _context = db.CreateContext();
        _repository = new RuleDefinitionRepository(_context);
        _unitOfWork = new RulesUnitOfWork(_context);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task AddAsync_WhenDefinitionTransitionsLifecycle_PersistsDerivedStateAndArchiveData()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleSubjectReference serviceActor = RuleSubjectReference.Service(Guid.NewGuid());
        RuleDefinition definition = ConfiguredDraft(workspaceId, UniqueKey("credit_threshold"), "Credit threshold");
        CreateVersion(definition, serviceActor);
        definition.ActivateVersion(definition.Revision, 1, serviceActor, DateTime.UtcNow).IsSuccess.Should().BeTrue();

        await _repository.AddAsync(definition, TestContext.Current.CancellationToken);
        await _unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using RulesDbContext activeReloadContext = db.CreateContext();
        RuleDefinition active = (await new RuleDefinitionRepository(activeReloadContext).GetByKeyForWorkspaceAsync(
            definition.Key,
            workspaceId,
            TestContext.Current.CancellationToken))!;

        active.Status.Should().Be(RuleLifecycleStatus.Active);
        active.ActiveVersion.Should().Be(1);
        active.Versions.Should().ContainSingle().Which.Condition.Should().BeOfType<RulePredicateCondition>();

        active.Versions.Single().PublishedBySubject.Should().Be(serviceActor);
        active.Archive(active.Revision, serviceActor, DateTime.UtcNow).IsSuccess.Should().BeTrue();
        await new RulesUnitOfWork(activeReloadContext).SaveChangesAsync(TestContext.Current.CancellationToken);

        await using RulesDbContext archivedReloadContext = db.CreateContext();
        RuleDefinition archived = (await new RuleDefinitionRepository(archivedReloadContext).GetByKeyForWorkspaceAsync(
            definition.Key,
            workspaceId,
            TestContext.Current.CancellationToken))!;

        archived.Status.Should().Be(RuleLifecycleStatus.Archived);
        archived.ActiveVersion.Should().BeNull();
        archived.ArchivedBySubject.Should().Be(serviceActor);
        archived.ArchivedAt.Should().NotBeNull();
        archived.FindVersion(1).Should().NotBeNull();
    }

    [Fact]
    public async Task GetByKeyForWorkspaceAsync_WhenWorkspaceDiffers_DoesNotDiscloseDefinition()
    {
        RuleDefinition definition = ConfiguredDraft(Guid.NewGuid(), UniqueKey("private_rule"), "Private rule");
        await _repository.AddAsync(definition, TestContext.Current.CancellationToken);
        await _unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        RuleDefinition? loaded = await _repository.GetByKeyForWorkspaceAsync(
            definition.Key,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task ListForWorkspaceAsync_DerivedLifecycleFilters_ReturnMatchingStablePages()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleDefinition draft = ConfiguredDraft(workspaceId, UniqueKey("draft"), "Same name");
        RuleDefinition inactive = ConfiguredDraft(workspaceId, UniqueKey("inactive"), "Same name");
        CreateVersion(inactive);
        RuleDefinition active = ConfiguredDraft(workspaceId, UniqueKey("active"), "Same name");
        CreateVersion(active);
        active.ActivateVersion(active.Revision, 1, RuleSubjectReference.Human(Guid.NewGuid()), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        RuleDefinition archived = ConfiguredDraft(workspaceId, UniqueKey("archived"), "Same name");
        CreateVersion(archived);
        archived.ActivateVersion(archived.Revision, 1, RuleSubjectReference.Human(Guid.NewGuid()), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        archived.Archive(archived.Revision, RuleSubjectReference.Human(Guid.NewGuid()), DateTime.UtcNow).IsSuccess.Should().BeTrue();

        await _repository.AddAsync(draft, TestContext.Current.CancellationToken);
        await _repository.AddAsync(inactive, TestContext.Current.CancellationToken);
        await _repository.AddAsync(active, TestContext.Current.CancellationToken);
        await _repository.AddAsync(archived, TestContext.Current.CancellationToken);
        await _unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        foreach (RuleLifecycleStatus status in Enum.GetValues<RuleLifecycleStatus>())
        {
            int count = await _repository.CountForWorkspaceAsync(
                workspaceId,
                status,
                cancellationToken: TestContext.Current.CancellationToken);
            IReadOnlyList<RuleDefinition> rows = await _repository.ListForWorkspaceAsync(
                workspaceId,
                0,
                10,
                status,
                cancellationToken: TestContext.Current.CancellationToken);

            count.Should().Be(1, status.ToString());
            rows.Should().ContainSingle().Which.Status.Should().Be(status);
        }

        IReadOnlyList<RuleDefinition> firstPage = await _repository.ListForWorkspaceAsync(
            workspaceId, 0, 2, cancellationToken: TestContext.Current.CancellationToken);
        IReadOnlyList<RuleDefinition> secondPage = await _repository.ListForWorkspaceAsync(
            workspaceId, 2, 2, cancellationToken: TestContext.Current.CancellationToken);

        firstPage.Select(definition => definition.Id)
            .Concat(secondPage.Select(definition => definition.Id))
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task SaveChangesAsync_WhenDraftIsRevised_PreservesPersistedImmutableVersion()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleDefinition definition = ConfiguredDraft(workspaceId, UniqueKey("snapshot"), "Original name");
        CreateVersion(definition);
        definition.SaveDraft(
            definition.Revision,
            "Revised name",
            "Revised description.",
            [Input("threshold"), Input("value")],
            Condition(),
            RuleSubjectReference.Human(Guid.NewGuid()),
            DateTime.UtcNow).IsSuccess.Should().BeTrue();

        await _repository.AddAsync(definition, TestContext.Current.CancellationToken);
        await _unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using RulesDbContext reloadContext = db.CreateContext();
        RuleDefinition loaded = (await new RuleDefinitionRepository(reloadContext).GetByKeyForWorkspaceAsync(
            definition.Key,
            workspaceId,
            TestContext.Current.CancellationToken))!;

        loaded.Name.Should().Be("Revised name");
        RuleDefinitionVersion snapshot = loaded.FindVersion(1)!;
        snapshot.Should().NotBeNull();
        snapshot.Name.Should().Be("Original name");
        snapshot.Description.Should().Be("Search document for Original name.");
    }

    [Fact]
    public void Model_LifecycleStorage_MapsImmutableVersions()
    {
        IEntityType definition = _context.Model.FindEntityType(typeof(RuleDefinition))!;
        IEntityType version = _context.Model.FindEntityType(typeof(RuleDefinitionVersion))!;

        definition.FindProperty(nameof(RuleDefinition.Status)).Should().BeNull();
        definition.FindProperty(nameof(RuleDefinition.ActiveVersion))!.GetColumnName().Should().Be("active_version");
        version.GetProperties()
            .Should().OnlyContain(property => property.GetAfterSaveBehavior() == PropertySaveBehavior.Throw);
    }

    private static RuleDefinition ConfiguredDraft(Guid workspaceId, string key, string name)
    {
        RuleDefinition definition = RuleDefinition.CreateDraft(
            workspaceId,
            RuleDefinitionKey.Create(key).Value,
            name,
            $"Search document for {name}.",
            RuleSubjectReference.Human(Guid.NewGuid()),
            DateTime.UtcNow).Value;
        definition.SaveDraft(
            definition.Revision,
            name,
            definition.Description,
            [Input("value"), Input("threshold")],
            Condition(),
            RuleSubjectReference.Human(Guid.NewGuid()),
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        return definition;
    }

    private static void CreateVersion(RuleDefinition definition, RuleSubjectReference? actor = null) =>
        definition.CreateVersion(
            definition.Revision,
            actor ?? RuleSubjectReference.Human(Guid.NewGuid()),
            DateTime.UtcNow).IsSuccess.Should().BeTrue();

    private static RuleInputDefinition Input(string key) =>
        RuleInputDefinition.Create(key, key, RuleValueType.Decimal, true).Value;

    private static RuleConditionNode Condition() =>
        RulePredicateCondition.Create(
            "threshold_check",
            RulePredicateOperator.GreaterThan,
            RuleOperand.Input("value").Value,
            RuleOperand.Input("threshold").Value).Value;

    private static string UniqueKey(string prefix) =>
        $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(63, prefix.Length + 9)];
}
