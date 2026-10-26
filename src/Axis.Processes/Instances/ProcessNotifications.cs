using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Expressions.Evaluation;
using Axis.Expressions.Typing;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Evaluates and writes the in-app notifications that <c>notify</c> steps and task escalations store
/// over a connection to a tenant database: one row for each user or role recipient, all with the
/// notification's id.
/// </summary>
internal static class ProcessNotifications
{
    /// <summary>The kind of a recipient that is one user, by user id.</summary>
    internal const string UserRecipient = "user";

    /// <summary>The kind of a recipient that is every user with one role.</summary>
    internal const string RoleRecipient = "role";

    /// <summary>The in-app channel, the only one that stores a notification yet.</summary>
    internal const string InAppChannel = "inApp";

    private const string DateFormat = "yyyy-MM-dd";

    // PostgreSQL stores microseconds, so six fraction digits show every stored value exactly.
    internal const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    /// <summary>
    /// Evaluates <paramref name="notification"/> into the notification it stores: its recipients first,
    /// then its placeholder values and the parameters of the page it opens, in the raw JSON form of an
    /// event payload. A <c>user</c> recipient that gives no non-blank text has no row, an <c>email</c>
    /// recipient is not evaluated yet, and two recipients of one kind and name are one. An expression
    /// that fails at run time fails the evaluation, whatever the channels. The caller gives the
    /// exceptions to throw for a failed expression, named by its part, and for a value with no JSON form.
    /// </summary>
    internal static async Task<NewNotification> EvaluateAsync(
        INotificationModel notification,
        Func<ExpressionModel, Task<ExpressionEvaluationResult>> evaluate,
        Guid applicationId,
        Guid releaseId,
        Func<string, ExpressionRuntimeError, Exception> expressionFailure,
        Func<object?, string, JsonValue?> toJson)
    {
        var recipients = new List<(string Kind, string Recipient)>();
        for (var index = 0; index < notification.Recipients.Count; index++)
        {
            var recipient = notification.Recipients[index];
            if (recipient.User is { } user)
            {
                var result = await evaluate(user);
                if (result.Error is { } error)
                {
                    throw expressionFailure($"user of recipient {index}", error);
                }

                if (result.Value is string id && !string.IsNullOrWhiteSpace(id))
                {
                    recipients.Add((UserRecipient, id));
                }
            }
            else if (recipient.Role is { } role)
            {
                recipients.Add((RoleRecipient, role));
            }
        }

        var values = new JsonObject();
        var valueTypes = new JsonObject();
        foreach (var value in notification.Values)
        {
            var result = await evaluate(value.Value);
            if (result.Error is { } error)
            {
                throw expressionFailure($"value '{value.Name}'", error);
            }

            values[value.Name] = toJson(result.Value, $"value '{value.Name}'");
            if (ValueTypeName(value.Value.Check.Type!.Kind) is { } typeName)
            {
                valueTypes[value.Name] = typeName;
            }
        }

        JsonObject? openParameters = null;
        if (notification.Open is { } open)
        {
            openParameters = new JsonObject();
            foreach (var parameter in open.Parameters)
            {
                var result = await evaluate(parameter.Value);
                if (result.Error is { } error)
                {
                    throw expressionFailure($"open parameter '{parameter.Parameter}'", error);
                }

                openParameters[parameter.Parameter] = toJson(result.Value, $"open parameter '{parameter.Parameter}'");
            }
        }

        return new NewNotification(
            Guid.CreateVersion7(),
            applicationId,
            releaseId,
            notification.Subject.TextKey,
            notification.Body.TextKey,
            values,
            valueTypes,
            notification.Open?.Page.Name,
            openParameters,
            [.. recipients.Distinct()]);
    }

    /// <summary>Whether <paramref name="notification"/> has a row to store: it has a recipient and goes out on the in-app channel. The email channel stores nothing yet.</summary>
    internal static bool ShouldStore(NewNotification notification, IReadOnlyList<string> channels) =>
        notification.Recipients.Count > 0 && channels.Contains(InAppChannel, StringComparer.Ordinal);

    /// <summary>
    /// A value an expression gave, in the record API's JSON form, which is null for a null value.
    /// False when the value has no JSON form.
    /// </summary>
    internal static bool TryToJson(object? value, out JsonValue? json)
    {
        json = null;
        switch (value)
        {
            case null:
                return true;
            case string text:
                json = JsonValue.Create(text);
                return true;
            case long number:
                json = JsonValue.Create(number);
                return true;
            case decimal number:
                json = JsonValue.Create(number);
                return true;
            case bool flag:
                json = JsonValue.Create(flag);
                return true;
            case DateOnly date:
                json = JsonValue.Create(date.ToString(DateFormat, CultureInfo.InvariantCulture));
                return true;
            case DateTimeOffset time:
                json = JsonValue.Create(time.UtcDateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture));
                return true;
            case Guid id:
                json = JsonValue.Create(id.ToString("D"));
                return true;
            default:
                return false;
        }
    }

    /// <summary>The name that a notification stores for a value of <paramref name="kind"/>, or null when it stores none.</summary>
    private static string? ValueTypeName(ExpressionTypeKind kind) =>
        kind switch
        {
            ExpressionTypeKind.Text => "text",
            ExpressionTypeKind.Integer => "integer",
            ExpressionTypeKind.Decimal => "decimal",
            ExpressionTypeKind.Boolean => "boolean",
            ExpressionTypeKind.Date => "date",
            ExpressionTypeKind.DateTime => "date-time",
            ExpressionTypeKind.Enum => "enum",
            ExpressionTypeKind.Reference => "reference",
            _ => null,
        };

    /// <summary>Inserts a row of <paramref name="notification"/> for each of its recipients. They are created at the transaction's time.</summary>
    internal static async Task InsertAsync(NpgsqlTransaction transaction, NewNotification notification, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.notifications
                (id, application_id, release_id, recipient_kind, recipient, subject_key, body_key, "values", value_types, open_page, open_parameters, created_at)
            SELECT @id, @application, @release, recipient.kind, recipient.name, @subject, @body, @values, @valueTypes, @page, @parameters, now()
            FROM unnest(@kinds, @recipients) AS recipient(kind, name)
            """,
            transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction)),
            transaction);
        command.Parameters.AddWithValue("id", notification.Id);
        command.Parameters.AddWithValue("application", notification.ApplicationId);
        command.Parameters.AddWithValue("release", notification.ReleaseId);
        command.Parameters.AddWithValue("subject", notification.SubjectKey);
        command.Parameters.AddWithValue("body", notification.BodyKey);
        command.Parameters.Add(new NpgsqlParameter("values", NpgsqlDbType.Jsonb) { Value = notification.Values.ToJsonString() });
        command.Parameters.Add(new NpgsqlParameter("valueTypes", NpgsqlDbType.Jsonb) { Value = notification.ValueTypes.ToJsonString() });
        command.Parameters.Add(new NpgsqlParameter("page", NpgsqlDbType.Text) { Value = (object?)notification.OpenPage ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("parameters", NpgsqlDbType.Jsonb) { Value = (object?)notification.OpenParameters?.ToJsonString() ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("kinds", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = notification.Recipients.Select(r => r.Kind).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("recipients", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = notification.Recipients.Select(r => r.Recipient).ToArray() });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>An in-app notification that a <c>notify</c> step stores for its recipients.</summary>
/// <param name="Id">The notification's id, a version 7 UUID. Every recipient's row has it.</param>
/// <param name="ReleaseId">The release the notifying instance is pinned to.</param>
/// <param name="SubjectKey">The text key of the subject.</param>
/// <param name="BodyKey">The text key of the body.</param>
/// <param name="Values">The raw evaluated placeholder values, keyed by the placeholders' names.</param>
/// <param name="ValueTypes">The type name of each value, keyed by the placeholders' names. A value of type null or file has none.</param>
/// <param name="OpenPage">The declared name of the page the notification opens, or null.</param>
/// <param name="OpenParameters">The raw evaluated page parameters, keyed by the parameters' declared names, or null when there is no page.</param>
/// <param name="Recipients">The distinct user and role recipients, each by kind and by user id or role name.</param>
internal sealed record NewNotification(
    Guid Id,
    Guid ApplicationId,
    Guid ReleaseId,
    string SubjectKey,
    string BodyKey,
    JsonObject Values,
    JsonObject ValueTypes,
    string? OpenPage,
    JsonObject? OpenParameters,
    IReadOnlyList<(string Kind, string Recipient)> Recipients);
