using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Configuration.Templates;

namespace Axis.Configuration.Tests;

public sealed partial class BlankApplicationsTests
{
    [Theory]
    [InlineData("acme", "acme")]
    [InlineData("acme-corp", "acmeCorp")]
    [InlineData("a--b", "aB")]
    [InlineData("acme2", "acme2")]
    [InlineData("1acme", "app1acme")]
    [InlineData("123", "app123")]
    [InlineData("-acme", "Acme")]
    [InlineData("api", "appApi")]
    [InlineData("studio", "appStudio")]
    public void The_name_of_a_tenant_id_is_a_valid_application_name_that_the_compiler_accepts(string tenantId, string expected)
    {
        var name = BlankApplications.NameFor(tenantId);

        Assert.Equal(expected, name);
        Assert.Matches(ApplicationName(), name);
    }

    [Fact]
    public void A_tenant_id_of_the_longest_length_makes_a_name_within_the_60_character_limit()
    {
        var name = BlankApplications.NameFor(string.Join('-', Enumerable.Repeat("a", 25)));

        Assert.Matches(ApplicationName(), name);
        Assert.InRange(name.Length, 1, 60);
    }

    [Fact]
    public void The_files_name_the_application_and_its_site_and_get_new_ids_each_time()
    {
        var first = BlankApplications.Files("acmeCorp");
        var second = BlankApplications.Files("acmeCorp");

        Assert.Equal(
            ["application.json", "sites/acme-corp.json", "identity-providers/accounts.json", "pages/home.json", "texts/en.json"],
            first.Select(file => file.File));
        Assert.Equal("acmeCorp", Property(first, "application.json", "name"));
        Assert.Equal("acme-corp", Property(first, "sites/acme-corp.json", "path"));
        Assert.NotEqual(Property(first, "application.json", "id"), Property(second, "application.json", "id"));
    }

    private static string? Property(IReadOnlyList<(string File, byte[] Bytes)> files, string file, string name) =>
        JsonDocument.Parse(files.Single(candidate => candidate.File == file).Bytes).RootElement.GetProperty(name).GetString();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ApplicationName();
}
