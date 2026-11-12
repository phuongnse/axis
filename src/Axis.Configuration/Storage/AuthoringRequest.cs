namespace Axis.Configuration.Storage;

/// <summary>
/// A change that an author describes in words, stored by the server and worked on by the agent host. The server
/// inserts a row in the <c>planning</c> state and changes its state when the author approves, changes the plan or
/// drops it. The publisher moves a <c>ready</c> row to <c>accepted</c> when the accept of its draft succeeds. The agent host
/// changes everything else.
/// </summary>
public sealed class AuthoringRequest
{
    public required Guid Id { get; init; }

    /// <summary>The <c>id</c> of the application manifest.</summary>
    public required Guid ApplicationId { get; init; }

    /// <summary>The user id of the author who sent the request.</summary>
    public required string Author { get; init; }

    public required string Text { get; init; }

    /// <summary>The model binding the request uses, or null until one is chosen.</summary>
    public string? Binding { get; init; }

    /// <summary><c>planning</c>, <c>awaitingApproval</c>, <c>working</c>, <c>checking</c>, <c>reviewing</c>, <c>fixing</c>, <c>ready</c>, <c>accepted</c>, <c>dropped</c> or <c>failed</c>.</summary>
    public required string State { get; init; }

    public Guid? DraftId { get; init; }

    public string? Plan { get; init; }

    public int Round { get; init; }

    public long Tokens { get; init; }

    public bool AutoAccept { get; init; }

    public DateTimeOffset SentAt { get; init; }

    public DateTimeOffset? ApprovedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public string? ClaimedBy { get; init; }

    public DateTimeOffset? ClaimedUntil { get; init; }

    /// <summary>The agent host's refresh token for the author, encrypted with the tenant data key, or null when none is stored.</summary>
    public byte[]? RefreshToken { get; init; }
}
