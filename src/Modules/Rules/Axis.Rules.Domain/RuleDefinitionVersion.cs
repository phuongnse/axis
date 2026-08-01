using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public sealed class RuleDefinitionVersion : Entity<RuleDefinitionVersionId>
{
    private readonly List<RuleInputDefinition> _inputs = [];

    public RuleDefinitionId DefinitionId { get; private set; }
    public int Version { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public int ExpressionLanguageVersion { get; private set; }
    public RuleConditionNode Condition { get; private set; }
    public RuleOutputContract Output { get; private set; }
    public IReadOnlyList<RuleInputDefinition> Inputs => _inputs.AsReadOnly();
    public Guid PublishedByUserId { get; private set; }
    public DateTime PublishedAt { get; private set; }

    private RuleDefinitionVersion()
        : base(default)
    {
        Name = string.Empty;
        Description = string.Empty;
        Condition = null!;
        Output = RuleOutputContract.BooleanMatch;
    }

    private RuleDefinitionVersion(
        RuleDefinitionVersionId id,
        RuleDefinitionId definitionId,
        int version,
        string name,
        string description,
        int expressionLanguageVersion,
        IReadOnlyList<RuleInputDefinition> inputs,
        RuleConditionNode condition,
        RuleOutputContract output,
        Guid publishedByUserId,
        DateTime publishedAt)
        : base(id)
    {
        DefinitionId = definitionId;
        Version = version;
        Name = name;
        Description = description;
        ExpressionLanguageVersion = expressionLanguageVersion;
        _inputs.AddRange(inputs);
        Condition = condition;
        Output = output;
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
            definition.ExpressionLanguageVersion,
            definition.Inputs.ToArray(),
            definition.Condition!,
            definition.Output,
            publishedByUserId,
            publishedAt);
}
