using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Data.Records;
using Axis.Processes.Records;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Scenarios;

/// <summary>
/// Runs the steps of one scenario over its tenant database connection. It keeps the records that
/// <c>createRecord</c> steps saved under an alias. A step that fails throws <see cref="StepFailedException"/>.
/// </summary>
internal sealed class ScenarioSteps(
    NpgsqlConnection connection,
    ApplicationModel model,
    Guid releaseId,
    ScenarioResource scenario,
    ILoggerFactory loggerFactory)
{
    private readonly Dictionary<string, (EntityModel Entity, Guid Id)> _saved = new(StringComparer.Ordinal);

    public async Task RunAsync(ScenarioStepDefinition step, CancellationToken cancellationToken)
    {
        switch (step.Type)
        {
            case ScenarioStepDefinition.CreateRecord:
                await CreateAsync(step, cancellationToken);
                break;
            case ScenarioStepDefinition.UpdateRecord:
                await UpdateAsync(step, cancellationToken);
                break;
            case ScenarioStepDefinition.DeleteRecord:
                await DeleteAsync(step, cancellationToken);
                break;
            case ScenarioStepDefinition.ExpectRecord:
                await ExpectRecordAsync(step, cancellationToken);
                break;
            default:
                throw new StepFailedException($"step type '{step.Type}' is not supported yet");
        }
    }

    private async Task CreateAsync(ScenarioStepDefinition step, CancellationToken cancellationToken)
    {
        RejectExpect(step);
        var entity = Entity(step.Entity);
        var body = Body(version: null, step.Values);
        var result = await RecordCommand.CreateAsync(
            connection, ScenarioRunner.TenantId, model, releaseId, entity, Actor(step), _ => Task.FromResult<byte[]?>(body), loggerFactory, cancellationToken);
        RequireOutcome(result, RecordCommandOutcome.Written);
        if (step.Save is { } alias)
        {
            _saved[alias] = (entity, result.Record!.Id);
        }
    }

    private async Task UpdateAsync(ScenarioStepDefinition step, CancellationToken cancellationToken)
    {
        RejectExpect(step);
        var (entity, id) = Saved(step.Record);
        var stored = await RecordQueries.GetAsync(connection, model, entity, id, cancellationToken)
            ?? throw new StepFailedException($"record '{step.Record}' no longer exists");
        var body = Body(stored.Version, step.Values);
        var result = await RecordCommand.UpdateAsync(
            connection, ScenarioRunner.TenantId, model, releaseId, entity, id, Actor(step), _ => Task.FromResult<byte[]?>(body), loggerFactory, cancellationToken);
        RequireOutcome(result, RecordCommandOutcome.Written);
    }

    private async Task DeleteAsync(ScenarioStepDefinition step, CancellationToken cancellationToken)
    {
        RejectExpect(step);
        var (entity, id) = Saved(step.Record);
        var result = await RecordCommand.DeleteAsync(connection, model, entity, id, Actor(step), loggerFactory, cancellationToken);
        RequireOutcome(result, RecordCommandOutcome.Deleted);
    }

    private async Task ExpectRecordAsync(ScenarioStepDefinition step, CancellationToken cancellationToken)
    {
        var (entity, id) = Saved(step.Record);
        var stored = await RecordQueries.GetAsync(connection, model, entity, id, cancellationToken)
            ?? throw new StepFailedException($"record '{step.Record}' does not exist");
        if (RecordExpectation.FirstDifference(model, entity, stored.Values, step.Values ?? new Dictionary<string, JsonElement>()) is { } difference)
        {
            throw new StepFailedException(difference);
        }
    }

    private static void RejectExpect(ScenarioStepDefinition step)
    {
        if (step.Expect is not null)
        {
            throw new StepFailedException("expect is not supported yet");
        }
    }

    private static void RequireOutcome(RecordCommandResult result, RecordCommandOutcome expected)
    {
        if (result.Outcome == expected)
        {
            return;
        }

        var message = $"expected success but the outcome was {result.Outcome}";
        if (result.Errors is { Count: > 0 } errors)
        {
            message += ": " + string.Join("; ", errors.Select(error => $"{error.Key} {string.Join(", ", error.Value)}"));
        }

        throw new StepFailedException(message);
    }

    private EntityModel Entity(string? name) =>
        name is not null && model.TryGetEntity(name, out var entity)
            ? entity
            : throw new StepFailedException($"entity '{name}' does not exist");

    private (EntityModel Entity, Guid Id) Saved(string? alias) =>
        alias is not null && _saved.TryGetValue(alias, out var saved)
            ? saved
            : throw new StepFailedException($"no record is saved as '{alias}'");

    private RecordActor Actor(ScenarioStepDefinition step) =>
        step.As is { } user && scenario.Users.TryGetValue(user, out var roles)
            ? new RecordActor(user, user, roles)
            : throw new StepFailedException($"user '{step.As}' is not a user of the scenario");

    private static byte[] Body(long? version, IReadOnlyDictionary<string, JsonElement>? values)
    {
        var body = new Dictionary<string, object?>();
        if (version is { } current)
        {
            body["version"] = current;
        }

        body["values"] = values ?? new Dictionary<string, JsonElement>();
        return JsonSerializer.SerializeToUtf8Bytes(body);
    }
}
