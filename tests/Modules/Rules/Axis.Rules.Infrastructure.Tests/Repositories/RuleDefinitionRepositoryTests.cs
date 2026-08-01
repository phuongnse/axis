using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Domain;
using Axis.Rules.Infrastructure.Persistence;
using Axis.Rules.Infrastructure.Repositories;
using Axis.Rules.Infrastructure.Tests.Fixtures;
using Axis.Shared.Application;
using FluentAssertions;

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
    public async Task AddAsync_WhenRuleIsPublished_PersistsInputsAndImmutableVersion()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleDefinition definition = PublishedRule(workspaceId, UniqueKey("credit_threshold"));

        await _repository.AddAsync(definition, TestContext.Current.CancellationToken);
        await _unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using RulesDbContext reloadContext = db.CreateContext();
        RuleDefinition loaded = (await new RuleDefinitionRepository(reloadContext).GetByKeyForWorkspaceAsync(
            definition.Key,
            workspaceId,
            TestContext.Current.CancellationToken))!;

        loaded.Status.Should().Be(RuleLifecycleStatus.Published);
        loaded.Inputs.Should().Contain(input => input.Key == "threshold");
        loaded.Versions.Should().ContainSingle().Which.Condition.Should().BeOfType<RulePredicateCondition>();
    }

    [Fact]
    public async Task GetByKeyForWorkspaceAsync_WhenWorkspaceDiffers_DoesNotDiscloseDefinition()
    {
        RuleDefinition definition = PublishedRule(Guid.NewGuid(), UniqueKey("private_rule"));
        await _repository.AddAsync(definition, TestContext.Current.CancellationToken);
        await _unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        RuleDefinition? loaded = await _repository.GetByKeyForWorkspaceAsync(
            definition.Key,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task ListForWorkspaceAsync_WhenStatusFilterIsApplied_ReturnsDeterministicRows()
    {
        Guid workspaceId = Guid.NewGuid();
        RuleDefinition published = PublishedRule(workspaceId, UniqueKey("published_rule"));
        RuleDefinition draft = DraftRule(workspaceId, UniqueKey("draft_rule"), "Draft rule");
        await _repository.AddAsync(published, TestContext.Current.CancellationToken);
        await _repository.AddAsync(draft, TestContext.Current.CancellationToken);
        await _unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        int count = await _repository.CountForWorkspaceAsync(
            workspaceId,
            RuleLifecycleStatus.Draft,
            cancellationToken: TestContext.Current.CancellationToken);
        IReadOnlyList<RuleDefinition> rows = await _repository.ListForWorkspaceAsync(
            workspaceId,
            0,
            10,
            RuleLifecycleStatus.Draft,
            cancellationToken: TestContext.Current.CancellationToken);

        count.Should().Be(1);
        rows.Should().ContainSingle(definition => definition.Name == "Draft rule");
    }

    private static RuleDefinition PublishedRule(Guid workspaceId, string key)
    {
        RuleDefinition definition = DraftRule(workspaceId, key, "Credit threshold");
        RuleInputDefinition threshold = RuleInputDefinition.Create("threshold", RuleValueType.Decimal, true).Value;
        RuleInputDefinition value = RuleInputDefinition.Create("value", RuleValueType.Decimal, true).Value;
        RuleConditionNode condition = RulePredicateCondition.Create(
            "threshold_check",
            RulePredicateOperator.GreaterThan,
            RuleOperand.Input("value").Value,
            RuleOperand.Input("threshold").Value).Value;
        definition.SaveDraft(
            definition.Revision,
            definition.Name,
            definition.Description,
            [value, threshold],
            condition,
            Guid.NewGuid(),
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        definition.Publish(definition.Revision, Guid.NewGuid(), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        return definition;
    }

    private static RuleDefinition DraftRule(Guid workspaceId, string key, string name) =>
        RuleDefinition.CreateDraft(
            workspaceId,
            RuleDefinitionKey.Create(key).Value,
            name,
            $"Search document for {name}.",
            Guid.NewGuid(),
            DateTime.UtcNow).Value;

    private static string UniqueKey(string prefix) =>
        $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(63, prefix.Length + 9)];
}
