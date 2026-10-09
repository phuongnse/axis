using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Data.Naming;
using Axis.Data.Schema;

namespace Axis.Data.Tests;

/// <summary>Builds compiled models in code, and the catalog and records a provisioned model leaves behind.</summary>
internal static class Models
{
    public static readonly Guid ApplicationId = Guid.Parse("0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01");

    public static ApplicationModel Application(params EntityModel[] entities) =>
        new()
        {
            Manifest = new ApplicationManifest
            {
                Id = ApplicationId,
                Kind = ResourceKinds.Application,
                Name = "Sample",
                FormatVersion = 1,
                File = "application.json",
            },
            Entities = entities,
        };

    public static EntityModel Entity(Guid id, string name, string file, params FieldModel[] fields) =>
        Entity(id, name, file, displayField: null, fields);

    public static EntityModel Entity(Guid id, string name, string file, string? displayField, params FieldModel[] fields) =>
        new() { Id = id, Name = name, File = file, Fields = fields, DisplayField = displayField };

    public static FieldModel Field(
        string name,
        FieldType type,
        bool required = false,
        bool unique = false,
        int? maxLength = null,
        int? precision = null,
        int? scale = null,
        IReadOnlyList<string>? values = null,
        EntityModel? target = null) =>
        new()
        {
            Name = name,
            Type = type,
            Required = required,
            Unique = unique,
            MaxLength = maxLength,
            Precision = precision,
            Scale = scale,
            Values = values,
            Target = target is null ? null : new EntityReference(target.Id, target.Name),
            TargetDisplayField = target?.DisplayField,
        };

    /// <summary>
    /// Makes <paramref name="field"/> a computed field whose <paramref name="expression"/> reads
    /// <paramref name="inputs"/>, as the compiler does with an entity's fields that are not computed.
    /// <paramref name="childOf"/> finds the child entity of each child collection in the inputs, so
    /// that aggregates can name it.
    /// </summary>
    public static FieldModel Computed(
        FieldModel field, string expression, FieldModel[] inputs, Func<FieldModel, EntityModel?>? childOf = null) =>
        field with
        {
            Computed = ComputedFieldModel.Compile(
                expression, ExpressionScopes.ForEntity(inputs, childOf: childOf), ExpressionScopes.TypeOf(field)!),
        };

    /// <summary>The catalog after the model's tables were created as planned.</summary>
    public static CatalogSnapshot Catalog(ApplicationModel model, bool hasRows) =>
        new(model.Entities.Select(entity => new CatalogTable(
            EntityNaming.Table(entity.Id),
            [
                new CatalogColumn(EntityNaming.IdColumn, "uuid", NotNull: true, Unique: false, ReferencedTable: null),
                .. SystemColumns(model, entity),
                .. entity.Fields.Where(field => field.HasColumn).Select(field => new CatalogColumn(
                    EntityNaming.Column(field.Name),
                    ColumnTypes.Render(field),
                    field.Required,
                    field.Unique,
                    field.Target is null ? null : EntityNaming.Table(field.Target.Id))),
            ],
            hasRows)).ToList());

    /// <summary>The owner of a child entity, found from the child collection that names it as the planner does.</summary>
    public static EntityModel? Owner(ApplicationModel model, EntityModel entity) =>
        model.Entities.FirstOrDefault(owner => owner.Fields.Any(field => field.Type == FieldType.ChildCollection && field.Target?.Id == entity.Id));

    // The version column, or the owner and position columns of a child table.
    private static CatalogColumn[] SystemColumns(ApplicationModel model, EntityModel entity) =>
        Owner(model, entity) is { } owner
            ?
            [
                new CatalogColumn(EntityNaming.OwnerColumn, "uuid", NotNull: true, Unique: false, ReferencedTable: EntityNaming.Table(owner.Id)),
                new CatalogColumn(EntityNaming.PositionColumn, "integer", NotNull: true, Unique: false, ReferencedTable: null),
            ]
            : [new CatalogColumn(EntityNaming.VersionColumn, "bigint", NotNull: true, Unique: false, ReferencedTable: null)];

    /// <summary>The records written when the model was first provisioned.</summary>
    public static ProvisioningRecords Records(ApplicationModel model)
    {
        var plan = SchemaPlanner.Plan(model, CatalogSnapshot.Empty, ProvisioningRecords.Empty);
        Assert.Empty(plan.Diagnostics);
        return new ProvisioningRecords(plan.NewEntities, plan.NewEnumValues);
    }
}
