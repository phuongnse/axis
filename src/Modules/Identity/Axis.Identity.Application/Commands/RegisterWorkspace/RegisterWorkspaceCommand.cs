using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.RegisterWorkspace;

public record RegisterWorkspaceCommand(
    string WorkspaceName,
    string WorkspaceContactEmail,
    string AcceptedTermsVersion,
    string AcceptedPrivacyVersion,
    Guid? SubscriptionPlanId = null,
    string? IdempotencyKey = null) : ICommand;
