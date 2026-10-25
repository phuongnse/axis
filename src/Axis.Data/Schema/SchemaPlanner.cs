using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Data.Naming;

namespace Axis.Data.Schema;

/// <summary>
/// Plans the entity table changes for a compiled application by comparing it with the tenant
/// catalog and the provisioning records. The pending <c>renameField</c>, <c>dropField</c>,
/// <c>dropEntity</c> and <c>fill</c> migrations are applied to the catalog and the records first, and
/// their commands come first in the plan. A field that a <c>fill</c> covers may become required. Other changes are additive only; anything that could lose or
/// reject existing data is reported as a diagnostic, and then nothing is planned at all.
/// The indexes of a table, named with the <c>ix_</c> prefix, follow the release: the planner creates the ones it
/// lacks and drops the ones it does not declare, and a unique index on a table with rows is reported.
/// </summary>
public static class SchemaPlanner
{
    // The version column definition, rendered the same in CREATE TABLE and ADD COLUMN.
    private static readonly string _versionDefinition = $"{EntityNaming.Quote(EntityNaming.VersionColumn)} bigint NOT NULL DEFAULT 1";

    // The computed release column definition: nullable and without a default, so existing rows stay unstamped.
    private static readonly string _computedReleaseDefinition = $"{EntityNaming.Quote(EntityNaming.ComputedReleaseColumn)} uuid";

    // The system columns of a child table, in place of the version column.
    private static readonly string _ownerDefinition = $"{EntityNaming.Quote(EntityNaming.OwnerColumn)} uuid NOT NULL";
    private static readonly string _positionDefinition = $"{EntityNaming.Quote(EntityNaming.PositionColumn)} integer NOT NULL";

    public static SchemaPlan Plan(ApplicationModel model, CatalogSnapshot catalog, ProvisioningRecords records) =>
        Plan(model, catalog, records, [], new Dictionary<string, Guid>());

    /// <summary>
    /// Plans <paramref name="model"/> after applying <paramref name="pending"/>, which come in path
    /// order. <paramref name="storedEntityIds"/> maps an entity name to the id its stored table was
    /// provisioned under, as the application's active release has it.
    /// </summary>
    public static SchemaPlan Plan(
        ApplicationModel model,
        CatalogSnapshot catalog,
        ProvisioningRecords records,
        IReadOnlyList<MigrationModel> pending,
        IReadOnlyDictionary<string, Guid> storedEntityIds)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(storedEntityIds);

        var migrations = new MigrationPlanning(model, catalog, records, pending, storedEntityIds);
        var planning = new Planning(model, migrations);
        foreach (var entity in model.Entities)
        {
            planning.PlanEntity(entity);
        }

        planning.CheckRemovedEntities();
        return planning.ToPlan();
    }

    private sealed class Planning(ApplicationModel model, MigrationPlanning migrations)
    {
        private readonly ProvisioningRecords _records = migrations.Records;

        // Creates run first, then column changes, then index drops and creates, then foreign keys, so every
        // referenced table exists before a foreign key points at it; cycles and self-references need no ordering.
        // An index drop comes before a create, so an index that changes its unique flag keeps its name.
        private readonly List<string> _creates = [];
        private readonly List<string> _alters = [];
        private readonly List<string> _indexDrops = [];
        private readonly List<string> _indexCreates = [];
        private readonly List<string> _foreignKeys = [];
        private readonly List<Diagnostic> _diagnostics = [.. migrations.Diagnostics];
        private readonly List<ProvisionedEntity> _newEntities = [];
        private readonly List<ProvisionedEnumValue> _newEnumValues = [];

        private readonly Dictionary<string, CatalogTable> _tablesByName =
            migrations.Catalog.Tables.ToDictionary(table => table.Name, StringComparer.Ordinal);

        // The compiler gives each child entity exactly one owner: the child collection field that names it.
        private readonly Dictionary<Guid, ChildOwner> _ownersByChildId = model.Entities
            .SelectMany(entity => entity.Fields
                .Select((field, index) => (Field: field, Owner: new ChildOwner(entity, index)))
                .Where(pair => pair.Field.Type == FieldType.ChildCollection))
            .ToDictionary(pair => pair.Field.Target!.Id, pair => pair.Owner);

        public void PlanEntity(EntityModel entity)
        {
            // Tables are keyed by entity id, so an id recorded for another application would share
            // its table, for example in an application copied with only its manifest id changed.
            var owner = _records.Entities.FirstOrDefault(recorded => recorded.EntityId == entity.Id && recorded.ApplicationId != model.Manifest.Id);
            if (owner is not null)
            {
                _diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.EntityOwnedByOtherApplication,
                    $"The entity id '{entity.Id}' is already provisioned for the application with id '{owner.ApplicationId}'. Give the entity a new id.",
                    entity.File,
                    "/id",
                    entity.Id));
                return;
            }

            var table = EntityNaming.Table(entity.Id);
            var childOwner = _ownersByChildId.GetValueOrDefault(entity.Id);
            if (_tablesByName.TryGetValue(table, out var existing))
            {
                PlanExistingTable(entity, existing, childOwner);
            }
            else
            {
                PlanNewTable(entity, table, childOwner);
            }
        }

        public void CheckRemovedEntities()
        {
            var modelIds = model.Entities.Select(entity => entity.Id).ToHashSet();
            foreach (var recorded in _records.Entities)
            {
                if (recorded.ApplicationId == model.Manifest.Id && !modelIds.Contains(recorded.EntityId))
                {
                    _diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.RemovedEntity,
                        $"The entity with id '{recorded.EntityId}' was removed, but its table '{recorded.TableName}' exists. Drop it with a dropEntity change in a migration.",
                        model.Manifest.File,
                        "",
                        recorded.EntityId));
                }
            }
        }

        public SchemaPlan ToPlan() =>
            _diagnostics.Count > 0
                ? new SchemaPlan(DiagnosticOrder.Sort(_diagnostics), [], [], [])
                : new SchemaPlan(
                    [],
                    [.. migrations.Commands, .. _creates.Concat(_alters).Concat(_indexDrops).Concat(_indexCreates).Concat(_foreignKeys).Select(sql => new SchemaCommand(sql))],
                    _newEntities,
                    _newEnumValues)
                {
                    RecordChanges = [.. migrations.RecordChanges],
                    DestructiveChanges = [.. migrations.DestructiveChanges],
                    RecomputedEntityIds = [.. migrations.RecomputedEntityIds],
                };

        private void PlanNewTable(EntityModel entity, string table, ChildOwner? childOwner)
        {
            // A child table has no version and no computed release: its rows live under the owner's.
            var definitions = new List<string> { $"{EntityNaming.Quote(EntityNaming.IdColumn)} uuid NOT NULL" };
            definitions.AddRange(childOwner is null ? [_versionDefinition, _computedReleaseDefinition] : [_ownerDefinition, _positionDefinition]);
            var constraints = new List<string>
            {
                $"CONSTRAINT {EntityNaming.Quote(EntityNaming.PrimaryKey(table))} PRIMARY KEY ({EntityNaming.Quote(EntityNaming.IdColumn)})",
            };

            foreach (var field in entity.Fields.Where(field => field.HasColumn))
            {
                var column = EntityNaming.Column(field.Name);
                definitions.Add($"{EntityNaming.Quote(column)} {ColumnTypes.Render(field)}{(field.Required ? " NOT NULL" : "")}");
                if (field.Unique)
                {
                    constraints.Add($"CONSTRAINT {EntityNaming.Quote(EntityNaming.Unique(table, column))} UNIQUE ({EntityNaming.Quote(column)})");
                }

                AddForeignKey(table, column, field);
                RecordNewEnumValues(entity, field);
            }

            _creates.Add($"CREATE TABLE {EntityNaming.QualifiedTable(table)} ({string.Join(", ", definitions.Concat(constraints))})");
            foreach (var index in EntityIndexes.Expected(entity, table, childOwner is not null))
            {
                _indexCreates.Add(CreateIndex(table, index));
            }

            if (childOwner is not null)
            {
                AddOwnerForeignKey(table, childOwner);
            }

            if (!_records.Entities.Any(recorded => recorded.EntityId == entity.Id))
            {
                _newEntities.Add(new ProvisionedEntity(entity.Id, model.Manifest.Id, table));
            }
        }

        private void PlanExistingTable(EntityModel entity, CatalogTable table, ChildOwner? childOwner)
        {
            void Report(string message, string path) =>
                _diagnostics.Add(new Diagnostic(DiagnosticCodes.IncompatibleFieldChange, message, entity.File, path, entity.Id));

            var columnsByName = table.Columns.ToDictionary(column => column.Name, StringComparer.Ordinal);

            string[] systemColumns;
            if (childOwner is null)
            {
                systemColumns = [EntityNaming.IdColumn, EntityNaming.VersionColumn, EntityNaming.ComputedReleaseColumn];

                // System columns are never compared; only a missing version or computed release column is
                // added. The version default fills existing rows, and the computed release column has no
                // default, so its existing rows stay null. Both hold even when the table has rows.
                if (!columnsByName.ContainsKey(EntityNaming.VersionColumn))
                {
                    _alters.Add($"ALTER TABLE {EntityNaming.QualifiedTable(table.Name)} ADD COLUMN {_versionDefinition}");
                }

                if (!columnsByName.ContainsKey(EntityNaming.ComputedReleaseColumn))
                {
                    _alters.Add($"ALTER TABLE {EntityNaming.QualifiedTable(table.Name)} ADD COLUMN {_computedReleaseDefinition}");
                }
            }
            else
            {
                systemColumns = [EntityNaming.IdColumn, EntityNaming.OwnerColumn, EntityNaming.PositionColumn];
                if (!PlanOwnerColumn(entity, table, columnsByName, childOwner))
                {
                    return;
                }
            }

            for (var index = 0; index < entity.Fields.Count; index++)
            {
                var field = entity.Fields[index];
                if (!field.HasColumn)
                {
                    continue;
                }

                var path = $"/fields/{index}";
                if (columnsByName.TryGetValue(EntityNaming.Column(field.Name), out var column))
                {
                    if (column.AddedByFill)
                    {
                        PlanNewColumn(entity, table, field, path, Report, addColumn: false);
                    }
                    else
                    {
                        PlanExistingColumn(entity, table, field, column, path, Report);
                    }
                }
                else
                {
                    PlanNewColumn(entity, table, field, path, Report);
                }
            }

            var fieldColumns = entity.Fields
                .Where(field => field.HasColumn)
                .Select(field => EntityNaming.Column(field.Name))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var column in table.Columns)
            {
                if (!systemColumns.Contains(column.Name, StringComparer.Ordinal) && !fieldColumns.Contains(column.Name))
                {
                    _diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.RemovedField,
                        $"The column '{column.Name}' has no field. Drop it with a dropField change in a migration, or rename it with a renameField change.",
                        entity.File,
                        "/fields",
                        entity.Id));
                }
            }

            PlanIndexes(entity, table, childOwner is not null);
        }

        // Creates the expected indexes the table lacks and drops the ix_ indexes the release does not declare. An index
        // whose unique flag differs is dropped and created again. A new unique index cannot be built over existing rows.
        private void PlanIndexes(EntityModel entity, CatalogTable table, bool isChild)
        {
            var expected = EntityIndexes.Expected(entity, table.Name, isChild);
            var catalogByName = table.Indexes.ToDictionary(index => index.Name, StringComparer.Ordinal);
            foreach (var index in expected)
            {
                if (catalogByName.TryGetValue(index.Name, out var current) && current.Unique == index.Unique)
                {
                    continue;
                }

                if (index.Unique && table.HasRows)
                {
                    _diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.IncompatibleFieldChange,
                        $"The unique index over {string.Join(", ", index.Columns.Select(column => $"'{column}'"))} cannot be added to a table that already has records.",
                        entity.File,
                        $"/indexes/{index.Declared}/unique",
                        entity.Id));
                    continue;
                }

                if (current is not null)
                {
                    _indexDrops.Add(DropIndex(current.Name));
                }

                _indexCreates.Add(CreateIndex(table.Name, index));
            }

            var expectedNames = expected.Select(index => index.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var index in table.Indexes.Where(index => !expectedNames.Contains(index.Name)))
            {
                _indexDrops.Add(DropIndex(index.Name));
            }
        }

        private static string DropIndex(string name) => $"DROP INDEX {EntityNaming.Quote(EntityNaming.Schema)}.{EntityNaming.Quote(name)}";

        private static string CreateIndex(string table, ExpectedIndex index) =>
            $"CREATE {(index.Unique ? "UNIQUE " : "")}INDEX {EntityNaming.Quote(index.Name)} ON {EntityNaming.QualifiedTable(table)} ({string.Join(", ", index.Columns.Select(EntityNaming.Quote))})";

        /// <summary>
        /// Checks the owner column of an existing child table and adds its foreign key when it has
        /// none. A table without an owner column, or whose owner column points at another table,
        /// is reported at the owner's child collection field. Returns whether the table is a child
        /// table of this owner, so its fields can be planned.
        /// </summary>
        private bool PlanOwnerColumn(EntityModel entity, CatalogTable table, Dictionary<string, CatalogColumn> columnsByName, ChildOwner childOwner)
        {
            void Report(string message) =>
                _diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.IncompatibleFieldChange,
                    message,
                    childOwner.Entity.File,
                    $"/fields/{childOwner.FieldIndex}/target",
                    childOwner.Entity.Id));

            // Adding a NOT NULL owner column to an existing table, or dropping it, would need a migration.
            if (!columnsByName.TryGetValue(EntityNaming.OwnerColumn, out var ownerColumn))
            {
                Report($"The entity '{entity.Name}' cannot become a child entity: its table already exists without an owner column.");
                return false;
            }

            if (ownerColumn.ReferencedTable is null)
            {
                AddOwnerForeignKey(table.Name, childOwner);
            }
            else if (ownerColumn.ReferencedTable != EntityNaming.Table(childOwner.Entity.Id))
            {
                Report($"The child entity '{entity.Name}' cannot change its owner to '{childOwner.Entity.Name}'.");
            }

            return true;
        }

        // A column that a pending fill added already exists, without NOT NULL, and holds no nulls: only a required field changes it.
        private void PlanNewColumn(EntityModel entity, CatalogTable table, FieldModel field, string path, Action<string, string> report, bool addColumn = true)
        {
            var column = EntityNaming.Column(field.Name);
            var alterTable = $"ALTER TABLE {EntityNaming.QualifiedTable(table.Name)}";
            if (!addColumn)
            {
                if (field.Required)
                {
                    _alters.Add($"{alterTable} ALTER COLUMN {EntityNaming.Quote(column)} SET NOT NULL");
                }
            }
            else
            {
                var notNull = "";
                if (field.Required)
                {
                    if (table.HasRows)
                    {
                        report($"The required field '{field.Name}' cannot be added to a table that already has records.", $"{path}/required");
                    }

                    notNull = " NOT NULL";
                }

                _alters.Add($"{alterTable} ADD COLUMN {EntityNaming.Quote(column)} {ColumnTypes.Render(field)}{notNull}");
            }

            if (field.Unique)
            {
                _alters.Add($"ALTER TABLE {EntityNaming.QualifiedTable(table.Name)} ADD CONSTRAINT {EntityNaming.Quote(EntityNaming.Unique(table.Name, column))} UNIQUE ({EntityNaming.Quote(column)})");
            }

            AddForeignKey(table.Name, column, field);
            RecordNewEnumValues(entity, field);
        }

        private void PlanExistingColumn(
            EntityModel entity,
            CatalogTable table,
            FieldModel field,
            CatalogColumn column,
            string path,
            Action<string, string> report)
        {
            var alterTable = $"ALTER TABLE {EntityNaming.QualifiedTable(table.Name)}";
            var quotedColumn = EntityNaming.Quote(column.Name);

            var recordedValues = RecordedEnumValues(entity, field);
            var isEnum = field.Type == FieldType.Enum;
            if (isEnum != (recordedValues.Count > 0))
            {
                report(
                    isEnum
                        ? $"The field '{field.Name}' cannot become an enum: its column already holds values of another type."
                        : $"The enum field '{field.Name}' cannot change to another type.",
                    $"{path}/type");
            }
            else
            {
                var expected = ColumnTypes.Render(field);
                if (column.Type == expected)
                {
                    // Nothing to change.
                }
                else if (IsTextType(column.Type) && field.Type == FieldType.Text)
                {
                    if (IsWider(expected, column.Type))
                    {
                        _alters.Add($"{alterTable} ALTER COLUMN {quotedColumn} TYPE {expected}");
                    }
                    else
                    {
                        report($"The maximum length of '{field.Name}' cannot shrink from {column.Type} to {expected}.", $"{path}/maxLength");
                    }
                }
                else
                {
                    report($"The type of '{field.Name}' cannot change from {column.Type} to {expected}. Convert it with a convert change in a migration.", $"{path}/type");
                }
            }

            if (column.NotNull && !field.Required)
            {
                _alters.Add($"{alterTable} ALTER COLUMN {quotedColumn} DROP NOT NULL");
            }
            else if (!column.NotNull && field.Required)
            {
                if (column.Filled)
                {
                    _alters.Add($"{alterTable} ALTER COLUMN {quotedColumn} SET NOT NULL");
                }
                else
                {
                    report($"The existing field '{field.Name}' cannot become required.", $"{path}/required");
                }
            }

            if (column.Unique && !field.Unique)
            {
                _alters.Add($"{alterTable} DROP CONSTRAINT {EntityNaming.Quote(EntityNaming.Unique(table.Name, column.Name))}");
            }
            else if (!column.Unique && field.Unique)
            {
                report($"The existing field '{field.Name}' cannot become unique.", $"{path}/unique");
            }

            if (field.Target is { } target && column.Type == ColumnTypes.Render(field))
            {
                var targetTable = EntityNaming.Table(target.Id);
                if (column.ReferencedTable is null)
                {
                    AddForeignKey(table.Name, column.Name, field);
                }
                else if (column.ReferencedTable != targetTable)
                {
                    report($"The reference '{field.Name}' cannot change its target to '{target.Name}'.", $"{path}/target");
                }
            }

            if (isEnum && recordedValues.Count > 0)
            {
                var removed = recordedValues.Where(value => !field.Values!.Contains(value, StringComparer.Ordinal)).ToList();
                if (removed.Count > 0)
                {
                    report(
                        $"The enum field '{field.Name}' cannot drop the values {string.Join(", ", removed.Select(value => $"'{value}'"))}. Convert them with a convert change in a migration.",
                        $"{path}/values");
                }

                RecordNewEnumValues(entity, field);
            }
        }

        private void AddForeignKey(string table, string column, FieldModel field)
        {
            if (field.Target is { } target)
            {
                AddForeignKey(table, column, target.Id, "");
            }
        }

        // Child rows are deleted with their owner.
        private void AddOwnerForeignKey(string table, ChildOwner childOwner) =>
            AddForeignKey(table, EntityNaming.OwnerColumn, childOwner.Entity.Id, " ON DELETE CASCADE");

        private void AddForeignKey(string table, string column, Guid targetId, string onDelete) =>
            _foreignKeys.Add(
                $"ALTER TABLE {EntityNaming.QualifiedTable(table)} ADD CONSTRAINT {EntityNaming.Quote(EntityNaming.ForeignKey(table, column))} "
                + $"FOREIGN KEY ({EntityNaming.Quote(column)}) REFERENCES {EntityNaming.QualifiedTable(EntityNaming.Table(targetId))} ({EntityNaming.Quote(EntityNaming.IdColumn)}){onDelete}");

        private void RecordNewEnumValues(EntityModel entity, FieldModel field)
        {
            if (field.Values is null || field.Type != FieldType.Enum)
            {
                return;
            }

            var recorded = RecordedEnumValues(entity, field);
            foreach (var value in field.Values)
            {
                if (!recorded.Contains(value, StringComparer.Ordinal))
                {
                    _newEnumValues.Add(new ProvisionedEnumValue(entity.Id, field.Name, value));
                }
            }
        }

        // Field names match ignoring letter case, like the column name; values match ordinally.
        private List<string> RecordedEnumValues(EntityModel entity, FieldModel field) =>
            _records.EnumValues
                .Where(recorded => recorded.EntityId == entity.Id && string.Equals(recorded.FieldName, field.Name, StringComparison.OrdinalIgnoreCase))
                .Select(recorded => recorded.Value)
                .ToList();

        private static bool IsTextType(string type) => type == ColumnTypes.Text || ColumnTypes.TryParseVarying(type, out _);

        // Text widens to unbounded text or to a larger character varying length.
        private static bool IsWider(string expected, string current)
        {
            if (expected == ColumnTypes.Text)
            {
                return current != ColumnTypes.Text;
            }

            return ColumnTypes.TryParseVarying(expected, out var expectedLength)
                && ColumnTypes.TryParseVarying(current, out var currentLength)
                && expectedLength > currentLength;
        }
    }

    /// <summary>The entity and the index of the child collection field that owns a child entity.</summary>
    private sealed record ChildOwner(EntityModel Entity, int FieldIndex);
}
