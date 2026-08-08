using Axis.Rules.Domain;
using Axis.Rules.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Axis.Rules.Infrastructure.Tests.Persistence;

public sealed class RulesSubjectModelTests
{
    [Fact]
    public void SubjectActors_WhenModelBuilt_UseDiscriminatedColumns()
    {
        DbContextOptions<RulesDbContext> options = new DbContextOptionsBuilder<RulesDbContext>()
            .UseNpgsql("Host=localhost;Database=axis_rules_model;Username=axis;Password=unused")
            .Options;
        using RulesDbContext context = new(options);

        IEntityType definition = context.Model.FindEntityType(typeof(RuleDefinition))!;
        IEntityType version = context.Model.FindEntityType(typeof(RuleDefinitionVersion))!;
        IEntityType binding = context.Model.FindEntityType(typeof(RuleBinding))!;

        Columns(definition, nameof(RuleDefinition.CreatedBySubject))
            .Should().Equal("created_by_subject_id", "created_by_subject_kind");
        Columns(definition, nameof(RuleDefinition.UpdatedBySubject))
            .Should().Equal("updated_by_subject_id", "updated_by_subject_kind");
        definition.FindProperty("ArchivedBySubjectKind")!.GetColumnName().Should().Be("archived_by_subject_kind");
        definition.FindProperty("ArchivedBySubjectId")!.GetColumnName().Should().Be("archived_by_subject_id");
        IProperty publishedKind = version.FindProperty("PublishedBySubjectKind")!;
        IProperty publishedId = version.FindProperty("PublishedBySubjectId")!;
        publishedKind.GetColumnName().Should().Be("published_by_subject_kind");
        publishedId.GetColumnName().Should().Be("published_by_subject_id");
        publishedKind.IsNullable.Should().BeFalse();
        publishedId.IsNullable.Should().BeFalse();
        Columns(binding, nameof(RuleBinding.CreatedBySubject))
            .Should().Equal("created_by_subject_id", "created_by_subject_kind");
        Columns(binding, nameof(RuleBinding.UpdatedBySubject))
            .Should().Equal("updated_by_subject_id", "updated_by_subject_kind");
    }

    private static IReadOnlyList<string> Columns(IEntityType entity, string complexProperty) =>
        entity.FindComplexProperty(complexProperty)!
            .ComplexType.GetProperties()
            .Select(property => property.GetColumnName())
            .Order(StringComparer.Ordinal)
            .ToArray();
}
