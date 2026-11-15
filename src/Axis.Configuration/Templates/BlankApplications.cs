using System.Text.Json.Nodes;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Templates;

/// <summary>
/// The blank application, which Studio's New application gives and a tenant created without an application gets,
/// see docs/reference/tenant-lifecycle.md#creating-a-tenant.
/// </summary>
public static class BlankApplications
{
    /// <summary>
    /// The files of a blank application named <paramref name="name"/>: the manifest, one site, an <c>accounts</c> identity
    /// provider, a <c>Home</c> page and an <c>en</c> text resource, each with a new id. It compiles with no diagnostic.
    /// </summary>
    public static IReadOnlyList<(string File, byte[] Bytes)> Files(string name)
    {
        var kebab = TemplateApplications.Kebab(name);
        return
        [
            ("application.json", TemplateApplications.Serialize(new JsonObject
            {
                ["id"] = TemplateApplications.NewId(),
                ["kind"] = "application",
                ["name"] = name,
                ["formatVersion"] = 1,
            })),
            ($"sites/{kebab}.json", TemplateApplications.Serialize(new JsonObject
            {
                ["id"] = TemplateApplications.NewId(),
                ["kind"] = "site",
                ["name"] = name,
                ["formatVersion"] = 1,
                ["path"] = kebab,
                ["title"] = new JsonObject { ["textKey"] = "site.title" },
                ["locales"] = new JsonObject
                {
                    ["default"] = "en",
                    ["fallback"] = "en",
                    ["available"] = new JsonArray("en"),
                },
                ["navigation"] = new JsonArray(new JsonObject
                {
                    ["page"] = "Home",
                    ["label"] = new JsonObject { ["textKey"] = "nav.home" },
                }),
            })),
            ("identity-providers/accounts.json", TemplateApplications.Serialize(new JsonObject
            {
                ["id"] = TemplateApplications.NewId(),
                ["kind"] = "identityProvider",
                ["name"] = "accounts",
                ["formatVersion"] = 1,
                ["type"] = "accounts",
            })),
            ("pages/home.json", TemplateApplications.Serialize(new JsonObject
            {
                ["id"] = TemplateApplications.NewId(),
                ["kind"] = "page",
                ["name"] = "Home",
                ["formatVersion"] = 1,
                ["title"] = new JsonObject { ["textKey"] = "pages.home.title" },
                ["widgets"] = new JsonArray(new JsonObject { ["type"] = "taskInbox", ["name"] = "taskInbox1" }),
            })),
            ("texts/en.json", TemplateApplications.Serialize(new JsonObject
            {
                ["id"] = TemplateApplications.NewId(),
                ["kind"] = "text",
                ["name"] = "TextsEn",
                ["formatVersion"] = 1,
                ["locale"] = "en",
                ["texts"] = new JsonObject
                {
                    ["site.title"] = name,
                    ["pages.home.title"] = "Home",
                    ["nav.home"] = "Home",
                },
            })),
        ];
    }

    /// <summary>
    /// The name of the blank application of tenant <paramref name="tenantId"/>: each hyphen is dropped and the letter after
    /// it is upper-cased, so <c>acme-corp</c> is <c>acmeCorp</c>. A name that is empty, does not start with a letter or has
    /// a reserved site path gets the prefix <c>app</c>, as <c>app1acme</c> and <c>appApi</c>.
    /// </summary>
    public static string NameFor(string tenantId)
    {
        var parts = tenantId.Split('-');
        var name = string.Concat(parts.Select((part, index) => index == 0 ? part : UpperFirst(part)));
        if (name.Length == 0 || !char.IsAsciiLetter(name[0]) || SitePaths.Reserved.Contains(TemplateApplications.Kebab(name)))
        {
            return "app" + UpperFirst(name);
        }

        return name;
    }

    private static string UpperFirst(string text) =>
        text.Length == 0 ? text : string.Concat(char.ToUpperInvariant(text[0]).ToString(), text.AsSpan(1));
}
