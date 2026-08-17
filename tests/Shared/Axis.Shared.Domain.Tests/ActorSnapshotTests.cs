using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Shared.Domain.Tests;

public sealed class ActorSnapshotTests
{
    [Fact]
    public void System_WhenCreated_ReturnsCanonicalActor()
    {
        ActorSnapshot actor = ActorSnapshot.System();

        actor.IsValid.Should().BeTrue();
        actor.Kind.Should().Be(ActorKind.System);
        actor.SubjectId.Should().BeNull();
        actor.DisplayName.Should().Be(ActorSnapshot.SystemDisplayName);
    }

    [Fact]
    public void IsValid_WhenSystemDisplayNameIsNotCanonical_ReturnsFalse()
    {
        ActorSnapshot actor = new(ActorKind.System, null, "Runtime");

        actor.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Create_WhenSystemDisplayNameIsNotCanonical_Throws()
    {
        Action action = () => ActorSnapshot.Create(ActorKind.System, null, "Runtime");

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Anonymous_WhenCreated_ReturnsCanonicalActor()
    {
        ActorSnapshot actor = ActorSnapshot.Anonymous();

        actor.IsValid.Should().BeTrue();
        actor.Kind.Should().Be(ActorKind.Anonymous);
        actor.SubjectId.Should().BeNull();
        actor.DisplayName.Should().Be(ActorSnapshot.AnonymousDisplayName);
    }

    [Fact]
    public void Create_WhenAnonymousDisplayNameIsNotCanonical_Throws()
    {
        Action action = () => ActorSnapshot.Create(ActorKind.Anonymous, null, "Guest");

        action.Should().Throw<ArgumentException>();
    }
}
