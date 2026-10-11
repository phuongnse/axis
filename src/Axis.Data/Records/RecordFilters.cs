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
/// A bare name is a column of the row aliased <see cref="RecordQueries.RowAlias"/>, and a path
/// through reference fields is one scalar subquery per step, so a null reference gives a null
/// value and the row stays one row.
/// </summary>
public static class RecordFilters
{
    private const string SavepointName = "record_filter";

    /// <summary>
    /// The condition that a row matches any of <paramref name="filters"/>, combined with OR, for
    /// the user <paramref name="userId"/>. No filter matches no row.
    /// </summary>
    public static RecordCondition Condition(
        ApplicationModel application, EntityModel entity, IReadOnlyList<ExpressionModel> filters, string userId)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(userId);

        if (filters.Count == 0)
        {
            return new RecordCondition("FALSE", [], userId);
        }

        var subqueries = 0;
        var conditions = new List<string>(filters.Count);
        var values = new List<SqlValue>();
        for (var index = 0; index < filters.Count; index++)
        {
            var filter = filters[index];
            var translated = SqlTranslator.Translate(
                filter.Syntax,
                names => Column(application, entity, names, ref subqueries),
                filter.Check,
                $"g{index.ToString(CultureInfo.InvariantCulture)}f");
            if (!translated.Succeeded)
            {
                throw new InvalidOperationException($"The compiler let through a policy filter outside the SQL subset: {translated.Diagnostic.Message}");
            }

            conditions.Add($"({translated.Sql})");
            values.AddRange(translated.Parameters);
        }

        return new RecordCondition($"({string.Join(" OR ", conditions)})", values, userId);
    }

    /// <summary>
    /// Checks the record with <paramref name="id"/> against each of <paramref name="filters"/> in
    /// turn, one statement per filter, and stops at the first that matches. A filter the database
    /// rejects for this record with a data exception (SQLSTATE class 22), such as an integer
    /// overflow, does not match, and its index is in <see cref="RecordFilterMatch.Failed"/>. Any
    /// other database error is thrown. Inside <paramref name="transaction"/>, each statement runs
    /// under a savepoint, so a failed filter leaves the transaction usable.
    /// </summary>
    public static async Task<RecordFilterMatch> MatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ApplicationModel application,
        EntityModel entity,
        Guid id,
        IReadOnlyList<ExpressionModel> filters,
        string userId,
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
            var condition = Condition(application, entity, [filters[index]], userId);
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

    private static async Task ReleaseAsync(NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.ReleaseAsync(SavepointName, cancellationToken);
        }
    }

    /// <summary>
    /// The SQL of the column a filter path names. A bare name is a column of the row, and each
    /// reference step before the last field is a scalar subquery on the target's id, aliased
    /// <c>s0</c>, <c>s1</c>, … so every alias in the statement is unique.
    /// </summary>
    private static string Column(ApplicationModel application, EntityModel entity, IReadOnlyList<string> names, ref int subqueries)
    {
        var current = entity;
        var sql = EntityNaming.Quote(RecordQueries.RowAlias);
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
                var alias = EntityNaming.Quote($"s{subqueries++}");
                sql = $"(SELECT {alias}.{column} FROM {RecordQueries.Table(current)} AS {alias} WHERE {alias}.{EntityNaming.Quote(EntityNaming.IdColumn)} = {sql})";
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
}

/// <summary>
/// A record condition as SQL over the row aliased <c>r</c>, with the values it names and the
/// user id that <c>@currentUser</c> stands for.
/// </summary>
public sealed class RecordCondition
{
    private readonly IReadOnlyList<SqlValue> _values;
    private readonly string _userId;

    internal RecordCondition(string sql, IReadOnlyList<SqlValue> values, string userId)
    {
        Sql = sql;
        _values = values;
        _userId = userId;
    }

    /// <summary>The condition, wrapped in parentheses.</summary>
    public string Sql { get; }

    /// <summary>Adds new parameters for the values and the user id to <paramref name="command"/>.</summary>
    internal void AddTo(NpgsqlCommand command)
    {
        DataSourceQueries.AddValues(command, _values);
        command.Parameters.Add(new NpgsqlParameter(SqlTranslator.CurrentUserParameter, NpgsqlDbType.Text) { Value = _userId });
    }
}

/// <summary>
/// The result of <see cref="RecordFilters.MatchAsync"/>. <see cref="Found"/> is whether the record
/// exists, and <see cref="Version"/> its version then. <see cref="Matched"/> is whether a filter
/// matched it. <see cref="Failed"/> holds the index of each filter that failed for it.
/// </summary>
public sealed record RecordFilterMatch(bool Found, long Version, bool Matched, IReadOnlyList<int> Failed);
