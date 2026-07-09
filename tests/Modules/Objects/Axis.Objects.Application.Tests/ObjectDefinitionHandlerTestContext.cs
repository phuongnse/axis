using Axis.Objects.Application.Repositories;
using Axis.Objects.Application.Services;
using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Objects.Application.Tests;

internal sealed class ObjectDefinitionHandlerTestContext
{
    public static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid UserId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    public IObjectDefinitionRepository Repository { get; } = Substitute.For<IObjectDefinitionRepository>();
    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
    public FakeCurrentUser CurrentUser { get; } = new()
    {
        UserId = UserId,
        workspaceId = WorkspaceId,
    };

    public static ObjectDefinition UnpublishedWithOneSave()
    {
        ObjectDefinition definition = CreateUnpublished("Customer", "customer");
        definition.SaveUnpublished(
            "Customer",
            [FieldSpec("name", "Name", order: 0)],
            expectedRevision: 1,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        return definition;
    }

    public static ObjectDefinition CreateUnpublished(string name, string key)
    {
        Result<ObjectDefinition> result = ObjectDefinition.CreateUnpublished(
            WorkspaceId,
            name,
            ObjectDefinitionKey.Create(key).Value,
            DateTime.UtcNow);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    public static ObjectFieldDefinitionInput FieldInput(string key, string label) =>
        new(key, label);

    public static ObjectFieldDefinitionInput FieldInput(
        string key,
        string label,
        ObjectFieldType fieldType,
        IReadOnlyList<ObjectFieldVariantInput>? variants = null) =>
        new(key, label, fieldType, variants);

    public static ObjectFieldDefinitionSpec FieldSpec(string key, string label, int order) =>
        new(key, label, order);

    internal sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public Guid? workspaceId { get; set; }
    }
}
