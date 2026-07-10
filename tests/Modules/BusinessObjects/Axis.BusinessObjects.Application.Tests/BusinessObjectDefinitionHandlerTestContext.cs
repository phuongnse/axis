using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Application.Services;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Rules.Application;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.BusinessObjects.Application.Tests;

internal sealed class BusinessObjectDefinitionHandlerTestContext
{
    public static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid UserId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    public IBusinessObjectDefinitionRepository Repository { get; } = Substitute.For<IBusinessObjectDefinitionRepository>();
    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
    public IRuleApplicationValidator RuleValidator { get; } =
        new RuleApplicationValidator(Substitute.For<IRuleDefinitionRepository>());
    public IBusinessObjectDefinitionInputPlanner InputPlanner =>
        new BusinessObjectDefinitionInputPlanner(RuleValidator);
    public FakeCurrentUser CurrentUser { get; } = new()
    {
        UserId = UserId,
        workspaceId = WorkspaceId,
    };

    public static BusinessObjectDefinition UnpublishedWithOneSave()
    {
        BusinessObjectDefinition definition = CreateUnpublished("Customer", "customer");
        definition.SaveUnpublished(
            "Customer",
            [FieldSpec("name", "Name", order: 0)],
            expectedRevision: 1,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        return definition;
    }

    public static BusinessObjectDefinition CreateUnpublished(string name, string key)
    {
        Result<BusinessObjectDefinition> result = BusinessObjectDefinition.CreateUnpublished(
            WorkspaceId,
            name,
            BusinessObjectDefinitionKey.Create(key).Value,
            DateTime.UtcNow);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    public static BusinessObjectFieldDefinitionInput FieldInput(string key, string label) =>
        new(key, label);

    public static BusinessObjectFieldDefinitionInput FieldInput(
        string key,
        string label,
        BusinessObjectFieldType fieldType,
        IReadOnlyList<BusinessObjectFieldRuleInput>? rules = null) =>
        new(key, label, fieldType, rules);

    public static BusinessObjectFieldDefinitionSpec FieldSpec(string key, string label, int order) =>
        new(key, label, order);

    internal sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public Guid? workspaceId { get; set; }
    }
}
