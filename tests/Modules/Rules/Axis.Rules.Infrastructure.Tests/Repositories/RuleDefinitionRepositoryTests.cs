using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Domain;
using Axis.Rules.Infrastructure.Persistence;
using Axis.Rules.Infrastructure.Repositories;
using Axis.Rules.Infrastructure.Tests.Fixtures;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Rules.Infrastructure.Tests.Repositories;

[Collection("RulesDb")]
public sealed class RuleDefinitionRepositoryTests(RulesDatabaseFixture db) : IAsyncLifetime
{
    private RulesDbContext _context = null!;
    private IRuleDefinitionRepository _repository = null!;
    private IUnitOfWork _unitOfWork = null!;

    public Task InitializeAsync()
    {
        _context = db.CreateContext();
        _repository = new RuleDefinitionRepository(_context);
        _unitOfWork = new RulesUnitOfWork(_context);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task AddAsync_WhenRuleIsPublished_PersistsCanonicalDraftAndImmutableVersion()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleDefinition definition = PublishedRule(workspaceId, UniqueKey("credit_threshold"));

        await _repository.AddAsync(definition);
        await _unitOfWork.SaveChangesAsync();

        await using RulesDbContext reloadContext = db.CreateContext();
        IRuleDefinitionRepository reloadRepository = new RuleDefinitionRepository(reloadContext);
        RuleDefinition loaded = (await reloadRepository.GetByKeyForWorkspaceAsync(
            definition.Key,
            workspaceId))!;

        loaded.Status.Should().Be(RuleLifecycleStatus.Published);
        loaded.Parameters.Should().ContainSingle(parameter =>
            parameter.Key == "threshold" && parameter.Type == RuleValueType.Decimal);
        RuleDefinitionVersion version = loaded.Versions.Should().ContainSingle().Subject;
        version.Version.Should().Be(1);
        version.Condition.Should().BeOfType<RulePredicateCondition>();
        version.Outcome.Should().BeOfType<RuleValidationOutcome>();
        version.PublishedByUserId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetByKeyForWorkspaceAsync_WhenWorkspaceDiffers_DoesNotDiscloseDefinition()
    {
        RuleDefinition definition = PublishedRule(Guid.NewGuid(), UniqueKey("private_rule"));
        await _repository.AddAsync(definition);
        await _unitOfWork.SaveChangesAsync();

        RuleDefinition? loaded = await _repository.GetByKeyForWorkspaceAsync(
            definition.Key,
            Guid.NewGuid());

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task SaveChangesAsync_WhenConcurrentDraftTransitionLoses_ThrowsConcurrencyException()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleDefinition definition = PublishedRule(workspaceId, UniqueKey("concurrent_rule"));
        await _repository.AddAsync(definition);
        await _unitOfWork.SaveChangesAsync();

        await using RulesDbContext firstContext = db.CreateContext();
        await using RulesDbContext secondContext = db.CreateContext();
        IRuleDefinitionRepository firstRepository = new RuleDefinitionRepository(firstContext);
        IRuleDefinitionRepository secondRepository = new RuleDefinitionRepository(secondContext);
        IUnitOfWork firstUnitOfWork = new RulesUnitOfWork(firstContext);
        IUnitOfWork secondUnitOfWork = new RulesUnitOfWork(secondContext);
        RuleDefinition first = (await firstRepository.GetByKeyForWorkspaceAsync(
            definition.Key,
            workspaceId))!;
        RuleDefinition second = (await secondRepository.GetByKeyForWorkspaceAsync(
            definition.Key,
            workspaceId))!;
        Guid userId = Guid.NewGuid();
        first.StartNextDraft(first.Revision, userId, DateTime.UtcNow).IsSuccess.Should().BeTrue();
        second.StartNextDraft(second.Revision, userId, DateTime.UtcNow).IsSuccess.Should().BeTrue();

        await firstUnitOfWork.SaveChangesAsync();
        Func<Task> act = () => secondUnitOfWork.SaveChangesAsync();

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    [Fact]
    public async Task ListForWorkspaceAsync_WhenFiltersAreApplied_ReturnsDeterministicWorkspaceRows()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleDefinition published = PublishedRule(workspaceId, UniqueKey("published_rule"));
        Result<RuleDefinition> draftResult = RuleDefinition.CreateDraft(
            workspaceId,
            RuleDefinitionKey.Create(UniqueKey("draft_rule")).Value,
            "Draft rule",
            "A draft workspace rule.",
            RuleScope.Record,
            RuleContextKey.Create("objects.record").Value,
            1,
            RuleOutcomeKind.Decision,
            Guid.NewGuid(),
            DateTime.UtcNow);
        draftResult.IsSuccess.Should().BeTrue();
        await _repository.AddAsync(published);
        await _repository.AddAsync(draftResult.Value);
        await _unitOfWork.SaveChangesAsync();

        int count = await _repository.CountForWorkspaceAsync(
            workspaceId,
            RuleScope.Record,
            RuleLifecycleStatus.Draft);
        IReadOnlyList<RuleDefinition> rows = await _repository.ListForWorkspaceAsync(
            workspaceId,
            skip: 0,
            take: 10,
            RuleScope.Record,
            RuleLifecycleStatus.Draft);

        count.Should().Be(1);
        rows.Should().ContainSingle(definition => definition.Name == "Draft rule");
    }

    private static RuleDefinition PublishedRule(Guid workspaceId, string key)
    {
        Guid userId = Guid.NewGuid();
        Result<RuleDefinition> created = RuleDefinition.CreateDraft(
            workspaceId,
            RuleDefinitionKey.Create(key).Value,
            "Credit threshold",
            "Flags values above a configured threshold.",
            RuleScope.Field,
            RuleContextKey.Create("business_objects.field.decimal").Value,
            1,
            RuleOutcomeKind.Validation,
            userId,
            DateTime.UtcNow);
        created.IsSuccess.Should().BeTrue();

        RuleParameterDefinition parameter = RuleParameterDefinition.Create(
            "threshold",
            RuleValueType.Decimal,
            isRequired: true).Value;
        RulePredicateCondition condition = RulePredicateCondition.Create(
            "threshold_check",
            RulePredicateOperator.GreaterThan,
            RuleOperand.Context("field.value").Value,
            RuleOperand.Parameter("threshold").Value).Value;
        RuleValidationOutcome outcome = RuleValidationOutcome.Create(
            "credit.threshold.exceeded",
            RuleSeverity.Error,
            "Credit value exceeds the configured threshold.").Value;
        created.Value.SaveDraft(
            expectedRevision: 1,
            created.Value.Name,
            created.Value.Description,
            created.Value.Scope,
            created.Value.ContextKey,
            created.Value.ContextSchemaVersion,
            created.Value.OutcomeKind,
            [parameter],
            condition,
            outcome,
            userId,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        created.Value.Publish(
            expectedRevision: 2,
            userId,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        return created.Value;
    }

    private static string UniqueKey(string prefix) =>
        $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(63, prefix.Length + 9)];
}
