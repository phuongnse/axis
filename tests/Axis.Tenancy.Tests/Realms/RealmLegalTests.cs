using Axis.Tenancy.Realms;

namespace Axis.Tenancy.Tests.Realms;

public sealed class RealmLegalTests
{
    private static readonly TenantLegal _legal = new("https://t.test/privacy", "https://t.test/terms", "1", new Dictionary<string, SiteLegal>());

    [Fact]
    public void A_tenant_with_no_host_has_no_licences_link_and_keeps_its_legal_settings()
    {
        var legal = RealmLegal.Of(_legal, [], hostSites: null);

        Assert.Null(legal.LicencesUrl);
        Assert.Same(_legal, legal.Legal);
        Assert.Null(RealmLegal.None.Legal);
        Assert.Null(RealmLegal.None.LicencesUrl);
    }

    [Fact]
    public void The_licences_link_uses_the_first_host_in_ordinal_order_that_belongs_to_no_site()
    {
        var hostSites = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["a.test"] = "sales" };

        var legal = RealmLegal.Of(_legal, ["c.test", "b.test", "a.test"], hostSites);

        Assert.Equal("https://b.test/licences", legal.LicencesUrl);
    }

    [Fact]
    public void The_licences_link_uses_the_first_host_when_no_host_has_a_site()
    {
        Assert.Equal("https://a.test/licences", RealmLegal.Of(null, ["b.test", "a.test"], hostSites: null).LicencesUrl);
    }

    [Fact]
    public void The_licences_link_uses_the_first_host_when_every_host_belongs_to_a_site()
    {
        var hostSites = new Dictionary<string, string> { ["b.test"] = "support", ["a.test"] = "sales" };

        var legal = RealmLegal.Of(_legal, ["b.test", "a.test"], hostSites);

        Assert.Equal("https://a.test/licences", legal.LicencesUrl);
    }

    [Fact]
    public void Hosts_are_trimmed_and_lower_cased_and_a_site_host_matches_ignoring_letter_case()
    {
        var hostSites = new Dictionary<string, string> { ["A.Test"] = "sales" };

        var legal = RealmLegal.Of(_legal, [" B.Test ", "", "A.TEST"], hostSites);

        Assert.Equal("https://b.test/licences", legal.LicencesUrl);
    }
}
