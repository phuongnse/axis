using System.Security.Cryptography;
using System.Text;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Parsing;
using Axis.Expressions.Sql;
using Axis.Expressions.Syntax;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the migrations of an application and builds the checked ones. A release holds only the
/// entity as it is after its migrations, so the checker undoes the changes of every migration of
/// an entity, last change first, to get the entity as it was before them. It then replays them
/// forward in path order, so that what an earlier migration leaves behind is what a later one
/// starts from. A <c>renameField</c> or <c>dropField</c> names a field that exists at that point,
/// a <c>renameField</c> gives a name that is not yet a field, and a <c>fill</c> or <c>convert</c>
/// names a field of the entity as the release has it. A child collection is not a field for these
/// checks, because it has no column. Every <c>fill</c> and <c>convert</c> value is parsed and
/// checked against the SQL subset. A <c>fill</c> or <c>convert</c> whose target field is sensitive
/// is refused with <see cref="DiagnosticCodes.SensitiveFieldUse"/>, because its SQL would write
/// plain text into the encrypted column. It is also type-checked against the field's type, over the
/// record's own fields only, when the type is known at that point. It is not when a later change
/// drops or converts the field, when the value comes after a <c>dropEntity</c>, or when it reads a
/// field that this migration's own undo leaves without a type. The stored schema is known only at
/// activation, which a later issue builds, so a name that never existed in the stored table is not
/// caught here. A migration cannot name a module entity, so one whose <c>entity</c> contains <c>/</c>
/// is reported with <see cref="DiagnosticCodes.MigrationOfModuleEntity"/> and checked no further. An entity that is not loaded is not reported when its file failed to load or some
/// migration of that name drops it.
/// </summary>
internal static class MigrationChecker
{
    /// <summary>
    /// Checks <paramref name="migrations"/>, which come in path order. Every problem of a migration
    /// is reported in one pass.
    /// </summary>
    public static void Check(
        IReadOnlyList<MigrationResource> migrations,
        Func<string, EntityResource?> findEntity,
        IReadOnlySet<string> unloadedEntityNames,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        var groups = new Dictionary<string, List<MigrationResource>>(StringComparer.OrdinalIgnoreCase);
        foreach (var migration in migrations)
        {
            if (!groups.TryGetValue(migration.Entity, out var group))
            {
                groups[migration.Entity] = group = [];
            }

            group.Add(migration);
        }

        foreach (var (entityName, group) in groups)
        {
            if (entityName.Contains('/', StringComparison.Ordinal))
            {
                foreach (var migration in group)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.MigrationOfModuleEntity,
                        $"The migration names the module entity '{migration.Entity}'. A migration cannot change a module entity.",
                        migration.File,
                        "/entity",
                        migration.Id));
                }

                continue;
            }

            var entity = findEntity(entityName);
            var dropped = group.Any(migration => migration.Changes.Any(change => change.Type == "dropEntity"));
            if (entity is null && !dropped)
            {
                if (!unloadedEntityNames.Contains(entityName))
                {
                    foreach (var migration in group)
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticCodes.UnknownMigrationEntity,
                            $"The entity '{migration.Entity}' was not found. No loaded entity has that name.",
                            migration.File,
                            "/entity",
                            migration.Id));
                    }
                }

                continue;
            }

            if (entity is not null)
            {
                foreach (var migration in group)
                {
                    references.Add(migration, "/entity", ReferenceRelations.MigrationEntity, ResourceNode.Of(entity));
                }
            }

            CheckEntity(group, entity, references, diagnostics);
        }
    }

    /// <summary>
    /// Builds a checked migration. Its values stay as expression text. <paramref name="content"/> is
    /// the file's canonical JSON, which gives the content hash.
    /// </summary>
    public static MigrationModel Build(MigrationResource migration, string content) =>
        new()
        {
            Id = migration.Id,
            Name = migration.Name,
            File = migration.File,
            Entity = migration.Entity,
            Changes = migration.Changes
                .Select(change => new MigrationChangeModel(ParseType(change.Type), change.Field, change.From, change.To, change.Value))
                .ToList(),
            ContentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))),
        };

    /// <summary>
    /// The fields of an entity at one point: a name maps to its definition, or to null when the name
    /// is a field but its type is not known. A null state means the whole entity is not known.
    /// </summary>
    private static Dictionary<string, FieldDefinition?>? Copy(Dictionary<string, FieldDefinition?>? state) =>
        state is null ? null : new Dictionary<string, FieldDefinition?>(state, StringComparer.OrdinalIgnoreCase);

    private static void CheckEntity(
        List<MigrationResource> group, EntityResource? entity, ReferenceIndexBuilder references, List<Diagnostic> diagnostics)
    {
        var changes = group
            .SelectMany(migration => migration.Changes.Select((change, index) => (Migration: migration, Index: index, Change: change)))
            .ToList();

        // A dropEntity makes the entity unknown from there back. A loaded entity is the start of the
        // backward pass. Child collections and references fields are not fields here.
        Dictionary<string, FieldDefinition?>? state = null;
        if (entity is not null)
        {
            state = new Dictionary<string, FieldDefinition?>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in entity.Fields.Where(field => FieldTypes.Parse(field.Type) is not (FieldType.ChildCollection or FieldType.References)))
            {
                state.TryAdd(field.Name, field);
            }
        }

        var before = new Dictionary<string, FieldDefinition?>?[changes.Count];
        var after = new Dictionary<string, FieldDefinition?>?[changes.Count];
        for (var k = changes.Count - 1; k >= 0; k--)
        {
            after[k] = Copy(state);
            state = Undo(changes[k].Change, state);
            before[k] = Copy(state);
        }

        var droppedBefore = new bool[changes.Count];
        var seenDrop = false;
        for (var k = 0; k < changes.Count; k++)
        {
            droppedBefore[k] = seenDrop;
            seenDrop |= changes[k].Change.Type == "dropEntity";
        }

        var names = before.Length > 0 && before[0] is { } start
            ? new HashSet<string>(start.Keys, StringComparer.OrdinalIgnoreCase)
            : null;
        for (var k = 0; k < changes.Count; k++)
        {
            var (migration, index, change) = changes[k];
            void Report(string code, string message, string path) =>
                diagnostics.Add(new Diagnostic(code, message, migration.File, path, migration.Id));

            var missingAfter = false;
            if (change.Type is "fill" or "convert"
                && !droppedBefore[k]
                && after[k] is { } afterState
                && !afterState.ContainsKey(change.Field!))
            {
                missingAfter = true;
                Report(
                    DiagnosticCodes.MigrationFieldNotInEntity,
                    $"The field '{change.Field}' is not a field of the entity '{migration.Entity}' in this release. A child collection or a references field is not a field here.",
                    $"/changes/{index}/field");
            }

            if (change.Type is "fill" or "convert"
                && !missingAfter
                && after[k]?.GetValueOrDefault(change.Field!) is { Sensitive: true } target)
            {
                Report(
                    DiagnosticCodes.SensitiveFieldUse,
                    ApplicationCompiler.SensitiveUseMessage(migration.Entity, target.Name, "a migration fill or convert"),
                    $"/changes/{index}/field");
            }

            switch (change.Type)
            {
                case "renameField":
                    if (names is not null)
                    {
                        // Both names are checked before the set changes, so renaming a field to its own
                        // name, or to the same name in another case, is reported.
                        if (!names.Contains(change.From!))
                        {
                            Report(
                                DiagnosticCodes.UnknownMigrationField,
                                $"The field '{change.From}' is not a field of the entity '{migration.Entity}' at this change.",
                                $"/changes/{index}/from");
                        }

                        if (names.Contains(change.To!))
                        {
                            Report(
                                DiagnosticCodes.MigrationFieldAlreadyExists,
                                $"The field '{change.To}' already exists in the entity '{migration.Entity}' at this change.",
                                $"/changes/{index}/to");
                        }

                        names.Remove(change.From!);
                        names.Add(change.To!);
                    }

                    break;
                case "dropField":
                    if (names is not null && !names.Remove(change.Field!))
                    {
                        Report(
                            DiagnosticCodes.UnknownMigrationField,
                            $"The field '{change.Field}' is not a field of the entity '{migration.Entity}' at this change.",
                            $"/changes/{index}/field");
                    }

                    break;
                case "convert":
                    if (names is not null && !names.Contains(change.Field!))
                    {
                        Report(
                            DiagnosticCodes.UnknownMigrationField,
                            $"The field '{change.Field}' is not a field of the entity '{migration.Entity}' at this change.",
                            $"/changes/{index}/field");
                    }

                    break;
                case "fill":
                    names?.Add(change.Field!);
                    break;
                case "dropEntity":
                    names = null;
                    break;
            }

            if (change.Type is "fill" or "convert")
            {
                var problem = CheckValue(change, migration.Entity, before[k], after[k], missingAfter || droppedBefore[k], out var type);
                references.RecordExpression(migration, $"/changes/{index}/value", type, problem);
                if (problem is not null)
                {
                    Report(problem.Code, problem.Message, $"/changes/{index}/value");
                }
            }
        }
    }

    /// <summary>
    /// Steps a state back over one change. A renamed field takes its type back under its old name. A
    /// dropped or converted field existed before the change with a type that is not known.
    /// </summary>
    private static Dictionary<string, FieldDefinition?>? Undo(MigrationChangeDefinition change, Dictionary<string, FieldDefinition?>? state)
    {
        if (state is null)
        {
            return null;
        }

        switch (change.Type)
        {
            case "renameField":
                state.TryGetValue(change.To!, out var field);
                state.Remove(change.To!);
                state[change.From!] = field;
                return state;
            case "dropField":
                state[change.Field!] = null;
                return state;
            case "convert":
                if (state.ContainsKey(change.Field!))
                {
                    state[change.Field!] = null;
                }

                return state;
            case "dropEntity":
                return null;
            default:
                return state;
        }
    }

    /// <summary>
    /// Parses a <c>fill</c> or <c>convert</c> value and checks it against the SQL subset. It is also
    /// type-checked against the field's type when <paramref name="untyped"/> is false and the
    /// types of the field and of every field the value names are known. Returns the first problem.
    /// <paramref name="type"/> is the value's type when that check ran and succeeded, and null otherwise.
    /// A value that reads a sensitive field is refused, because it runs as SQL over the stored value, which is encrypted.
    /// </summary>
    private static ExpressionDiagnostic? CheckValue(
        MigrationChangeDefinition change,
        string entity,
        Dictionary<string, FieldDefinition?>? before,
        Dictionary<string, FieldDefinition?>? after,
        bool untyped,
        out ExpressionType? type)
    {
        type = null;
        var parsed = ExpressionParser.Parse(change.Value!);
        if (!parsed.Succeeded)
        {
            return parsed.Diagnostic;
        }

        var expression = parsed.Expression!;
        static string Column(IReadOnlyList<string> path) => string.Join('.', path);

        if (before is not null
            && NamedFields(expression).FirstOrDefault(name => before.GetValueOrDefault(name) is { Sensitive: true }) is { } sensitive)
        {
            return new ExpressionDiagnostic(
                DiagnosticCodes.SensitiveFieldUse,
                ApplicationCompiler.SensitiveUseMessage(entity, before[sensitive]!.Name, "a migration value"),
                0);
        }

        if (!untyped
            && before is not null
            && after is not null
            && after.GetValueOrDefault(change.Field!) is { } field
            && !NamedFields(expression).Any(name => before.TryGetValue(name, out var known) && known is null))
        {
            var scope = ExpressionScopes.ForMigration(
                before.Where(pair => pair.Value is not null).Select(pair => (pair.Key, pair.Value!)));
            var check = ExpressionTypeChecker.Check(expression, scope, ExpressionScopes.TypeOf(field)!);
            if (!check.Succeeded)
            {
                return check.Diagnostic;
            }

            type = check.Type;

            var typed = SqlTranslator.Translate(expression, Column, check);
            return typed.Succeeded ? null : typed.Diagnostic;
        }

        var translated = SqlTranslator.Translate(expression, Column);
        return translated.Succeeded ? null : translated.Diagnostic;
    }

    /// <summary>The names a value starts a field path at, in any order.</summary>
    private static IEnumerable<string> NamedFields(ExpressionNode node) => node switch
    {
        NameNode name => [name.Name],
        MemberNode member => NamedFields(member.Target),
        CallNode call => call.Arguments.SelectMany(NamedFields),
        UnaryNode unary => NamedFields(unary.Operand),
        BinaryNode binary => NamedFields(binary.Left).Concat(NamedFields(binary.Right)),
        IsNullNode isNull => NamedFields(isNull.Operand),
        InNode inNode => NamedFields(inNode.Operand).Concat(inNode.Items.SelectMany(NamedFields)),
        _ => [],
    };

    private static MigrationChangeType ParseType(string type) => type switch
    {
        "renameField" => MigrationChangeType.RenameField,
        "dropField" => MigrationChangeType.DropField,
        "dropEntity" => MigrationChangeType.DropEntity,
        "fill" => MigrationChangeType.Fill,
        "convert" => MigrationChangeType.Convert,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown migration change type."),
    };
}
