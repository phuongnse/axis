using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Expressions.Parsing;
using Axis.Expressions.Sql;
using Axis.Expressions.Typing;

namespace Axis.Data.Schema;

/// <summary>
/// Applies the pending migrations of a release to in-memory copies of the catalog snapshot and the
/// provisioning records, and collects the commands and record changes that do the same to the
/// tenant database. Planning the release then runs on the copies, so a removed field or entity that
/// a migration covers is not reported, and a field that a <c>fill</c> covers may become required. A
/// change that does not fit the stored schema is skipped: the table or the <c>from</c> column is
/// missing, or the <c>to</c> column exists. If something is left over, the planner still reports it.
/// A <c>fill</c> or <c>convert</c> sets the release stamp of each row it changes to <c>NULL</c>. On a child
/// entity it also increments the version of the owner of each changed row and clears the owner's stamp.
/// <see cref="RecomputedEntityIds"/> names the entities whose rows activation then recomputes.
/// </summary>
internal sealed class MigrationPlanning
{
    // Field columns start with f_, so this name cannot clash with one.
    private const string StagingColumn = "axis_convert";

    private readonly ApplicationModel _model;
    private readonly IReadOnlyList<MigrationModel> _pending;
    private readonly IReadOnlyDictionary<string, Guid> _storedEntityIds;
    private readonly List<CatalogTable> _tables;
    private readonly List<ProvisionedEntity> _entities;
    private readonly List<ProvisionedEnumValue> _enumValues;

    public MigrationPlanning(
        ApplicationModel model,
        CatalogSnapshot catalog,
        ProvisioningRecords records,
        IReadOnlyList<MigrationModel> pending,
        IReadOnlyDictionary<string, Guid> storedEntityIds)
    {
        _model = model;
        _pending = pending;
        _storedEntityIds = storedEntityIds;
        _tables = [.. catalog.Tables];
        _entities = [.. records.Entities];
        _enumValues = [.. records.EnumValues];

        foreach (var migration in pending)
        {
            for (var index = 0; index < migration.Changes.Count; index++)
            {
                Apply(migration, index, migration.Changes[index]);
            }
        }
    }

    public List<SchemaCommand> Commands { get; } = [];

    public List<Diagnostic> Diagnostics { get; } = [];

    public List<MigrationRecordChange> RecordChanges { get; } = [];

    public List<DestructiveChange> DestructiveChanges { get; } = [];

    /// <summary>
    /// The entities that own the rows a <c>fill</c> or <c>convert</c> changes: the entity itself, or for a child
    /// entity the entity whose child collection it is. Activation recomputes their rows with a cleared stamp.
    /// </summary>
    public HashSet<Guid> RecomputedEntityIds { get; } = [];

    public CatalogSnapshot Catalog => new(_tables);

    public ProvisioningRecords Records => new(_entities, _enumValues);

    private void Apply(MigrationModel migration, int index, MigrationChangeModel change)
    {
        var entityId = FindEntityId(migration.Entity);
        if (entityId is not { } id)
        {
            return;
        }

        var table = EntityNaming.Table(id);
        var tableIndex = _tables.FindIndex(candidate => candidate.Name == table);
        if (tableIndex < 0)
        {
            return;
        }

        switch (change.Type)
        {
            case MigrationChangeType.RenameField:
                Rename(migration, tableIndex, id, change);
                break;
            case MigrationChangeType.DropField:
                Drop(migration, tableIndex, id, change);
                break;
            case MigrationChangeType.DropEntity:
                DropEntity(migration, id, table);
                break;
            case MigrationChangeType.Fill:
                Fill(migration, index, tableIndex, id, change);
                break;
            case MigrationChangeType.Convert:
                Convert(migration, index, tableIndex, id, change);
                break;
        }
    }

    // The application's active release describes the tables that hold the data now, and only it has a dropped
    // entity. The release being activated covers entities the active release does not have.
    private Guid? FindEntityId(string name)
    {
        if (_storedEntityIds.TryGetValue(name, out var stored))
        {
            return stored;
        }

        return _model.Entities.FirstOrDefault(entity => string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    // One UPDATE sets every null of the field, then the planner may make the column NOT NULL. The statement
    // counts the rows it left null, so a value that is null for some row stops the activation.
    private void Fill(MigrationModel migration, int index, int tableIndex, Guid entityId, MigrationChangeModel change)
    {
        var table = _tables[tableIndex];
        var column = EntityNaming.Column(change.Field!);

        var releaseEntity = _model.Entities.FirstOrDefault(entity => entity.Id == entityId);

        var existing = table.Columns.FirstOrDefault(candidate => candidate.Name == column);
        var quotedTable = EntityNaming.QualifiedTable(table.Name);
        var addColumn = new List<SchemaCommand>();
        CatalogColumn filled;
        if (existing is null)
        {
            var field = releaseEntity?.Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, change.Field, StringComparison.OrdinalIgnoreCase));
            if (field is null || !field.HasColumn)
            {
                return;
            }

            addColumn.Add(new SchemaCommand($"ALTER TABLE {quotedTable} ADD COLUMN {EntityNaming.Quote(column)} {ColumnTypes.Render(field)}"));
            filled = new CatalogColumn(column, ColumnTypes.Render(field), false, false, null) { Filled = true, AddedByFill = true };
        }
        else
        {
            filled = existing with { Filled = true };
        }

        List<CatalogColumn> columns = existing is null
            ? [.. table.Columns, filled]
            : [.. table.Columns.Select(candidate => candidate == existing ? filled : candidate)];

        var unknown = false;
        string Column(IReadOnlyList<string> path)
        {
            var name = EntityNaming.Column(path[0]);
            if (path.Count == 1 && columns.Any(candidate => candidate.Name == name))
            {
                return EntityNaming.Quote(name);
            }

            unknown = true;
            Diagnostics.Add(new Diagnostic(
                DiagnosticCodes.UnknownMigrationField,
                $"The value names '{string.Join('.', path)}', which is not a field stored in the entity's table.",
                migration.File,
                $"/changes/{index}/value",
                migration.Id));
            return "NULL";
        }

        var parsed = ExpressionParser.Parse(change.Value!);
        if (!parsed.Succeeded)
        {
            throw new InvalidOperationException($"The fill value does not parse, though the compiler checked it: {parsed.Diagnostic.Message}");
        }

        var translated = SqlTranslator.Translate(parsed.Expression!, Column, timeZone: _model.TimeZone);
        if (!translated.Succeeded)
        {
            throw new InvalidOperationException($"The fill value does not translate to SQL, though the compiler checked it: {translated.Diagnostic.Message}");
        }

        if (unknown)
        {
            return;
        }

        var owner = FindOwner(table);
        if (owner is { Exists: false })
        {
            return;
        }

        var quotedColumn = EntityNaming.Quote(column);
        var returning = owner is null ? quotedColumn : $"{EntityNaming.Quote(EntityNaming.OwnerColumn)}, {quotedColumn}";
        var ownerUpdate = owner is null ? "" : $", owners AS ({owner.Update("filled")})";
        Commands.AddRange(addColumn);
        Commands.Add(new SchemaCommand(
            $"WITH filled AS (UPDATE {quotedTable} SET {quotedColumn} = {translated.Sql}{Stale(columns)} WHERE {quotedColumn} IS NULL RETURNING {returning}){ownerUpdate} "
            + $"SELECT count(*) FROM filled WHERE {quotedColumn} IS NULL")
        {
            Parameters = translated.Parameters,
            Fill = new FillTarget(migration.Id, migration.File, index, change.Field!),
        });
        RecomputedEntityIds.Add(RecomputedOwnerId(entityId, owner));
        _tables[tableIndex] = table with { Columns = columns };
    }

    // A convert writes the new value of each row from its expression. For an enum field it maps the rows that hold
    // a removed value. Otherwise it changes the column to the field's new type through a staging column, because
    // DDL cannot take the parameters that the value's literals need. Any other case plans nothing, so the planner
    // reports what it reports today.
    private void Convert(MigrationModel migration, int index, int tableIndex, Guid entityId, MigrationChangeModel change)
    {
        var table = _tables[tableIndex];
        var column = EntityNaming.Column(change.Field!);

        var releaseEntity = _model.Entities.FirstOrDefault(entity => entity.Id == entityId);
        var names = NamesAfter(migration, index, entityId, change.Field!);
        var existing = table.Columns.FirstOrDefault(candidate => candidate.Name == column);
        var field = releaseEntity?.Fields.FirstOrDefault(candidate => names.Contains(candidate.Name));
        if (existing is null || field is null)
        {
            return;
        }

        // SQL cannot convert into or out of encrypted values, so the planner's refusal stands.
        if (field.Sensitive || existing.Type == ColumnTypes.Encrypted)
        {
            return;
        }

        var expected = ColumnTypes.Render(field);
        var recorded = _enumValues
            .Where(value => value.EntityId == entityId && string.Equals(value.FieldName, change.Field, StringComparison.OrdinalIgnoreCase))
            .Select(value => value.Value)
            .ToList();
        var isEnumConvert = field.Type == FieldType.Enum && recorded.Count > 0;
        var isTypeConvert = field.Type != FieldType.Enum && recorded.Count == 0 && existing.Type != expected;
        if (!isEnumConvert && !isTypeConvert)
        {
            return;
        }

        List<string> removed = [];
        if (isEnumConvert)
        {
            removed = [.. recorded.Where(value => !field.Values!.Contains(value, StringComparer.Ordinal))];
            if (removed.Count == 0)
            {
                return;
            }
        }

        var unknown = false;
        string Column(IReadOnlyList<string> path)
        {
            var name = EntityNaming.Column(path[0]);
            if (path.Count == 1 && table.Columns.Any(candidate => candidate.Name == name))
            {
                return EntityNaming.Quote(name);
            }

            unknown = true;
            Diagnostics.Add(new Diagnostic(
                DiagnosticCodes.UnknownMigrationField,
                $"The value names '{string.Join('.', path)}', which is not a field stored in the entity's table.",
                migration.File,
                $"/changes/{index}/value",
                migration.Id));
            return "NULL";
        }

        var parsed = ExpressionParser.Parse(change.Value!);
        if (!parsed.Succeeded)
        {
            throw new InvalidOperationException($"The convert value does not parse, though the compiler checked it: {parsed.Diagnostic.Message}");
        }

        var translated = SqlTranslator.Translate(parsed.Expression!, Column, timeZone: _model.TimeZone);
        if (!translated.Succeeded)
        {
            throw new InvalidOperationException($"The convert value does not translate to SQL, though the compiler checked it: {translated.Diagnostic.Message}");
        }

        if (unknown)
        {
            return;
        }

        var owner = FindOwner(table);
        if (owner is { Exists: false })
        {
            return;
        }

        var stale = Stale(table.Columns);
        var quotedTable = EntityNaming.QualifiedTable(table.Name);
        var quotedColumn = EntityNaming.Quote(column);

        if (isEnumConvert)
        {
            // The mapping may produce any value the release lists, including one this release declares for the first time.
            var kept = field.Values!.Distinct(StringComparer.Ordinal).ToList();
            var parameters = new List<SqlValue>(translated.Parameters);
            parameters.AddRange(removed.Select((value, position) => new SqlValue($"r{position}", ExpressionTypeKind.Text, value)));
            parameters.AddRange(kept.Select((value, position) => new SqlValue($"k{position}", ExpressionTypeKind.Text, value)));
            var removedList = string.Join(", ", removed.Select((_, position) => $"@r{position}"));
            var keptList = kept.Count == 0 ? "" : $" AND {quotedColumn} NOT IN ({string.Join(", ", kept.Select((_, position) => $"@k{position}"))})";
            var returning = owner is null ? quotedColumn : $"{EntityNaming.Quote(EntityNaming.OwnerColumn)}, {quotedColumn}";
            var ownerUpdate = owner is null ? "" : $", owners AS ({owner.Update("converted")})";
            Commands.Add(new SchemaCommand(
                $"WITH converted AS (UPDATE {quotedTable} SET {quotedColumn} = {translated.Sql}{stale} WHERE {quotedColumn} IN ({removedList}) RETURNING {returning}){ownerUpdate} "
                + $"SELECT count(*) FROM converted WHERE {quotedColumn} IS NOT NULL{keptList}")
            {
                Parameters = parameters,
                Convert = new ConvertTarget(migration.Id, migration.File, index, change.Field!, CountsDisallowed: true),
            });
            _enumValues.RemoveAll(value => value.EntityId == entityId
                && string.Equals(value.FieldName, change.Field, StringComparison.OrdinalIgnoreCase)
                && removed.Contains(value.Value, StringComparer.Ordinal));
            RecordChanges.Add(new MigrationRecordChange(MigrationChangeType.Convert, entityId, change.Field, null) { Values = removed });
            DestructiveChanges.Add(new DestructiveChange(migration, entityId, change.Field));
            RecomputedEntityIds.Add(RecomputedOwnerId(entityId, owner));
            return;
        }

        // Assigning to character varying(n) truncates a cast silently, so it casts to text and lets the ALTER check the length.
        var cast = ColumnTypes.TryParseVarying(expected, out _) ? ColumnTypes.Text : expected;
        var staging = EntityNaming.Quote(StagingColumn);
        var alterTable = $"ALTER TABLE {quotedTable}";
        var target = new ConvertTarget(migration.Id, migration.File, index, change.Field!, CountsDisallowed: false);
        Commands.Add(new SchemaCommand($"{alterTable} ADD COLUMN {staging} {expected}") { Convert = target });
        var stagingUpdate = owner is null
            ? $"UPDATE {quotedTable} SET {staging} = ({translated.Sql})::{cast}{stale}"
            : $"WITH converted AS (UPDATE {quotedTable} SET {staging} = ({translated.Sql})::{cast} RETURNING {EntityNaming.Quote(EntityNaming.OwnerColumn)}) {owner.Update("converted")}";
        Commands.Add(new SchemaCommand(stagingUpdate)
        {
            Parameters = translated.Parameters,
            Convert = target,
        });
        Commands.Add(new SchemaCommand($"{alterTable} ALTER COLUMN {quotedColumn} TYPE {expected} USING {staging}") { Convert = target });
        Commands.Add(new SchemaCommand($"{alterTable} DROP COLUMN {staging}") { Convert = target });
        _tables[tableIndex] = table with
        {
            Columns = [.. table.Columns.Select(candidate => candidate == existing ? existing with { Type = expected } : candidate)],
        };
        DestructiveChanges.Add(new DestructiveChange(migration, entityId, change.Field));
        RecomputedEntityIds.Add(RecomputedOwnerId(entityId, owner));
    }

    // The row assignments that mark a changed row for a recompute, for the columns the table has: a version bump,
    // so an edit begun before the migration gets a conflict, and a cleared release stamp. A child table has neither.
    private static string Stale(IEnumerable<CatalogColumn> columns) => string.Concat(StaleAssignments(columns).Select(assignment => ", " + assignment));

    private static List<string> StaleAssignments(IEnumerable<CatalogColumn> columns)
    {
        var present = columns.Select(column => column.Name).ToHashSet();
        var assignments = new List<string>();
        if (present.Contains(EntityNaming.VersionColumn))
        {
            assignments.Add($"{EntityNaming.Quote(EntityNaming.VersionColumn)} = {EntityNaming.Quote(EntityNaming.VersionColumn)} + 1");
        }

        if (present.Contains(EntityNaming.ComputedReleaseColumn))
        {
            assignments.Add($"{EntityNaming.Quote(EntityNaming.ComputedReleaseColumn)} = NULL");
        }

        return assignments;
    }

    // The owner of a child table's rows, null for a table that is not a child table. A child table whose owner
    // table the catalog lacks, or that has no version or stamp column, gives an owner that does not exist, and the change is not planned.
    private OwnerUpdate? FindOwner(CatalogTable table)
    {
        var ownerColumn = table.Columns.FirstOrDefault(candidate => candidate.Name == EntityNaming.OwnerColumn);
        if (ownerColumn is null)
        {
            return null;
        }

        var owner = _tables.FirstOrDefault(candidate => candidate.Name == ownerColumn.ReferencedTable);
        var assignments = owner is null ? [] : StaleAssignments(owner.Columns);
        return owner is null || assignments.Count == 0
            ? new OwnerUpdate(false, "", [])
            : new OwnerUpdate(true, EntityNaming.QualifiedTable(owner.Name), assignments);
    }

    // The entity whose rows activation recomputes: the entity itself, or for a child entity the entity whose
    // child collection it is. Nesting is one level only.
    private Guid RecomputedOwnerId(Guid entityId, OwnerUpdate? owner)
    {
        if (owner is null)
        {
            return entityId;
        }

        var parent = _model.Entities.FirstOrDefault(entity =>
            entity.Fields.Any(field => field.Type == FieldType.ChildCollection && field.Target?.Id == entityId));
        return parent?.Id ?? entityId;
    }

    private sealed record OwnerUpdate(bool Exists, string QuotedTable, IReadOnlyList<string> Assignments)
    {
        // Marks the owner of every row that the CTE named by source returned.
        public string Update(string source) =>
            $"UPDATE {QuotedTable} SET {string.Join(", ", Assignments)} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} IN (SELECT {EntityNaming.Quote(EntityNaming.OwnerColumn)} FROM {source})";
    }

    // The release names the field by its last name, so a rename after the fill gives the names to look for.
    private HashSet<string> NamesAfter(MigrationModel migration, int index, Guid entityId, string field)
    {
        var names = new HashSet<string>([field], StringComparer.OrdinalIgnoreCase);
        var after = false;
        foreach (var candidate in _pending)
        {
            for (var next = 0; next < candidate.Changes.Count; next++)
            {
                if (!after)
                {
                    after = candidate == migration && next == index;
                    continue;
                }

                var change = candidate.Changes[next];
                if (change.Type == MigrationChangeType.RenameField
                    && names.Contains(change.From!)
                    && FindEntityId(candidate.Entity) == entityId)
                {
                    names.Add(change.To!);
                }
            }
        }

        return names;
    }

    private void Rename(MigrationModel migration, int tableIndex, Guid entityId, MigrationChangeModel change)
    {
        var table = _tables[tableIndex];
        var from = EntityNaming.Column(change.From!);
        var to = EntityNaming.Column(change.To!);
        var column = table.Columns.FirstOrDefault(candidate => candidate.Name == from);
        if (column is null || table.Columns.Any(candidate => candidate.Name == to))
        {
            return;
        }

        var alterTable = $"ALTER TABLE {EntityNaming.QualifiedTable(table.Name)}";
        Commands.Add(new SchemaCommand($"{alterTable} RENAME COLUMN {EntityNaming.Quote(from)} TO {EntityNaming.Quote(to)}"));
        if (column.Unique)
        {
            Commands.Add(new SchemaCommand($"{alterTable} RENAME CONSTRAINT {EntityNaming.Quote(EntityNaming.Unique(table.Name, from))} TO {EntityNaming.Quote(EntityNaming.Unique(table.Name, to))}"));
        }

        if (column.ReferencedTable is not null)
        {
            Commands.Add(new SchemaCommand($"{alterTable} RENAME CONSTRAINT {EntityNaming.Quote(EntityNaming.ForeignKey(table.Name, from))} TO {EntityNaming.Quote(EntityNaming.ForeignKey(table.Name, to))}"));
        }

        // The associated data of an encrypted value holds the field name, so activation encrypts each value again under the new one.
        if (column.Type == ColumnTypes.Encrypted)
        {
            Commands.Add(new SchemaCommand(
                $"UPDATE {EntityNaming.QualifiedTable(table.Name)} AS t SET {EntityNaming.Quote(to)} = v.value "
                + $"FROM unnest(@ids, @values) AS v(id, value) WHERE t.{EntityNaming.Quote(EntityNaming.IdColumn)} = v.id")
            {
                Rewrite = new SensitiveRewrite(SensitiveRewriteKind.Rename, entityId, table.Name, to, to, null, change.To!, change.From),
            });
        }

        // An index is named after its columns, so each index over the column gets the name its renamed column list has.
        var indexes = new List<CatalogIndex>();
        foreach (var index in table.Indexes)
        {
            if (!index.Columns.Contains(from, StringComparer.Ordinal))
            {
                indexes.Add(index);
                continue;
            }

            List<string> columns = [.. index.Columns.Select(candidate => candidate == from ? to : candidate)];
            var name = index.Trigram ? EntityNaming.TrigramIndex(table.Name, to) : EntityNaming.Index(table.Name, columns);
            Commands.Add(new SchemaCommand($"ALTER INDEX {EntityNaming.Quote(EntityNaming.Schema)}.{EntityNaming.Quote(index.Name)} RENAME TO {EntityNaming.Quote(name)}"));
            indexes.Add(index with { Name = name, Columns = columns });
        }

        _tables[tableIndex] = table with
        {
            Columns = [.. table.Columns.Select(candidate => candidate == column ? column with { Name = to } : candidate)],
            Indexes = [.. indexes.OrderBy(index => index.Name, StringComparer.Ordinal)],
        };
        for (var index = 0; index < _enumValues.Count; index++)
        {
            var recorded = _enumValues[index];
            if (recorded.EntityId == entityId && string.Equals(recorded.FieldName, change.From, StringComparison.OrdinalIgnoreCase))
            {
                _enumValues[index] = recorded with { FieldName = change.To! };
            }
        }

        RecordChanges.Add(new MigrationRecordChange(MigrationChangeType.RenameField, entityId, change.From, change.To));
        DestructiveChanges.Add(new DestructiveChange(migration, entityId, change.From));
    }

    private void Drop(MigrationModel migration, int tableIndex, Guid entityId, MigrationChangeModel change)
    {
        var table = _tables[tableIndex];
        var name = EntityNaming.Column(change.Field!);
        if (!table.Columns.Any(candidate => candidate.Name == name))
        {
            return;
        }

        // Without CASCADE, so a dependency the planner does not know of stops the drop.
        Commands.Add(new SchemaCommand($"ALTER TABLE {EntityNaming.QualifiedTable(table.Name)} DROP COLUMN {EntityNaming.Quote(name)}"));

        // PostgreSQL drops every index over the column along with it, so the planner must not drop them again.
        _tables[tableIndex] = table with
        {
            Columns = [.. table.Columns.Where(candidate => candidate.Name != name)],
            Indexes = [.. table.Indexes.Where(index => !index.Columns.Contains(name, StringComparer.Ordinal))],
        };
        _enumValues.RemoveAll(recorded => recorded.EntityId == entityId && string.Equals(recorded.FieldName, change.Field, StringComparison.OrdinalIgnoreCase));
        RecordChanges.Add(new MigrationRecordChange(MigrationChangeType.DropField, entityId, change.Field, null));
        DestructiveChanges.Add(new DestructiveChange(migration, entityId, change.Field));
    }

    private void DropEntity(MigrationModel migration, Guid entityId, string table)
    {
        // Child tables come before the table they reference, deepest first. Never CASCADE.
        var dropped = new List<string>();
        CollectChildTables(table, dropped);
        dropped.Add(table);
        foreach (var name in dropped)
        {
            Commands.Add(new SchemaCommand($"DROP TABLE {EntityNaming.QualifiedTable(name)}"));
            _tables.RemoveAll(candidate => candidate.Name == name);
        }

        foreach (var name in dropped)
        {
            var recorded = _entities.Where(entity => entity.TableName == name).Select(entity => entity.EntityId).ToList();
            if (name == table && !recorded.Contains(entityId))
            {
                recorded.Add(entityId);
            }

            foreach (var id in recorded)
            {
                _entities.RemoveAll(entity => entity.EntityId == id);
                _enumValues.RemoveAll(value => value.EntityId == id);
                RecordChanges.Add(new MigrationRecordChange(MigrationChangeType.DropEntity, id, null, null));
                DestructiveChanges.Add(new DestructiveChange(migration, id, null));
            }
        }
    }

    // A child table has an owner column that references its owner's table.
    private void CollectChildTables(string owner, List<string> dropped)
    {
        var children = _tables
            .Where(candidate => candidate.Columns.Any(column => column.Name == EntityNaming.OwnerColumn && column.ReferencedTable == owner))
            .Select(candidate => candidate.Name)
            .ToList();
        foreach (var child in children)
        {
            if (!dropped.Contains(child))
            {
                CollectChildTables(child, dropped);
                dropped.Add(child);
            }
        }
    }
}
