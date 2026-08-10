using System.Text.Json;
using Axis.Identity.Application.Services;
using Microsoft.AspNetCore.DataProtection;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class DataProtectionInvitationDeliveryEnvelopeProtector
    : IInvitationDeliveryEnvelopeProtector
{
    private readonly IDataProtector protector;

    public DataProtectionInvitationDeliveryEnvelopeProtector(IDataProtectionProvider provider)
    {
        protector = provider.CreateProtector(
            "Axis.Identity",
            "WorkspaceInvitationDeliveryEnvelope",
            "v1");
    }

    public string Protect(InvitationDeliveryMessage message) =>
        protector.Protect(JsonSerializer.Serialize(message));

    public InvitationDeliveryMessage Unprotect(string protectedEnvelope)
    {
        if (string.IsNullOrWhiteSpace(protectedEnvelope))
            throw new ArgumentException("Protected invitation envelope is required.", nameof(protectedEnvelope));

        return JsonSerializer.Deserialize<InvitationDeliveryMessage>(
                protector.Unprotect(protectedEnvelope))
            ?? throw new InvalidOperationException("Invitation delivery envelope is invalid.");
    }
}
