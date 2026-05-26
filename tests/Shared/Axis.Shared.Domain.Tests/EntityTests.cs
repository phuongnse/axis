using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Shared.Domain.Tests;

public class EntityTests
{
    private class TestEntity(Guid id) : Entity<Guid>(id);

    [Fact]
    public void Entity_WhenIdsAreIdentical_AreEqual()
    {
        Guid id = Guid.NewGuid();
        TestEntity a = new TestEntity(id);
        TestEntity b = new TestEntity(id);

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Entity_WhenIdsDiffer_AreNotEqual()
    {
        TestEntity a = new TestEntity(Guid.NewGuid());
        TestEntity b = new TestEntity(Guid.NewGuid());

        a.Should().NotBe(b);
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void Entity_WhenComparedToNull_IsNotEqual()
    {
        TestEntity entity = new TestEntity(Guid.NewGuid());

        entity.Equals(null).Should().BeFalse();
        (entity == null!).Should().BeFalse();
    }

    [Fact]
    public void Entity_WhenSameId_HasConsistentHashCode()
    {
        Guid id = Guid.NewGuid();
        TestEntity a = new TestEntity(id);
        TestEntity b = new TestEntity(id);

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Entity_WhenComparedToDifferentType_IsNotEqual()
    {
        Guid id = Guid.NewGuid();
        TestEntity entity = new TestEntity(id);
        object other = new object();

        entity.Equals(other).Should().BeFalse();
    }
}
