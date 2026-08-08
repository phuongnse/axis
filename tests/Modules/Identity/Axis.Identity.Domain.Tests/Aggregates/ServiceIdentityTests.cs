using Axis.Identity.Domain.Aggregates;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public sealed class ServiceIdentityTests
{
    [Fact]
    public void Create_WhenClientIdExceedsProjectionLimit_RejectsIdentity()
    {
        Action create = () => ServiceIdentity.Create(
            Guid.NewGuid(),
            new string('a', 101),
            DateTime.UtcNow);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RevokeKey_WhenMaterialIsReused_RejectsResurrection()
    {
        ServiceIdentity identity = ServiceIdentity.Create(Guid.NewGuid(), "svc-claims", DateTime.UtcNow);
        identity.AddKey("key-a", "thumb-a", "x", "y", identity.Revision, DateTime.UtcNow);
        ServiceIdentityKey key = identity.Keys.Single();
        identity.RevokeKey(key.Id, identity.Revision, DateTime.UtcNow);

        Action reuseKid = () => identity.AddKey("key-a", "thumb-b", "x2", "y2", identity.Revision, DateTime.UtcNow);
        Action reuseMaterial = () => identity.AddKey("key-b", "thumb-a", "x3", "y3", identity.Revision, DateTime.UtcNow);

        reuseKid.Should().Throw<InvalidOperationException>();
        reuseMaterial.Should().Throw<InvalidOperationException>();
        identity.Tombstones.Should().ContainSingle(x => x.Kid == "key-a" && x.Thumbprint == "thumb-a");
    }

    [Fact]
    public void RevokeIdentity_WhenAuthorityWasActive_RevokesGrant()
    {
        ServiceIdentity identity = ServiceIdentity.Create(Guid.NewGuid(), "svc-claims", DateTime.UtcNow);
        identity.AddKey("key-a", "thumb-a", "x", "y", identity.Revision, DateTime.UtcNow);
        Guid keyId = identity.Keys.Single().Id;
        identity.Revoke(identity.Revision, DateTime.UtcNow);

        identity.Status.Should().Be(ServiceIdentityStatus.Revoked);
        identity.WorkspaceGrantStatus.Should().Be(ServiceWorkspaceGrantStatus.Revoked);
        identity.HasActiveAuthority(keyId).Should().BeFalse();
        Action add = () => identity.AddKey("key-b", "thumb-b", "x", "y", identity.Revision, DateTime.UtcNow);
        add.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RevokeKey_WhenTerminalRequestIsRetriedWithOriginalRevision_IsCanonical()
    {
        ServiceIdentity identity = ServiceIdentity.Create(Guid.NewGuid(), "svc-key-retry", DateTime.UtcNow);
        ServiceIdentityKey key = identity.AddKey(
            "key-a",
            "thumb-a",
            "x",
            "y",
            identity.Revision,
            DateTime.UtcNow);
        int expectedRevision = identity.Revision;

        bool first = identity.RevokeKey(key.Id, expectedRevision, DateTime.UtcNow);
        bool retry = identity.RevokeKey(key.Id, expectedRevision, DateTime.UtcNow.AddMinutes(1));

        first.Should().BeTrue();
        retry.Should().BeFalse();
        identity.Revision.Should().Be(expectedRevision + 1);
        identity.Tombstones.Should().ContainSingle();
    }

    [Fact]
    public void RevokeIdentity_WhenTerminalRequestIsRetriedWithOriginalRevision_IsCanonical()
    {
        ServiceIdentity identity = ServiceIdentity.Create(Guid.NewGuid(), "svc-identity-retry", DateTime.UtcNow);

        bool first = identity.Revoke(identity.Revision, DateTime.UtcNow);
        bool retry = identity.Revoke(expectedRevision: 1, DateTime.UtcNow.AddMinutes(1));

        first.Should().BeTrue();
        retry.Should().BeFalse();
        identity.Revision.Should().Be(2);
        identity.WorkspaceGrantStatus.Should().Be(ServiceWorkspaceGrantStatus.Revoked);
    }
}
