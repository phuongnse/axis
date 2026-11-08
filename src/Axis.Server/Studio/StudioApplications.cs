using System.Text.Json.Nodes;
using Axis.Configuration.Templates;
using Axis.Studio;

namespace Axis.Server.Studio;

/// <summary>Writes the files of a new application. A test replaces it to make a write fail.</summary>
internal interface IStudioFileWriter
{
    Task WriteAsync(string path, byte[] bytes, CancellationToken cancellationToken);
}

internal sealed class StudioFileWriter : IStudioFileWriter
{
    public Task WriteAsync(string path, byte[] bytes, CancellationToken cancellationToken) =>
        File.WriteAllBytesAsync(path, bytes, cancellationToken);
}

internal enum StudioCreateOutcome
{
    Created,
    Exists,
    Unavailable,
}

/// <summary>
/// Creates a new application folder in <c>Studio:ApplicationsPath</c>, blank or as a copy of an installed template.
/// The files are written to a hidden temporary folder under the write lock, and the folder is then renamed to the
/// kebab-case name, so a reader never sees half an application and a failed create leaves nothing behind.
/// </summary>
internal static class StudioApplications
{
    /// <summary>
    /// The files of a blank application named <paramref name="name"/>: the manifest, one site, an <c>accounts</c> identity
    /// provider, a <c>Home</c> page and an <c>en</c> text resource, each with a new id. It compiles with no diagnostic.
    /// </summary>
    public static IReadOnlyList<(string File, byte[] Bytes)> Blank(string name)
    {
        var kebab = StudioFileNames.Kebab(name);
        return
        [
            ("application.json", StudioWrites.Serialize(new JsonObject
            {
                ["id"] = TemplateApplications.NewId(),
                ["kind"] = "application",
                ["name"] = name,
                ["formatVersion"] = 1,
            })),
            ($"sites/{kebab}.json", StudioWrites.Serialize(new JsonObject
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
            ("identity-providers/accounts.json", StudioWrites.Serialize(new JsonObject
            {
                ["id"] = TemplateApplications.NewId(),
                ["kind"] = "identityProvider",
                ["name"] = "accounts",
                ["formatVersion"] = 1,
                ["type"] = "accounts",
            })),
            ("pages/home.json", StudioWrites.Serialize(new JsonObject
            {
                ["id"] = TemplateApplications.NewId(),
                ["kind"] = "page",
                ["name"] = "Home",
                ["formatVersion"] = 1,
                ["title"] = new JsonObject { ["textKey"] = "pages.home.title" },
                ["widgets"] = new JsonArray(new JsonObject { ["type"] = "taskInbox", ["name"] = "taskInbox1" }),
            })),
            ("texts/en.json", StudioWrites.Serialize(new JsonObject
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
    /// Creates the folder of <paramref name="name"/> from <paramref name="files"/>. It answers
    /// <see cref="StudioCreateOutcome.Exists"/> and writes nothing when an application already has the name, ignoring
    /// letter case, or the target folder exists. A failed write deletes the temporary folder and the exception goes on.
    /// </summary>
    public static async Task<StudioCreateOutcome> CreateAsync(
        IStudioWriteLock writeLock,
        IStudioFileWriter writer,
        StudioOptions options,
        string name,
        IReadOnlyList<(string File, byte[] Bytes)> files,
        CancellationToken cancellationToken)
    {
        var root = Path.TrimEndingDirectorySeparator(options.ApplicationsPath!);
        IAsyncDisposable held;
        try
        {
            held = await writeLock.TakeAsync(root, cancellationToken);
        }
        catch (StudioWriteLockUnavailableException)
        {
            return StudioCreateOutcome.Unavailable;
        }

        await using var _ = held;

        var kebab = StudioFileNames.Kebab(name);
        var target = Path.Combine(root, kebab);
        if (StudioFolder.Applications(options).Contains(name, StringComparer.OrdinalIgnoreCase) || Path.Exists(target))
        {
            return StudioCreateOutcome.Exists;
        }

        var temporary = Path.Combine(root, $".{kebab}-{Guid.NewGuid():N}");
        var prefix = temporary + Path.DirectorySeparatorChar;
        try
        {
            foreach (var (file, bytes) in files)
            {
                var path = Path.GetFullPath(Path.Combine(temporary, file));
                if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(".json", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A file of a new application is outside its folder.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await writer.WriteAsync(path, bytes, cancellationToken);
            }

            Directory.Move(temporary, target);
        }
        catch
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }

            throw;
        }

        return StudioCreateOutcome.Created;
    }
}
