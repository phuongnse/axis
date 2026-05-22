using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Commands.SubmitFormByToken;

/// <summary>US-087: Submit an assigned form via its unique access token (no login required).</summary>
public sealed record SubmitFormByTokenCommand(
    Guid AccessToken,
    IReadOnlyDictionary<string, object?> Data) : ICommand;
