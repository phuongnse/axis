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
    public void Create_produces_valid_organization()
    {
        var org = Organization.Create("Acme Corp", ValidSlug, ValidEmail);

        org.Name.Should().Be("Acme Corp");
        org.Slug.Should().Be(ValidSlug);
        org.OwnerEmail.Should().Be(ValidEmail);
        org.Status.Should().Be(OrganizationStatus.Active);
    }

    [Fact]
    public void Create_raises_OrganizationCreated_event()
    {
        var org = Organization.Create("Acme Corp", ValidSlug, ValidEmail);

        org.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrganizationCreated>();
    }

    [Fact]
    public void OrganizationCreated_event_contains_correct_data()
    {
        var org = Organization.Create("Acme Corp", ValidSlug, ValidEmail);

        var evt = org.DomainEvents.OfType<OrganizationCreated>().Single();
        evt.OrganizationId.Should().Be(org.Id);
        evt.Name.Should().Be("Acme Corp");
        evt.Slug.Should().Be("acme-corp");
        evt.OwnerEmail.Should().Be("admin@acme.com");
    }

    [Fact]
    public void Archive_changes_status_to_archived()
    {
        var org = Organization.Create("Acme Corp", ValidSlug, ValidEmail);
        org.ClearDomainEvents();

        org.Archive();

        org.Status.Should().Be(OrganizationStatus.Archived);
    }

    [Fact]
    public void Archiving_already_archived_org_throws()
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
    public void Create_with_empty_name_throws(string name)
    {
        var act = () => Organization.Create(name, ValidSlug, ValidEmail);

        act.Should().Throw<ArgumentException>();
    }
}
