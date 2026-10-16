using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Data.Naming;

namespace Axis.Data.Schema;

/// <summary>
/// Applies the pending migrations of a release to in-memory copies of the catalog snapshot and the
/// provisioning records, and collects the statements and record changes that do the same to the
/// tenant database. Planning the release then runs on the copies, so a removed field or entity that
/// a migration covers is not reported. A change that does not fit the stored schema is skipped: the
/// table or the <c>from</c> column is missing, or the <c>to</c> column exists. If something is left
/// over, the planner still reports it.
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

    public List<string> Statements { get; } = [];

    public List<Diagnostic> Diagnostics { get; } = [];

    public List<MigrationRecordChange> RecordChanges { get; } = [];

    public CatalogSnapshot Catalog => new(_tables);

    public ProvisioningRecords Records => new(_entities, _enumValues);

    private void Apply(MigrationModel migration, int index, MigrationChangeModel change)
    {
        if (change.Type is MigrationChangeType.Fill or MigrationChangeType.Convert)
        {
            Diagnostics.Add(new Diagnostic(
                DiagnosticCodes.MigrationChangeNotApplied,
                $"The '{(change.Type == MigrationChangeType.Fill ? "fill" : "convert")}' change is not applied yet. Activation applies only renameField, dropField and dropEntity.",
                migration.File,
                $"/changes/{index}/type",
                migration.Id));
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
        Statements.Add($"{alterTable} RENAME COLUMN {EntityNaming.Quote(from)} TO {EntityNaming.Quote(to)}");
        if (column.Unique)
        {
            Statements.Add($"{alterTable} RENAME CONSTRAINT {EntityNaming.Quote(EntityNaming.Unique(table.Name, from))} TO {EntityNaming.Quote(EntityNaming.Unique(table.Name, to))}");
        }

        if (column.ReferencedTable is not null)
        {
            Statements.Add($"{alterTable} RENAME CONSTRAINT {EntityNaming.Quote(EntityNaming.ForeignKey(table.Name, from))} TO {EntityNaming.Quote(EntityNaming.ForeignKey(table.Name, to))}");
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
        Statements.Add($"ALTER TABLE {EntityNaming.QualifiedTable(table.Name)} DROP COLUMN {EntityNaming.Quote(name)}");
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
            Statements.Add($"DROP TABLE {EntityNaming.QualifiedTable(name)}");
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
