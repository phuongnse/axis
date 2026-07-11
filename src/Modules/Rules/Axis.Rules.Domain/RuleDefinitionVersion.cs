using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public sealed class RuleDefinitionVersion : Entity<RuleDefinitionVersionId>
{
    private readonly List<RuleParameterDefinition> _parameters = [];

    public RuleDefinitionId DefinitionId { get; private set; }
    public int Version { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public RuleScope Scope { get; private set; }
    public RuleContextKey ContextKey { get; private set; }
    public int ContextSchemaVersion { get; private set; }
    public RuleOutcomeKind OutcomeKind { get; private set; }
    public RuleConditionNode Condition { get; private set; }
    public RuleOutcome Outcome { get; private set; }
    public IReadOnlyList<RuleParameterDefinition> Parameters => _parameters.AsReadOnly();
    public Guid PublishedByUserId { get; private set; }
    public DateTime PublishedAt { get; private set; }

    private RuleDefinitionVersion()
        : base(default)
    {
        Name = string.Empty;
        Description = string.Empty;
        ContextKey = default;
        Condition = null!;
        Outcome = null!;
    }

    private RuleDefinitionVersion(
        RuleDefinitionVersionId id,
        RuleDefinitionId definitionId,
        int version,
        string name,
        string description,
        RuleScope scope,
        RuleContextKey contextKey,
        int contextSchemaVersion,
        RuleOutcomeKind outcomeKind,
        IReadOnlyList<RuleParameterDefinition> parameters,
        RuleConditionNode condition,
        RuleOutcome outcome,
        Guid publishedByUserId,
        DateTime publishedAt)
        : base(id)
    {
        DefinitionId = definitionId;
        Version = version;
        Name = name;
        Description = description;
        Scope = scope;
        ContextKey = contextKey;
        ContextSchemaVersion = contextSchemaVersion;
        OutcomeKind = outcomeKind;
        _parameters.AddRange(parameters);
        Condition = condition;
        Outcome = outcome;
        PublishedByUserId = publishedByUserId;
        PublishedAt = publishedAt;
    }

    internal static RuleDefinitionVersion Create(
        RuleDefinition definition,
        int version,
        Guid publishedByUserId,
        DateTime publishedAt) =>
        new(
            RuleDefinitionVersionId.New(),
            definition.Id,
            version,
            definition.Name,
            definition.Description,
            definition.Scope,
            definition.ContextKey,
            definition.ContextSchemaVersion,
            definition.OutcomeKind,
            definition.Parameters.ToArray(),
            definition.Condition!,
            definition.Outcome!,
            publishedByUserId,
            publishedAt);
}
