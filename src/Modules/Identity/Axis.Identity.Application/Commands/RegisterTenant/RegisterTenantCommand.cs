using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.RegisterTenant;

public record RegisterTenantCommand(
    string TenantName,
    string TenantContactEmail,
    string AcceptedTermsVersion,
    string AcceptedPrivacyVersion,
    Guid? SubscriptionPlanId = null,
    string? IdempotencyKey = null) : ICommand;
