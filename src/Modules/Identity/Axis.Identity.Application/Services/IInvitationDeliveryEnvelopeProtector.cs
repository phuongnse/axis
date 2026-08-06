namespace Axis.Identity.Application.Services;

public interface IInvitationDeliveryEnvelopeProtector
{
    string Protect(InvitationDeliveryMessage message);
    InvitationDeliveryMessage Unprotect(string protectedEnvelope);
}

public sealed record InvitationDeliveryMessage(
    Guid InvitationId,
    int Generation,
    string RecipientEmail,
    string RawToken,
    string OrganizationName,
    string WorkspaceName,
    string InviterName,
    string RequestedRole,
    DateTime ExpiresAt,
    string Language,
    string DeliveryCorrelation);
