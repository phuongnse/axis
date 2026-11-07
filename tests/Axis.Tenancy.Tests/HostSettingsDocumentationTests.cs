using System.Text.RegularExpressions;

namespace Axis.Tenancy.Tests;

/// <summary>The setting keys in the settings tables under docs/ are the keys the hosts read, no more and no fewer.</summary>
public sealed partial class HostSettingsDocumentationTests
{
    // The entity designer's field settings and the fuzz tests' budgets are not configuration. The control
    // plane and model binding settings are compared by ControlPlaneSettingsTableTests and AiSettingsTests.
    private static readonly HashSet<string> _skippedPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "reference/entity-designer.md",
        "reference/fuzz-tests.md",
        "reference/control-plane.md",
        "reference/model-bindings.md",
    };

    // ASP.NET Core and Kestrel read these from the Kestrel section that the server passes on. Axis code never reads them.
    private static readonly HashSet<string> _excludedDocumentedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "ASPNETCORE_HTTP_PORTS",
        "Kestrel:Endpoints:Https:*",
    };

    // Keys relative to a tenant's Oidc section or to the Email section, which the docs give under their full keys.
    // The Ai keys are compared by AiSettingsTests.
    private static readonly HashSet<string> _excludedCodeSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "Oidc",
        "Claims",
        "Smtp",
        "Ai",
    };

    [Fact]
    public void The_settings_tables_under_docs_list_the_settings_the_hosts_read()
    {
        var root = RepositoryRoot();
        var message = Compare(DocumentedSettings(root), CodeSettings(root));

        Assert.True(message is null, message);
    }

    [Fact]
    public void A_key_on_one_side_only_is_named()
    {
        var code = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Made:Read", "Made:Both" };
        var documented = new List<DocumentedSetting>
        {
            new("Made:Both", "reference/host-settings.md"),
            new("Made:Documented", "reference/host-settings.md"),
        };

        var message = Compare(documented, code);

        Assert.NotNull(message);
        Assert.Contains("Made:Read", message);
        Assert.Contains("Made:Documented", message);
        Assert.Contains("reference/host-settings.md", message);
    }

    private sealed record DocumentedSetting(string Key, string Page);

    private sealed record SourceFile(string Path, string Text);

    private static string? Compare(IReadOnlyList<DocumentedSetting> documented, IReadOnlySet<string> code)
    {
        var readOnly = code
            .Where(key => !documented.Any(setting => Documents(setting.Key, key)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var unread = documented
            .Where(setting => !code.Any(key => Documents(setting.Key, key)))
            .OrderBy(setting => setting.Page, StringComparer.Ordinal)
            .ThenBy(setting => setting.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (readOnly.Count == 0 && unread.Count == 0)
        {
            return null;
        }

        var problems = new List<string>();
        if (readOnly.Count > 0)
        {
            problems.Add($"Read by a host but missing from the settings tables under docs/: {string.Join(", ", readOnly.Select(key => $"`{key}`"))}.");
        }

        if (unread.Count > 0)
        {
            problems.Add($"In a settings table under docs/ but not read by a host: {string.Join(", ", unread.Select(setting => $"`{setting.Key}` (docs/{setting.Page})"))}.");
        }

        return string.Join(" ", problems);
    }

    // A documented wildcard matches every code key under its prefix, and a code section matches every documented key under it.
    private static bool Documents(string documentedKey, string codeKey) =>
        documentedKey.Equals(codeKey, StringComparison.OrdinalIgnoreCase)
        || documentedKey.EndsWith('*') && codeKey.StartsWith(documentedKey[..^1], StringComparison.OrdinalIgnoreCase)
        || documentedKey.StartsWith(codeKey + ":", StringComparison.OrdinalIgnoreCase);

    private static List<DocumentedSetting> DocumentedSettings(string root)
    {
        var docs = Path.Combine(root, "docs");
        var settings = new List<DocumentedSetting>();
        foreach (var file in Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories))
        {
            var page = Path.GetRelativePath(docs, file).Replace('\\', '/');
            if (_skippedPages.Contains(page))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!SettingHeader().IsMatch(lines[i]))
                {
                    continue;
                }

                var headers = lines[i].Split('|');
                var variable = Array.FindIndex(headers, cell => cell.Trim() == "Environment variable");
                for (var j = i + 2; j < lines.Length && lines[j].StartsWith('|'); j++)
                {
                    if (lines[j].Contains("_(planned for", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var cells = lines[j].Split('|');
                    var cell = cells[1].Trim() == "none" && variable > 0 ? cells[variable] : cells[1];
                    foreach (Match match in BacktickedKey().Matches(cell))
                    {
                        var key = Placeholder().Replace(match.Groups[1].Value, "<>");
                        if (!_excludedDocumentedKeys.Any(excluded => Documents(excluded, key)))
                        {
                            settings.Add(new DocumentedSetting(key, page));
                        }
                    }
                }
            }
        }

        return settings;
    }

    private static HashSet<string> CodeSettings(string root)
    {
        var sources = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "obj" or "bin" or "Migrations"))
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .ToList();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sectionOptions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            foreach (Match match in SettingConstant().Matches(source.Text))
            {
                keys.Add(match.Groups[1].Value);
            }

            foreach (Match match in LiteralKey().Matches(source.Text))
            {
                keys.Add(match.Groups[1].Value);
            }
        }

        foreach (var source in sources)
        {
            foreach (Match bind in BoundSection().Matches(source.Text))
            {
                var declaring = bind.Groups[1].Success
                    ? sources.First(candidate => Regex.IsMatch(candidate.Text, $@"\bclass {bind.Groups[1].Value}\b"))
                    : source;
                var section = SectionConstant().Match(declaring.Text).Groups[1].Value;
                foreach (Match property in BoundProperty().Matches(declaring.Text))
                {
                    keys.Add($"{section}:{property.Groups[1].Value}");
                }

                sectionOptions.Add(declaring.Path);
            }
        }

        foreach (var source in sources.Where(source => !sectionOptions.Contains(source.Path)))
        {
            var section = SectionConstant().Match(source.Text);
            if (!section.Success)
            {
                continue;
            }

            var name = section.Groups[1].Value;
            var reads = RelativeRead().Matches(source.Text)
                .Select(match => match.Groups.Values.Skip(1).First(group => group.Success).Value)
                .ToList();
            if (reads.Count == 0)
            {
                keys.Add(name);
            }

            foreach (var read in reads)
            {
                keys.Add($"{name}:{read}");
            }
        }

        keys.RemoveWhere(key => _excludedCodeSections.Contains(key.Split(':')[0]));
        return keys;
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

    [GeneratedRegex(@"^\|\s*Setting\s*\|")]
    private static partial Regex SettingHeader();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex BacktickedKey();

    [GeneratedRegex("<[^>]*>|\\{[^}]*\\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"const string \w*[Ss]etting\s*=\s*""([^""]+)""")]
    private static partial Regex SettingConstant();

    [GeneratedRegex(@"(?<![$@])""((?:[A-Z][A-Za-z]*(?::[A-Z][A-Za-z]*)+)|OTEL_[A-Z_]+)""")]
    private static partial Regex LiteralKey();

    [GeneratedRegex(@"GetSection\((?:(\w+)\.)?SectionName\)\.Bind\(")]
    private static partial Regex BoundSection();

    [GeneratedRegex(@"const string SectionName\s*=\s*""([^""]+)""")]
    private static partial Regex SectionConstant();

    [GeneratedRegex(@"public [\w<>?]+ (\w+) \{ get;")]
    private static partial Regex BoundProperty();

    [GeneratedRegex(@"\[""([A-Z]\w*)""\]|\[nameof\((\w+)\)\]|GetSection\(""([A-Z]\w*)""\)|GetValue<\w+>\(""([A-Z]\w*)""")]
    private static partial Regex RelativeRead();
}
