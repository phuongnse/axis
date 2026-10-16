using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Expressions.Parsing;
using Axis.Expressions.Sql;
using Axis.Expressions.Syntax;

namespace Axis.Data.Schema;

/// <summary>
/// Applies the pending migrations of a release to in-memory copies of the catalog snapshot and the
/// provisioning records, and collects the commands and record changes that do the same to the
/// tenant database. Planning the release then runs on the copies, so a removed field or entity that
/// a migration covers is not reported, and a field that a <c>fill</c> covers may become required. A
/// change that does not fit the stored schema is skipped: the table or the <c>from</c> column is
/// missing, or the <c>to</c> column exists. If something is left over, the planner still reports it.
/// A <c>fill</c> on a child entity, or of a field that a computed field reads, is refused, because
/// activation cannot recompute computed fields yet.
/// </summary>
internal sealed class MigrationPlanning
{
    private readonly ApplicationModel _model;
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

    public CatalogSnapshot Catalog => new(_tables);

    public ProvisioningRecords Records => new(_entities, _enumValues);

    private void Apply(MigrationModel migration, int index, MigrationChangeModel change)
    {
        if (change.Type == MigrationChangeType.Convert)
        {
            NotApplied(migration, index, "The 'convert' change is not applied yet. Activation applies renameField, dropField, dropEntity and fill.");
            return;
        }

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
                Rename(tableIndex, id, change);
                break;
            case MigrationChangeType.DropField:
                Drop(tableIndex, id, change);
                break;
            case MigrationChangeType.DropEntity:
                DropEntity(id, table);
                break;
            case MigrationChangeType.Fill:
                Fill(migration, index, tableIndex, id, change);
                break;
        }
    }

    private void NotApplied(MigrationModel migration, int index, string message) =>
        Diagnostics.Add(new Diagnostic(DiagnosticCodes.MigrationChangeNotApplied, message, migration.File, $"/changes/{index}/type", migration.Id));

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

        if (table.Columns.Any(candidate => candidate.Name == EntityNaming.OwnerColumn))
        {
            NotApplied(migration, index, "A 'fill' on a child entity is not applied yet: it must also change the owner's version and recompute the owner's computed fields.");
            return;
        }

        var releaseEntity = _model.Entities.FirstOrDefault(entity => entity.Id == entityId);
        var reader = releaseEntity?.Fields.FirstOrDefault(field => field.Computed is { } computed && Reads(computed.Syntax, change.Field!));
        if (reader is not null)
        {
            NotApplied(migration, index, $"A 'fill' of '{change.Field}' is not applied yet: the computed field '{reader.Name}' reads it, and activation cannot recompute computed fields.");
            return;
        }

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

        var translated = SqlTranslator.Translate(parsed.Expression!, Column);
        if (!translated.Succeeded)
        {
            throw new InvalidOperationException($"The fill value does not translate to SQL, though the compiler checked it: {translated.Diagnostic.Message}");
        }

        if (unknown)
        {
            return;
        }

        var version = columns.Any(candidate => candidate.Name == EntityNaming.VersionColumn)
            ? $", {EntityNaming.Quote(EntityNaming.VersionColumn)} = {EntityNaming.Quote(EntityNaming.VersionColumn)} + 1"
            : "";
        var quotedColumn = EntityNaming.Quote(column);
        Commands.AddRange(addColumn);
        Commands.Add(new SchemaCommand(
            $"WITH filled AS (UPDATE {quotedTable} SET {quotedColumn} = {translated.Sql}{version} WHERE {quotedColumn} IS NULL RETURNING {quotedColumn}) "
            + $"SELECT count(*) FROM filled WHERE {quotedColumn} IS NULL")
        {
            Parameters = translated.Parameters,
            Fill = new FillTarget(migration.Id, migration.File, index, change.Field!),
        });
        _tables[tableIndex] = table with { Columns = columns };
    }

    // Field names match ignoring letter case.
    private static bool Reads(ExpressionNode node, string field) => node switch
    {
        NameNode name => string.Equals(name.Name, field, StringComparison.OrdinalIgnoreCase),
        MemberNode member => Reads(member.Target, field),
        CallNode call => call.Arguments.Any(argument => Reads(argument, field)),
        UnaryNode unary => Reads(unary.Operand, field),
        BinaryNode binary => Reads(binary.Left, field) || Reads(binary.Right, field),
        IsNullNode isNull => Reads(isNull.Operand, field),
        InNode inNode => Reads(inNode.Operand, field) || inNode.Items.Any(item => Reads(item, field)),
        _ => false,
    };

    private void Rename(int tableIndex, Guid entityId, MigrationChangeModel change)
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

        _tables[tableIndex] = table with
        {
            Columns = [.. table.Columns.Select(candidate => candidate == column ? column with { Name = to } : candidate)],
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
    }

    private void Drop(int tableIndex, Guid entityId, MigrationChangeModel change)
    {
        var table = _tables[tableIndex];
        var name = EntityNaming.Column(change.Field!);
        if (!table.Columns.Any(candidate => candidate.Name == name))
        {
            return;
        }

        // Without CASCADE, so a dependency the planner does not know of stops the drop.
        Commands.Add(new SchemaCommand($"ALTER TABLE {EntityNaming.QualifiedTable(table.Name)} DROP COLUMN {EntityNaming.Quote(name)}"));
        _tables[tableIndex] = table with { Columns = [.. table.Columns.Where(candidate => candidate.Name != name)] };
        _enumValues.RemoveAll(recorded => recorded.EntityId == entityId && string.Equals(recorded.FieldName, change.Field, StringComparison.OrdinalIgnoreCase));
        RecordChanges.Add(new MigrationRecordChange(MigrationChangeType.DropField, entityId, change.Field, null));
    }

    private void DropEntity(Guid entityId, string table)
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
