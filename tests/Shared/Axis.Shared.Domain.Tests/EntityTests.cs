using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Shared.Domain.Tests;

public class EntityTests
{
    private class TestEntity(Guid id) : Entity<Guid>(id);

    [Fact]
    public void Entity_WhenIdsAreIdentical_AreEqual()
    {
        var id = Guid.NewGuid();
        var a = new TestEntity(id);
        var b = new TestEntity(id);

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Entity_WhenIdsDiffer_AreNotEqual()
    {
        var a = new TestEntity(Guid.NewGuid());
        var b = new TestEntity(Guid.NewGuid());

        a.Should().NotBe(b);
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void Entity_WhenComparedToNull_IsNotEqual()
    {
        var entity = new TestEntity(Guid.NewGuid());

        entity.Equals(null).Should().BeFalse();
        (entity == null!).Should().BeFalse();
    }

    [Fact]
    public void Entity_WhenSameId_HasConsistentHashCode()
    {
        var id = Guid.NewGuid();
        var a = new TestEntity(id);
        var b = new TestEntity(id);

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Entity_WhenComparedToDifferentType_IsNotEqual()
    {
        var id = Guid.NewGuid();
        var entity = new TestEntity(id);
        var other = new object();

        entity.Equals(other).Should().BeFalse();
    }
}
