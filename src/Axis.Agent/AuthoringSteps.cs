namespace Axis.Agent;

/// <summary>
/// The work of one state of an authoring request, see <c>docs/reference/authoring-requests.md#states</c>. The agent host claims only
/// a state that has a step, see <c>docs/reference/authoring-requests.md#durable-requests</c>.
/// </summary>
public interface IAuthoringStep
{
    /// <summary>The state of the request this step works on. It is one of <see cref="AuthoringRequestStore.StatesNeedingWork"/>.</summary>
    string State { get; }

    /// <summary>The kind the step's row in <c>axis.authoring_request_steps</c> gets.</summary>
    string Kind { get; }

    /// <summary>Runs the step and says the state the request moves to. The token is cancelled when the host stops or loses its claim.</summary>
    Task<AuthoringStepResult> RunAsync(AuthoringStepContext context, CancellationToken cancellationToken);
}

/// <summary>
/// What a finished step writes: the request's next state, the step row's outcome and input summary, the request's new <c>plan</c> and
/// the step's model messages as JSON and the results of a check step as JSON. A <see langword="null"/> plan leaves the request's plan as it is.
/// With <paramref name="DropDraft"/> the request's draft is cleared in the same commit, so the next working step makes a new one.
/// </summary>
public sealed record AuthoringStepResult(
    string NextState, string? Outcome = null, string? InputSummary = null, string? Plan = null, string? Messages = null, string? Checks = null, bool DropDraft = false);

/// <summary>
/// What a step knows about the request it works on, how it reports the tokens a model call used, how it gets the author's access token,
/// see <c>docs/reference/authoring-requests.md#acting-for-the-author</c>, and its client of the tenant's MCP server.
/// </summary>
public sealed class AuthoringStepContext
{
    private readonly Func<long, CancellationToken, Task> _addTokens;
    private readonly Func<CancellationToken, Task<string>> _accessToken;
    private readonly Func<Guid, CancellationToken, Task> _setDraft;
    private long _tokens;

    internal AuthoringStepContext(
        string tenantId,
        AuthoringRequestToWork request,
        Func<long, CancellationToken, Task> addTokens,
        Func<CancellationToken, Task<string>> accessToken,
        Func<Guid, CancellationToken, Task> setDraft,
        AuthorMcpClient mcp,
        long tokenBudget)
    {
        TenantId = tenantId;
        Request = request;
        _addTokens = addTokens;
        _accessToken = accessToken;
        _setDraft = setDraft;
        Mcp = mcp;
        TokenBudget = tokenBudget;
        _tokens = request.Tokens;
    }

    public string TenantId { get; }

    public AuthoringRequestToWork Request { get; }

    /// <summary>The tokens the request has used, this step included. Only the claiming host adds tokens, so the total needs no read.</summary>
    public long Tokens => Interlocked.Read(ref _tokens);

    /// <summary>The tokens the request may use, see <c>docs/reference/authoring-requests.md#budgets</c>.</summary>
    public long TokenBudget { get; }

    /// <summary>Adds the tokens to the step's row and to the request now, so they count even when the step is interrupted later.</summary>
    /// <exception cref="AuthoringClaimLostException">The host no longer holds the claim, or the request was dropped.</exception>
    public async Task AddTokensAsync(long tokens, CancellationToken cancellationToken)
    {
        await _addTokens(tokens, cancellationToken);
        Interlocked.Add(ref _tokens, tokens);
    }

    /// <summary>Stores the id of the request's draft on the request now, so a step that runs again finds it.</summary>
    /// <exception cref="AuthoringClaimLostException">The host no longer holds the claim, or the request was dropped.</exception>
    public Task SetDraftAsync(Guid draftId, CancellationToken cancellationToken) => _setDraft(draftId, cancellationToken);

    /// <summary>
    /// Refreshes the request's token at the tenant's realm and answers a current access token of the agent host for the author. The
    /// realm's new refresh token replaces the stored one.
    /// </summary>
    /// <exception cref="AuthoringTokenEndedException">The realm refused the token, or the request keeps none, or it no longer decrypts. The runner then stops the request as <c>failed</c>.</exception>
    /// <exception cref="AuthoringClaimLostException">The host no longer holds the claim, or the request was dropped.</exception>
    public Task<string> AccessTokenAsync(CancellationToken cancellationToken) => _accessToken(cancellationToken);

    /// <summary>
    /// The client of the tenant's MCP server, which acts as the author with the same access token, see <c>docs/reference/authoring-requests.md#the-agent-host</c>.
    /// A server that refuses the author ends the request like a realm that refuses the token.
    /// </summary>
    public AuthorMcpClient Mcp { get; }
}

/// <summary>How a check step waits for its scenario run, see <c>docs/reference/authoring-requests.md#budgets</c>.</summary>
/// <param name="PollInterval">How long the step waits before it asks for the run's status.</param>
/// <param name="MaxPolls">How many times the step asks. A run that has not ended by then stops the request as <c>failed</c>.</param>
public sealed record CheckingOptions(TimeSpan PollInterval, int MaxPolls)
{
    /// <summary>Every 2 seconds, 450 times: about 15 minutes, past the publisher's own limit of 10 minutes for a test run.</summary>
    public static CheckingOptions Default { get; } = new(TimeSpan.FromSeconds(2), 450);
}

/// <summary>How long the agent host holds a claim and how often it looks for work.</summary>
/// <param name="Lease">How long a claim lasts without a renewal. The host renews it every third of it.</param>
/// <param name="PollInterval">How often the work loop adds the served tenants to its turns.</param>
public sealed record AuthoringLoopOptions(TimeSpan Lease, TimeSpan PollInterval)
{
    /// <summary>The lease of the publisher, see <c>docs/reference/authoring-requests.md#durable-requests</c>.</summary>
    public static AuthoringLoopOptions Default { get; } = new(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1));
}

/// <summary>A write was refused because the host no longer holds the claim: its lease went to another host, or the request was dropped.</summary>
public sealed class AuthoringClaimLostException : Exception
{
    public AuthoringClaimLostException()
        : base("The agent host no longer holds the claim on the authoring request.")
    {
    }
}

/// <summary>The author's token for the agent host has ended, so the request cannot act for the author any more.</summary>
public sealed class AuthoringTokenEndedException : Exception
{
    public AuthoringTokenEndedException()
        : base("The author's token for the agent host has ended.")
    {
    }
}

/// <summary>A model call would pass the request's token budget, so the loop made no call. The runner stops the request as <c>failed</c>.</summary>
public sealed class AuthoringOverBudgetException : Exception
{
    public AuthoringOverBudgetException()
        : base("The authoring request would pass its token budget.")
    {
    }
}

/// <summary>The steps the agent host runs, by the state they work on. Building it refuses a set the host cannot run.</summary>
public sealed class AuthoringSteps
{
    private readonly Dictionary<string, IAuthoringStep> _byState = new(StringComparer.Ordinal);

    /// <exception cref="InvalidOperationException">Two steps work on one state, or a step works on a state that needs no work.</exception>
    public AuthoringSteps(IEnumerable<IAuthoringStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        foreach (var step in steps)
        {
            if (!AuthoringRequestStore.StatesNeedingWork.Contains(step.State, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"The authoring step '{step.Kind}' is registered for the state '{step.State}', which needs no work.");
            }

            if (!_byState.TryAdd(step.State, step))
            {
                throw new InvalidOperationException($"Two authoring steps are registered for the state '{step.State}'.");
            }
        }
    }

    /// <summary>The states that have a step.</summary>
    public IReadOnlyList<string> States => [.. _byState.Keys];

    public bool Any => _byState.Count > 0;

    public bool Has(string state) => _byState.ContainsKey(state);

    public IAuthoringStep? Find(string state) => _byState.GetValueOrDefault(state);
}
