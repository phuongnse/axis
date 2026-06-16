using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;

namespace Axis.Identity.Application.Tests;

/// <summary>Exposes internal domain test factories to the Application test project.</summary>
internal static class InvitationTestHelper
{
    public static Invitation CreateExpired(Email email, Guid tenantId, Guid roleId, Guid invitedByUserId) =>
        Invitation.CreateExpired(email, tenantId, roleId, invitedByUserId);
}
