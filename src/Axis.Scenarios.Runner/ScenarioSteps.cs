using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Processes.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Scenarios.Runner;

/// <summary>
/// Runs the steps of one scenario over its tenant database connection. It keeps the records that
/// <c>createRecord</c> steps saved under an alias. A step that fails throws <see cref="StepFailedException"/>.
/// <c>startProcess</c> and <c>completeTask</c> apply the checks of the process start and task API
/// without the HTTP layer. The runner runs the process steps that become due after each action.
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
            case ScenarioStepDefinition.StartProcess:
                await StartProcessAsync(step, cancellationToken);
                break;
            case ScenarioStepDefinition.CompleteTask:
                await CompleteTaskAsync(step, cancellationToken);
                break;
            case ScenarioStepDefinition.ExpectRecord:
                await ExpectRecordAsync(step, cancellationToken);
                break;
            case ScenarioStepDefinition.ExpectInstance:
                await ExpectInstanceAsync(step, cancellationToken);
                break;
            case ScenarioStepDefinition.ExpectTask:
                await ExpectTaskAsync(step, cancellationToken);
                break;
            default:
                throw new StepFailedException($"step type '{step.Type}' is not supported yet");
        }
    }

    private async Task CreateAsync(ScenarioStepDefinition step, CancellationToken cancellationToken)
    {
        var entity = Entity(step.Entity);
        var body = Body(version: null, step.Values);
        var result = await RecordCommand.CreateAsync(
            connection, ScenarioRunner.TenantId, model, releaseId, entity, Actor(step), _ => Task.FromResult<byte[]?>(body), null, loggerFactory, cancellationToken);
        Check(step, Action(result, RecordCommandOutcome.Written));
        if (step.Save is { } alias)
        {
            _saved[alias] = (entity, result.Record!.Id);
        }
    }

    private async Task UpdateAsync(ScenarioStepDefinition step, CancellationToken cancellationToken)
    {
        var (entity, id) = Saved(step.Record);
        var stored = await RecordQueries.GetAsync(connection, model, entity, id, cancellationToken);

        // A record that no longer exists still goes to the record command, which answers NotFound.
        var body = Body(stored?.Version ?? 1, step.Values);
        var result = await RecordCommand.UpdateAsync(
            connection, ScenarioRunner.TenantId, model, releaseId, entity, id, Actor(step), _ => Task.FromResult<byte[]?>(body), null, loggerFactory, cancellationToken);
        Check(step, Action(result, RecordCommandOutcome.Written));
    }

    private async Task DeleteAsync(ScenarioStepDefinition step, CancellationToken cancellationToken)
    {
        var (entity, id) = Saved(step.Record);
        var result = await RecordCommand.DeleteAsync(
            connection, ScenarioRunner.TenantId, model, releaseId, entity, id, Actor(step), loggerFactory, cancellationToken);
        Check(step, Action(result, RecordCommandOutcome.Deleted));
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

    private async Task StartProcessAsync(ScenarioStepDefinition step, CancellationToken cancellationToken)
    {
        var process = Process(step.Process);
        var subject = process.Entity
            ?? throw new StepFailedException($"process '{process.Name}' has no subject");
        var entity = model.TryGetEntity(subject.Name, out var found)
            ? found
            : throw new StepFailedException($"entity '{subject.Name}' of process '{process.Name}' does not exist");
        var (_, id) = Saved(step.Record);
        var (user, roles) = User(step);
        if (!PolicyEvaluator.AllowsStart(model, roles, process))
        {
            Check(step, Failed("denied", "Denied"));
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // A read filter on a field that is being recomputed decides nothing, so it comes before the record is looked up.
        var read = PolicyEvaluator.Access(model, roles, entity, PolicyAction.Read);
        if (read.Filters is { } pendingFilters
            && await RecordCommand.ReadsPendingAsync(connection, transaction, model, entity, pendingFilters, loggerFactory, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            Check(step, Failed("conflict", "PendingFilter"));
            return;
        }

        // A record the user may not read does not exist for them.
        if (!await RecordCommands.LockAsync(connection, entity, id, cancellationToken)
            || !read.Granted
            || (read.Filters is { } filters
                && await RecordCommand.MatchAsync(connection, transaction, model, entity, id, filters, user, loggerFactory, cancellationToken)
                    is not { Found: true, Matched: true })
            || await RecordQueries.GetAsync(connection, model, entity, id, cancellationToken) is not { } record)
        {
            await transaction.RollbackAsync(cancellationToken);
            Check(step, Failed("notFound", "NotFound"));
            return;
        }

        var started = await ProcessStarter.StartAsync(
            connection, transaction, ScenarioRunner.TenantId, model, releaseId, process, entity, id, record, Guid.CreateVersion7(), user, user, null, cancellationToken);
        if (started.Outcome == ProcessStartOutcome.Started)
        {
            await transaction.CommitAsync(cancellationToken);
            Check(step, new ActionResult(true, null, null, "Started"));
            return;
        }

        await transaction.RollbackAsync(cancellationToken);
        Check(
            step,
            started.Outcome == ProcessStartOutcome.AlreadyRunning
                ? Failed("conflict", "AlreadyRunning")
                : Failed(null, $"ConditionFailed ({started.TextKey})"));
    }

    private async Task CompleteTaskAsync(ScenarioStepDefinition step, CancellationToken cancellationToken)
    {
        var (entity, id) = Saved(step.Record);
        var (user, roles) = User(step);
        if (await NewestTaskAsync(entity, id, step.Step, cancellationToken) is not { } task)
        {
            Check(step, Failed("notFound", "NotFound"));
            return;
        }

        var body = new Dictionary<string, object?> { ["outcome"] = step.Outcome };
        if (step.Values is { } values)
        {
            body["values"] = values;
            body["version"] = (await RecordQueries.GetAsync(connection, model, entity, id, cancellationToken))?.Version ?? 1;
        }

        var utf8Body = JsonSerializer.SerializeToUtf8Bytes(body);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var locked = await TaskQueries.LockAsync(transaction, task.Id, cancellationToken);
        var result = await TaskCompletionCommand.CompleteLockedAsync(
            connection,
            transaction,
            ScenarioRunner.TenantId,
            model,
            releaseId,
            locked,
            new RecordActor(user, user, PolicyEvaluator.HeldRoles(model, roles)),
            (_, _) => Task.FromResult(model),
            _ => Task.FromResult<byte[]?>(utf8Body),
            loggerFactory,
            cancellationToken);
        if (result.Outcome == TaskCompletionOutcome.Completed)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        Check(step, Action(result));
    }

    private async Task ExpectInstanceAsync(ScenarioStepDefinition step, CancellationToken cancellationToken)
    {
        var process = Process(step.Process);
        var (_, id) = Saved(step.Record);
        await using var context = Processes();
        var instance = await context.Instances.AsNoTracking()
            .Where(candidate => candidate.ProcessId == process.Id && candidate.SubjectId == id)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new StepFailedException($"no instance of process '{process.Name}' exists for record '{step.Record}'");
        if (!string.Equals(instance.State, step.State, StringComparison.Ordinal))
        {
            throw new StepFailedException(
                $"instance of process '{process.Name}' expected state {step.State} but was {instance.State} at step '{instance.Step}'");
        }

        if (step.Step is { } expectedStep && !string.Equals(instance.Step, expectedStep, StringComparison.OrdinalIgnoreCase))
        {
            throw new StepFailedException(
                $"instance of process '{process.Name}' expected step '{expectedStep}' but was '{instance.Step}'");
        }
    }

    private async Task ExpectTaskAsync(ScenarioStepDefinition step, CancellationToken cancellationToken)
    {
        var (entity, id) = Saved(step.Record);
        var task = await NewestTaskAsync(entity, id, step.Step, cancellationToken)
            ?? throw new StepFailedException($"no task of step '{step.Step}' exists for record '{step.Record}'");
        if (step.State is { } state && !string.Equals(task.State, state, StringComparison.Ordinal))
        {
            throw new StepFailedException($"task '{task.Step}' expected state {state} but was {task.State}");
        }

        if (step.Assignee is { } assignee)
        {
            var (kind, value) = assignee.User is { } user ? (ProcessTasks.UserAssignee, user) : (ProcessTasks.RoleAssignee, assignee.Role);
            if (!string.Equals(task.AssigneeKind, kind, StringComparison.Ordinal) || !string.Equals(task.Assignee, value, StringComparison.Ordinal))
            {
                throw new StepFailedException(
                    $"task '{task.Step}' expected assignee {kind} '{value}' but was {task.AssigneeKind} '{task.Assignee}'");
            }
        }
    }

    // The task steps of the processes over the entity that match the name, ignoring letter case, as
    // the process declares them. A step with no task yet has none.
    private async Task<ProcessTaskRow?> NewestTaskAsync(EntityModel entity, Guid id, string? stepName, CancellationToken cancellationToken)
    {
        var declared = model.Processes
            .Where(process => process.Entity?.Id == entity.Id)
            .SelectMany(process => process.Steps.OfType<TaskStepModel>()
                .Where(task => string.Equals(task.Name, stepName, StringComparison.OrdinalIgnoreCase))
                .Select(task => (ProcessId: process.Id, task.Name)))
            .ToList();
        if (declared.Count == 0)
        {
            throw new StepFailedException($"step '{stepName}' is not a task step of a process of {entity.Name}");
        }

        var processIds = declared.Select(task => task.ProcessId).ToList();
        var names = declared.Select(task => task.Name).Distinct(StringComparer.Ordinal).ToList();
        await using var context = Processes();
        return await context.Tasks.AsNoTracking()
            .Where(task => task.SubjectId == id && processIds.Contains(task.ProcessId) && names.Contains(task.Step))
            .OrderByDescending(task => task.CreatedAt)
            .ThenByDescending(task => task.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private ProcessesDbContext Processes() =>
        new(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connection).Options);

    private ProcessModel Process(string? name) =>
        name is not null && model.TryGetProcess(name, out var process)
            ? process
            : throw new StepFailedException($"process '{name}' does not exist");

    /// <summary>
    /// What an action did. <see cref="Kind"/> is the answer an <c>expect</c> can match: <c>denied</c>,
    /// <c>notFound</c>, <c>conflict</c> or <c>invalid</c>, and null for success and for any other failure.
    /// <see cref="Actual"/> says the outcome in words for the step's message.
    /// </summary>
    private sealed record ActionResult(bool Succeeded, string? Kind, IDictionary<string, string[]>? Errors, string Actual);

    private static ActionResult Failed(string? kind, string actual) => new(false, kind, null, actual);

    private static ActionResult Action(RecordCommandResult result, RecordCommandOutcome success)
    {
        var kind = result.Outcome switch
        {
            RecordCommandOutcome.Denied or RecordCommandOutcome.OutsideFilters or RecordCommandOutcome.UnwritableFields => "denied",
            RecordCommandOutcome.NotFound => "notFound",
            RecordCommandOutcome.Conflict or RecordCommandOutcome.StaleVersion or RecordCommandOutcome.PendingFilter => "conflict",
            RecordCommandOutcome.Invalid => "invalid",
            _ => null,
        };
        var actual = result.Outcome.ToString();
        if (result.Conflict is { } conflict)
        {
            actual += $" ({conflict})";
        }

        return new ActionResult(result.Outcome == success, kind, result.Errors, actual + ErrorText(result.Errors));
    }

    private static ActionResult Action(TaskCompletionResult result)
    {
        var write = result.Write?.Outcome;
        var kind = result.Outcome switch
        {
            TaskCompletionOutcome.NotFound => "notFound",
            TaskCompletionOutcome.Denied => "denied",
            TaskCompletionOutcome.AlreadyCompleted or TaskCompletionOutcome.StaleVersion => "conflict",
            TaskCompletionOutcome.Invalid => "invalid",
            TaskCompletionOutcome.WriteFailed => write switch
            {
                RecordWriteOutcome.NotFound => "notFound",
                RecordWriteOutcome.UniqueViolation or RecordWriteOutcome.SchemaConflict => "conflict",
                RecordWriteOutcome.MissingReference or RecordWriteOutcome.Invalid => "invalid",
                _ => null,
            },
            _ => null,
        };
        var errors = result.Errors ?? result.Write?.Errors;
        var actual = write is { } outcome ? $"{result.Outcome} ({outcome})" : result.Outcome.ToString();
        return new ActionResult(result.Outcome == TaskCompletionOutcome.Completed, kind, errors, actual + ErrorText(errors));
    }

    private static string ErrorText(IDictionary<string, string[]>? errors) =>
        errors is { Count: > 0 }
            ? ": " + string.Join("; ", errors.Select(error => $"{error.Key} {string.Join(", ", error.Value)}"))
            : "";

    private static void Check(ScenarioStepDefinition step, ActionResult result)
    {
        if (step.Expect is not { } expect)
        {
            if (!result.Succeeded)
            {
                throw new StepFailedException($"expected success but the outcome was {result.Actual}");
            }

            return;
        }

        if (!Meets(expect, result))
        {
            throw new StepFailedException($"expected {Describe(expect)} but the outcome was {result.Actual}");
        }
    }

    private static bool Meets(JsonElement expect, ActionResult result) =>
        expect.ValueKind == JsonValueKind.String
            ? result.Kind is not null && result.Kind == expect.GetString()
            : result.Kind == "invalid"
                && result.Errors?.ContainsKey("/values/" + expect.GetProperty("invalid").GetString()) == true;

    private static string Describe(JsonElement expect) =>
        expect.ValueKind == JsonValueKind.String
            ? expect.GetString()!
            : $"invalid at '/values/{expect.GetProperty("invalid").GetString()}'";

    private EntityModel Entity(string? name) =>
        name is not null && model.TryGetEntity(name, out var entity)
            ? entity
            : throw new StepFailedException($"entity '{name}' does not exist");

    private (EntityModel Entity, Guid Id) Saved(string? alias) =>
        alias is not null && _saved.TryGetValue(alias, out var saved)
            ? saved
            : throw new StepFailedException($"no record is saved as '{alias}'");

    private RecordActor Actor(ScenarioStepDefinition step)
    {
        var (user, roles) = User(step);
        return new RecordActor(user, user, roles);
    }

    private (string Name, IReadOnlyList<string> Roles) User(ScenarioStepDefinition step) =>
        step.As is { } user && scenario.Users.TryGetValue(user, out var roles)
            ? (user, roles)
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
