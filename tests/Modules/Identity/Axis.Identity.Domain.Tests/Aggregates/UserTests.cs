using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class UserTests
{
    private static Email ValidEmail => Email.Create("alice@acme.com").Value;

    [Fact]
    public void User_WhenCreated_ProducesValidUser()
    {
        User user = User.Create("Alice", "Smith", ValidEmail);

        user.FirstName.Should().Be("Alice");
        user.LastName.Should().Be("Smith");
        user.Email.Should().Be(ValidEmail);
        user.Status.Should().Be(UserStatus.Active);
    }

    [Fact]
    public void User_WhenCreated_RaisesUserRegisteredEvent()
    {
        User user = User.Create("Alice", "Smith", ValidEmail);

        user.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserRegistered>();
    }

    [Fact]
    public void User_WhenCreated_UserRegisteredEventContainsCorrectData()
    {
        User user = User.Create("Alice", "Smith", ValidEmail);
        UserRegistered evt = user.DomainEvents.OfType<UserRegistered>().Single();
        evt.UserId.Should().Be(user.Id);
        evt.Email.Should().Be("alice@acme.com");
    }

    [Fact]
    public void User_WhenDeactivated_ChangesStatusToInactive()
    {
        User user = User.Create("Alice", "Smith", ValidEmail);
        user.ClearDomainEvents();

        user.Deactivate();

        user.Status.Should().Be(UserStatus.Inactive);
    }

    [Fact]
    public void User_WhenAlreadyInactive_DeactivateThrows()
    {
        User user = User.Create("Alice", "Smith", ValidEmail);
        user.Deactivate();

        Action act = () => user.Deactivate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already inactive*");
    }

    [Fact]
    public void User_WhenAccessed_FullNameCombinesFirstAndLastName()
    {
        User user = User.Create("Alice", "Smith", ValidEmail);

        user.FullName.Should().Be("Alice Smith");
    }
}
