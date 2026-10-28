using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Server.Tests;

public sealed class DevelopmentStudioFoldersTests
{
    // Port 1 on loopback refuses connections immediately. The authoring API reads no database.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void The_development_settings_list_every_sample_application_in_Studio()
    {
        var listed = DevelopmentFolders().Order(StringComparer.Ordinal).ToList();
        var onDisk = Directory.EnumerateDirectories(Path.Combine(RepositoryRoot(), "samples", "apps"))
            .Select(path => path.TrimEnd(Path.DirectorySeparatorChar))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(onDisk, listed);
    }

    [Fact]
    public async Task Author_lists_the_purchase_request_leave_request_service_desk_and_asset_management_applications_under_the_development_settings()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("author");

        var response = await client.GetAsync(new Uri("/api/studio/apps", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var names = body.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(["AssetManagement", "LeaveRequests", "PurchaseRequests", "ServiceDesk"], names);
    }

    [Fact]
    public async Task A_user_without_the_author_role_gets_403_from_the_list_of_applications_under_the_development_settings()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("requester");

        var response = await client.GetAsync(new Uri("/api/studio/apps", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Author_reads_a_service_desk_resource_and_a_user_without_the_author_role_gets_403_under_the_development_settings()
    {
        var dashboard = Path.Combine(RepositoryRoot(), "samples", "apps", "service-desk", "pages", "dashboard.json");
        var id = JsonNode.Parse(File.ReadAllBytes(dashboard))!["id"]!.GetValue<string>();
        var url = new Uri($"/api/studio/apps/ServiceDesk/resources/{id}", UriKind.Relative);
        await using var factory = CreateFactory();

        using var author = factory.CreateClient().SignedInAs("author");
        Assert.Equal(HttpStatusCode.OK, (await author.GetAsync(url, CancellationToken)).StatusCode);

        using var employee = factory.CreateClient().SignedInAs("requester");
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync(url, CancellationToken)).StatusCode);
    }

    // The Studio:Folders of appsettings.Development.json, resolved from the server project folder as the server does.
    private static List<string> DevelopmentFolders()
    {
        var serverFolder = Path.Combine(RepositoryRoot(), "src", "Axis.Server");
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(serverFolder, "appsettings.Development.json")));
        return settings.RootElement.GetProperty("Studio").GetProperty("Folders").EnumerateArray()
            .Select(entry => Path.GetFullPath(entry.GetString()!, serverFolder).TrimEnd(Path.DirectorySeparatorChar))
            .ToList();
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var folders = DevelopmentFolders();
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform();
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", UnreachableDatabase);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            for (var i = 0; i < folders.Count; i++)
            {
                builder.UseSetting($"Studio:Folders:{i}", folders[i]);
            }

            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseTestOidc("default");
            builder.UseTestAuthentication(
                new TestIdentity("author", "Author User", "author"),
                new TestIdentity("requester", "Requester User", "employee"));
        });
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Axis.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Axis.slnx was not found above the test output folder.");
    }
}
