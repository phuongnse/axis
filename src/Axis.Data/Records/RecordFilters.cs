using System.Globalization;
using Axis.Configuration.Model;
using Axis.Data.DataSources;
using Axis.Data.Naming;
using Axis.Expressions.Sql;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Records;

/// <summary>
/// Runs the record filters of policy grants as SQL over the rows of an entity table. Each filter
/// is translated by <see cref="SqlTranslator"/>, with the body of each rule it calls inlined. The
/// literals of filter <c>i</c> are <c>@g{i}f0</c>, <c>@g{i}f1</c>, …, and <c>currentUser()</c> is
/// <c>@currentUser</c>, bound as text to the user's id. So no value is ever part of the SQL text.
/// A test of an exact lookup field with <c>==</c> or <c>!=</c> against a literal compares the field's
/// lookup column with the parameter <c>@g{i}fl{n}</c>, a <c>bytea[]</c> that holds the literal's hash
/// under every lookup key of the tenant, so it needs the tenant's <see cref="RecordCipher"/>.
/// A bare name is a column of the row aliased <see cref="RecordQueries.RowAlias"/>, and a path
/// through reference fields is one scalar subquery per step, so a null reference gives a null
/// value and the row stays one row. <c>any</c> and <c>count</c> over a child collection or a
/// references field are subqueries on the child table, or on the junction table joined to the
/// linked entity's table, tied to the row's id.
/// </summary>
public static class RecordFilters
{
    private const string SavepointName = "record_filter";

    /// <summary>
    /// The condition that a row matches any of <paramref name="filters"/>, combined with OR, for
    /// the user <paramref name="userId"/>. No filter matches no row. A test of an exact lookup field
    /// is hashed with <paramref name="cipher"/>, which it needs.
    /// </summary>
    /// <exception cref="InvalidOperationException">A filter tests an exact lookup field and <paramref name="cipher"/> is null.</exception>
    public static async Task<RecordCondition> ConditionAsync(
        ApplicationModel application,
        EntityModel entity,
        IReadOnlyList<ExpressionModel> filters,
        string userId,
        RecordCipher? cipher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(userId);

        if (filters.Count == 0)
        {
            return new RecordCondition("FALSE", [], [], userId);
        }

        var aliases = new AliasCounter();
        var conditions = new List<string>(filters.Count);
        var values = new List<SqlValue>();
        var lookups = new List<(string Name, byte[][] Hashes)>();
        for (var index = 0; index < filters.Count; index++)
        {
            var filter = filters[index];
            var translated = SqlTranslator.Translate(
                filter.Syntax,
                names => Column(application, entity, RecordQueries.RowAlias, names, aliases),
                filter.Check,
                $"g{index.ToString(CultureInfo.InvariantCulture)}f",
                application.TimeZone,
                name => Collection(
                    application,
                    entity,
                    name,
                    $"{EntityNaming.Quote(RecordQueries.RowAlias)}.{EntityNaming.Quote(EntityNaming.IdColumn)}",
                    aliases),
                names => names is [var name] && entity.TryGetField(name, out var field) && field.ExactLookup
                    ? $"{EntityNaming.Quote(RecordQueries.RowAlias)}.{EntityNaming.Quote(EntityNaming.LookupColumn(field.Name))}"
                    : null);
            if (!translated.Succeeded)
            {
                throw new InvalidOperationException($"The compiler let through a policy filter outside the SQL subset: {translated.Diagnostic.Message}");
            }

            foreach (var lookup in translated.Lookups)
            {
                if (lookup.Parameter is not null)
                {
                    throw new InvalidOperationException(
                        $"The compiler let through a policy filter that tests the field '{lookup.Path[0]}' against the name '{lookup.Parameter}'. A policy filter has no parameters.");
                }

                lookups.Add((lookup.Name, await LookupHashesAsync(entity, lookup.Path[0], lookup.Value!, cipher, cancellationToken)));
            }

            conditions.Add($"({translated.Sql})");
            values.AddRange(translated.Parameters);
        }

        return new RecordCondition($"({string.Join(" OR ", conditions)})", values, lookups, userId);
    }

    /// <summary>
    /// The hashes of <paramref name="value"/> for the exact lookup field <paramref name="fieldName"/> of
    /// <paramref name="entity"/>, under every lookup key of the tenant. A value that no stored value can equal,
    /// such as a fraction for an integer field, has none, so <c>==</c> matches no row. The value is a literal
    /// of the filter or the value of a data source parameter, as <see cref="SqlValue"/> and
    /// <see cref="Axis.Data.DataSources.DataSourceParameterValue"/> give it.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="cipher"/> is null, or the value's type does not fit the field.</exception>
    internal static async Task<byte[][]> LookupHashesAsync(
        EntityModel entity, string fieldName, object value, RecordCipher? cipher, CancellationToken cancellationToken)
    {
        if (cipher is null)
        {
            throw new InvalidOperationException("A filter on an exact lookup field needs the tenant's lookup keys.");
        }

        if (!entity.TryGetField(fieldName, out var field) || LookupValue(field, value) is not { } stored)
        {
            return [];
        }

        return [.. await cipher.Values.LookupAllAsync(cipher.TenantId, entity.Id, new RecordValue(field, stored), cancellationToken)];
    }

    // The value in the shape that a write of the field has, so its text form is the one a write hashed.
    private static object? LookupValue(FieldModel field, object value) =>
        (field.Type, value) switch
        {
            (FieldType.Text or FieldType.Enum, string text) => text,
            (FieldType.Integer, long number) => number,
            (FieldType.Integer, decimal number) => number == decimal.Truncate(number) ? (long)number : null,
            (FieldType.Decimal or FieldType.Money, long number) => number.ToString(CultureInfo.InvariantCulture),
            (FieldType.Decimal or FieldType.Money, decimal number) => number.ToString(CultureInfo.InvariantCulture),
            (FieldType.Boolean, bool flag) => flag,
            _ => throw new InvalidOperationException(
                $"The compiler let through an exact lookup filter on the field '{field.Name}' with a {value.GetType().Name} value."),
        };

    /// <summary>
    /// Checks the record with <paramref name="id"/> against each of <paramref name="filters"/> in
    /// turn, one statement per filter, and stops at the first that matches. A filter the database
    /// rejects for this record with a data exception (SQLSTATE class 22), such as an integer
    /// overflow, does not match, and its index is in <see cref="RecordFilterMatch.Failed"/>. Any
    /// other database error is thrown. Inside <paramref name="transaction"/>, each statement runs
    /// under a savepoint, so a failed filter leaves the transaction usable. A test of an exact lookup
    /// field is hashed with <paramref name="cipher"/>, as in <see cref="ConditionAsync"/>.
    /// </summary>
    public static async Task<RecordFilterMatch> MatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ApplicationModel application,
        EntityModel entity,
        Guid id,
        IReadOnlyList<ExpressionModel> filters,
        string userId,
        RecordCipher? cipher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(userId);

        var row = EntityNaming.Quote(RecordQueries.RowAlias);
        var from = $"FROM {RecordQueries.Table(entity)} AS {row} WHERE {row}.{EntityNaming.Quote(EntityNaming.IdColumn)} = @id";
        var version = EntityNaming.Quote(EntityNaming.VersionColumn);
        var failed = new List<int>();
        long? found = null;
        for (var index = 0; index < filters.Count; index++)
        {
            var condition = await ConditionAsync(application, entity, [filters[index]], userId, cipher, cancellationToken);
            await using var command = new NpgsqlCommand($"SELECT {row}.{version}, {condition.Sql} IS TRUE {from}", connection);
            command.Parameters.AddWithValue("id", id);
            condition.AddTo(command);
            if (transaction is not null)
            {
                await transaction.SaveAsync(SavepointName, cancellationToken);
            }

            bool? matched;
            try
            {
                await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    matched = await reader.ReadAsync(cancellationToken) ? reader.GetBoolean(1) : null;
                    found = matched is null ? null : reader.GetInt64(0);
                }

                await ReleaseAsync(transaction, cancellationToken);
            }
            catch (PostgresException exception) when (exception.SqlState.StartsWith("22", StringComparison.Ordinal))
            {
                // Class 22 is a data exception: the filter could not be evaluated for this record.
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(SavepointName, cancellationToken);
                    await transaction.ReleaseAsync(SavepointName, cancellationToken);
                }

                failed.Add(index);
                continue;
            }

            if (matched is null)
            {
                // The record is gone, so no filter failed for it.
                failed.Clear();
                break;
            }

            if (matched.Value)
            {
                return new RecordFilterMatch(true, found!.Value, true, failed);
            }
        }

        // When every filter failed, no statement said whether the record exists.
        if (found is null && failed.Count > 0)
        {
            await using var command = new NpgsqlCommand($"SELECT {row}.{version} {from}", connection);
            command.Parameters.AddWithValue("id", id);
            found = await command.ExecuteScalarAsync(cancellationToken) as long?;
        }

        return found is { } stored
            ? new RecordFilterMatch(true, stored, false, failed)
            : new RecordFilterMatch(false, 0, false, failed);
    }

    /// <summary>
    /// The entity and the field name of every field that <paramref name="filter"/> reads, in the order the
    /// translation finds them, with the body of each rule it calls inlined. Each step of a path through
    /// reference fields reads a field of the entity before it, so a path reads one field of each entity on it.
    /// </summary>
    public static IReadOnlyList<(EntityModel Entity, string Field)> FieldsRead(
        ApplicationModel application, EntityModel entity, ExpressionModel filter)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(filter);

        var read = new List<(EntityModel, string)>();
        string Read(EntityModel start, IReadOnlyList<string> names)
        {
            var current = start;
            for (var index = 0; index < names.Count; index++)
            {
                if (!current.TryGetField(names[index], out var field))
                {
                    throw new InvalidOperationException(
                        $"The compiler let through the policy filter path '{string.Join('.', names)}' that does not resolve.");
                }

                read.Add((current, field.Name));
                if (index < names.Count - 1)
                {
                    current = field.Target is { } target && application.FindEntity(target.Id) is { } next
                        ? next
                        : throw new InvalidOperationException(
                            $"The compiler let through the policy filter path '{string.Join('.', names)}' that does not resolve.");
                }
            }

            return "NULL";
        }

        var translated = SqlTranslator.Translate(
            filter.Syntax,
            names => Read(entity, names),
            filter.Check,
            "g0f",
            collection: name => new SqlCollection("NULL", "TRUE", names => Read(ItemEntity(application, entity, name), names)));
        if (!translated.Succeeded)
        {
            throw new InvalidOperationException($"The compiler let through a policy filter outside the SQL subset: {translated.Diagnostic.Message}");
        }

        return read;
    }

    private static async Task ReleaseAsync(NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.ReleaseAsync(SavepointName, cancellationToken);
        }
    }

    /// <summary>
    /// The SQL of the column a filter path names, starting at the row of <paramref name="entity"/>
    /// aliased <paramref name="alias"/>. A bare name is a column of the row, and each reference step
    /// before the last field is a scalar subquery on the target's id, aliased <c>s0</c>, <c>s1</c>, …
    /// so every alias in the statement is unique.
    /// </summary>
    internal static string Column(
        ApplicationModel application, EntityModel entity, string alias, IReadOnlyList<string> names, AliasCounter aliases)
    {
        var current = entity;
        var sql = EntityNaming.Quote(alias);
        for (var index = 0; index < names.Count; index++)
        {
            if (!current.TryGetField(names[index], out var field) || !field.HasColumn)
            {
                throw new InvalidOperationException(
                    $"The compiler let through the policy filter path '{string.Join('.', names)}' that does not resolve.");
            }

            var column = EntityNaming.Quote(EntityNaming.Column(field.Name));
            if (index == 0)
            {
                sql = $"{sql}.{column}";
            }
            else
            {
                var subquery = EntityNaming.Quote($"s{aliases.Next()}");
                sql = $"(SELECT {subquery}.{column} FROM {RecordQueries.Table(current)} AS {subquery} WHERE {subquery}.{EntityNaming.Quote(EntityNaming.IdColumn)} = {sql})";
            }

            if (index < names.Count - 1)
            {
                current = field.Target is { } target && application.FindEntity(target.Id) is { } next
                    ? next
                    : throw new InvalidOperationException(
                        $"The compiler let through the policy filter path '{string.Join('.', names)}' that does not resolve.");
            }
        }

        return sql;
    }

    /// <summary>
    /// The SQL source of the child collection or references field <paramref name="name"/> of
    /// <paramref name="owner"/>, for <c>any</c> and <c>count</c>. A child collection reads the child
    /// table, and a references field reads its junction table joined to the linked entity's table.
    /// Its rows belong to the row whose id is <paramref name="ownerIdSql"/>. The item columns start at
    /// the child row, or at the linked record. A path inside the condition is a scalar subquery, as
    /// in <see cref="Column"/>. The aliases <c>a</c><i>n</i> and <c>a</c><i>n</i><c>t</c> come from
    /// <paramref name="aliases"/>.
    /// </summary>
    internal static SqlCollection Collection(
        ApplicationModel application, EntityModel owner, string name, string ownerIdSql, AliasCounter aliases)
    {
        var item = ItemEntity(application, owner, name);
        var field = owner.Fields.First(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        var number = aliases.Next();
        var alias = EntityNaming.Quote($"a{number}");
        var ownerMatch = $"{alias}.{EntityNaming.Quote(EntityNaming.OwnerColumn)} = {ownerIdSql}";
        if (field.Type == FieldType.ChildCollection)
        {
            return new SqlCollection(
                $"{RecordQueries.Table(item)} AS {alias}",
                ownerMatch,
                names => Column(application, item, $"a{number}", names, aliases));
        }

        var linked = EntityNaming.Quote($"a{number}t");
        var junction = EntityNaming.QualifiedTable(EntityNaming.Junction(owner.Id, field.Name));
        return new SqlCollection(
            $"{junction} AS {alias} JOIN {RecordQueries.Table(item)} AS {linked} "
            + $"ON {linked}.{EntityNaming.Quote(EntityNaming.IdColumn)} = {alias}.{EntityNaming.Quote(EntityNaming.TargetColumn)}",
            ownerMatch,
            names => Column(application, item, $"a{number}t", names, aliases));
    }

    /// <summary>The child entity of the child collection, or the target entity of the references field, <paramref name="name"/> of <paramref name="owner"/>.</summary>
    internal static EntityModel ItemEntity(ApplicationModel application, EntityModel owner, string name) =>
        owner.TryGetField(name, out var field)
        && field.Type is FieldType.ChildCollection or FieldType.References
        && field.Target is { } target
        && application.FindEntity(target.Id) is { } item
            ? item
            : throw new InvalidOperationException($"The compiler let through the policy filter list '{name}' that does not resolve.");
}

/// <summary>Numbers the aliases of the subqueries of one translation, so each is unique in the statement.</summary>
internal sealed class AliasCounter
{
    private int _next;

    public int Next() => _next++;
}

/// <summary>
/// A record condition as SQL over the row aliased <c>r</c>, with the values it names, the lookup
/// hashes it names and the user id that <c>@currentUser</c> stands for.
/// </summary>
public sealed class RecordCondition
{
    private readonly IReadOnlyList<SqlValue> _values;
    private readonly IReadOnlyList<(string Name, byte[][] Hashes)> _lookups;
    private readonly string _userId;

    internal RecordCondition(string sql, IReadOnlyList<SqlValue> values, IReadOnlyList<(string Name, byte[][] Hashes)> lookups, string userId)
    {
        Sql = sql;
        _values = values;
        _lookups = lookups;
        _userId = userId;
    }

    /// <summary>The condition, wrapped in parentheses.</summary>
    public string Sql { get; }

    /// <summary>Adds new parameters for the values, the lookup hashes and the user id to <paramref name="command"/>.</summary>
    internal void AddTo(NpgsqlCommand command)
    {
        DataSourceQueries.AddValues(command, _values);
        foreach (var (name, hashes) in _lookups)
        {
            command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Bytea) { Value = hashes });
        }

        command.Parameters.Add(new NpgsqlParameter(SqlTranslator.CurrentUserParameter, NpgsqlDbType.Text) { Value = _userId });
    }
}

/// <summary>
/// The result of <see cref="RecordFilters.MatchAsync"/>. <see cref="Found"/> is whether the record
/// exists, and <see cref="Version"/> its version then. <see cref="Matched"/> is whether a filter
/// matched it. <see cref="Failed"/> holds the index of each filter that failed for it.
/// </summary>
public sealed record RecordFilterMatch(bool Found, long Version, bool Matched, IReadOnlyList<int> Failed);
