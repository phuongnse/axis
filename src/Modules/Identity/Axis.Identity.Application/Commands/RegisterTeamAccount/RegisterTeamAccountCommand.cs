using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.RegisterTeamAccount;

public record RegisterTeamAccountCommand(
    string TeamAccountName,
    string TeamContactEmail,
    string AcceptedTermsVersion,
    string AcceptedPrivacyVersion,
    Guid? SubscriptionPlanId = null,
    string? IdempotencyKey = null) : ICommand;
