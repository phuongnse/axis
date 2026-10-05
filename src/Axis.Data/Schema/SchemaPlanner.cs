using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Data.Naming;

namespace Axis.Data.Schema;

/// <summary>
/// Plans the entity table changes for a compiled application by comparing it with the tenant
/// catalog and the provisioning records. Only additive changes are planned; anything that could
/// lose or reject existing data is reported as a diagnostic, and then nothing is planned at all.
/// </summary>
public static class SchemaPlanner
{
    public static SchemaPlan Plan(ApplicationModel model, CatalogSnapshot catalog, ProvisioningRecords records)
    {
        var planning = new Planning(model, catalog, records);
        foreach (var entity in model.Entities)
        {
            planning.PlanEntity(entity);
        }

        planning.CheckRemovedEntities();
        return planning.ToPlan();
    }

    private sealed class Planning(ApplicationModel model, CatalogSnapshot catalog, ProvisioningRecords records)
    {
        // Creates run first, then column changes, then foreign keys, so every referenced table
        // exists before a foreign key points at it; cycles and self-references need no ordering.
        private readonly List<string> _creates = [];
        private readonly List<string> _alters = [];
        private readonly List<string> _foreignKeys = [];
        private readonly List<Diagnostic> _diagnostics = [];
        private readonly List<ProvisionedEntity> _newEntities = [];
        private readonly List<ProvisionedEnumValue> _newEnumValues = [];

        private readonly Dictionary<string, CatalogTable> _tablesByName =
            catalog.Tables.ToDictionary(table => table.Name, StringComparer.Ordinal);

        public void PlanEntity(EntityModel entity)
        {
            var table = EntityNaming.Table(entity.Id);
            if (_tablesByName.TryGetValue(table, out var existing))
            {
                PlanExistingTable(entity, existing);
            }
            else
            {
                PlanNewTable(entity, table);
            }
        }

        public void CheckRemovedEntities()
        {
            var modelIds = model.Entities.Select(entity => entity.Id).ToHashSet();
            foreach (var recorded in records.Entities)
            {
                if (recorded.ApplicationId == model.Manifest.Id && !modelIds.Contains(recorded.EntityId))
                {
                    _diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.RemovedEntity,
                        $"The entity with id '{recorded.EntityId}' was removed, but its table '{recorded.TableName}' exists. Removing an entity is not supported yet.",
                        model.Manifest.File,
                        "",
                        recorded.EntityId));
                }
            }
        }

        public SchemaPlan ToPlan() =>
            _diagnostics.Count > 0
                ? new SchemaPlan(DiagnosticOrder.Sort(_diagnostics), [], [], [])
                : new SchemaPlan([], [.. _creates, .. _alters, .. _foreignKeys], _newEntities, _newEnumValues);

        private void PlanNewTable(EntityModel entity, string table)
        {
            var definitions = new List<string> { $"{EntityNaming.Quote(EntityNaming.IdColumn)} uuid NOT NULL" };
            var constraints = new List<string>
            {
                $"CONSTRAINT {EntityNaming.Quote(EntityNaming.PrimaryKey(table))} PRIMARY KEY ({EntityNaming.Quote(EntityNaming.IdColumn)})",
            };

            foreach (var field in entity.Fields)
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
            if (!records.Entities.Any(recorded => recorded.EntityId == entity.Id))
            {
                _newEntities.Add(new ProvisionedEntity(entity.Id, model.Manifest.Id, table));
            }
        }

        private void PlanExistingTable(EntityModel entity, CatalogTable table)
        {
            void Report(string message, string path) =>
                _diagnostics.Add(new Diagnostic(DiagnosticCodes.IncompatibleFieldChange, message, entity.File, path, entity.Id));

            var columnsByName = table.Columns.ToDictionary(column => column.Name, StringComparer.Ordinal);
            for (var index = 0; index < entity.Fields.Count; index++)
            {
                var field = entity.Fields[index];
                var path = $"/fields/{index}";
                if (columnsByName.TryGetValue(EntityNaming.Column(field.Name), out var column))
                {
                    PlanExistingColumn(entity, table, field, column, path, Report);
                }
                else
                {
                    PlanNewColumn(entity, table, field, path, Report);
                }
            }

            var fieldColumns = entity.Fields.Select(field => EntityNaming.Column(field.Name)).ToHashSet(StringComparer.Ordinal);
            foreach (var column in table.Columns)
            {
                if (column.Name != EntityNaming.IdColumn && !fieldColumns.Contains(column.Name))
                {
                    _diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.RemovedField,
                        $"The column '{column.Name}' has no field. Removing a field is not supported yet.",
                        entity.File,
                        "/fields",
                        entity.Id));
                }
            }
        }

        private void PlanNewColumn(EntityModel entity, CatalogTable table, FieldModel field, string path, Action<string, string> report)
        {
            var column = EntityNaming.Column(field.Name);
            var notNull = "";
            if (field.Required)
            {
                if (table.HasRows)
                {
                    report($"The required field '{field.Name}' cannot be added to a table that already has records.", $"{path}/required");
                }

                notNull = " NOT NULL";
            }

            _alters.Add($"ALTER TABLE {EntityNaming.QualifiedTable(table.Name)} ADD COLUMN {EntityNaming.Quote(column)} {ColumnTypes.Render(field)}{notNull}");
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
                    report($"The type of '{field.Name}' cannot change from {column.Type} to {expected}.", $"{path}/type");
                }
            }

            if (column.NotNull && !field.Required)
            {
                _alters.Add($"{alterTable} ALTER COLUMN {quotedColumn} DROP NOT NULL");
            }
            else if (!column.NotNull && field.Required)
            {
                report($"The existing field '{field.Name}' cannot become required.", $"{path}/required");
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
                        $"The enum field '{field.Name}' cannot drop the values {string.Join(", ", removed.Select(value => $"'{value}'"))}.",
                        $"{path}/values");
                }

                RecordNewEnumValues(entity, field);
            }
        }

        private void AddForeignKey(string table, string column, FieldModel field)
        {
            if (field.Target is not { } target)
            {
                return;
            }

            _foreignKeys.Add(
                $"ALTER TABLE {EntityNaming.QualifiedTable(table)} ADD CONSTRAINT {EntityNaming.Quote(EntityNaming.ForeignKey(table, column))} "
                + $"FOREIGN KEY ({EntityNaming.Quote(column)}) REFERENCES {EntityNaming.QualifiedTable(EntityNaming.Table(target.Id))} ({EntityNaming.Quote(EntityNaming.IdColumn)})");
        }

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
            records.EnumValues
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
}
