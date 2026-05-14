using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class OrganizationTests
{
    private static OrganizationSlug ValidSlug => OrganizationSlug.Create("acme-corp").Value;
    private static Email ValidEmail => Email.Create("admin@acme.com").Value;

    [Fact]
    public void Organization_WhenCreated_ProducesValidOrganization()
    {
        var org = Organization.Create("Acme Corp", ValidSlug, ValidEmail);

        org.Name.Should().Be("Acme Corp");
        org.Slug.Should().Be(ValidSlug);
        org.OwnerEmail.Should().Be(ValidEmail);
        org.Status.Should().Be(OrganizationStatus.Active);
    }

    [Fact]
    public void Organization_WhenCreated_RaisesOrganizationCreatedEvent()
    {
        var org = Organization.Create("Acme Corp", ValidSlug, ValidEmail);

        org.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrganizationCreated>();
    }

    [Fact]
    public void Organization_WhenCreated_OrganizationCreatedEventContainsCorrectData()
    {
        var org = Organization.Create("Acme Corp", ValidSlug, ValidEmail);

        var evt = org.DomainEvents.OfType<OrganizationCreated>().Single();
        evt.OrganizationId.Should().Be(org.Id);
        evt.Name.Should().Be("Acme Corp");
        evt.Slug.Should().Be("acme-corp");
        evt.OwnerEmail.Should().Be("admin@acme.com");
    }

    [Fact]
    public void Organization_WhenArchived_ChangesStatusToArchived()
    {
        var org = Organization.Create("Acme Corp", ValidSlug, ValidEmail);
        org.ClearDomainEvents();

        org.Archive();

        org.Status.Should().Be(OrganizationStatus.Archived);
    }

    [Fact]
    public void Organization_WhenAlreadyArchived_ArchiveThrows()
    {
        var org = Organization.Create("Acme Corp", ValidSlug, ValidEmail);
        org.Archive();

        var act = () => org.Archive();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already archived*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Organization_WhenCreatedWithEmptyName_Throws(string name)
    {
        var act = () => Organization.Create(name, ValidSlug, ValidEmail);

        act.Should().Throw<ArgumentException>();
    }
}
