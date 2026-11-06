using System.Reflection;
using System.Text.RegularExpressions;

namespace Axis.ControlPlane.Tests;

public sealed partial class ControlPlaneSettingsTableTests
{
    [Fact]
    public void The_settings_table_names_the_settings_the_code_binds()
    {
        var documented = DocumentedSettings();
        var bound = BoundSettings(typeof(ControlPlaneOptions), ControlPlaneOptions.SectionName);

        Assert.Equal(bound.Order(StringComparer.OrdinalIgnoreCase), documented.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> DocumentedSettings()
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "reference", "control-plane.md");
        var settings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            if (!line.StartsWith("| `ControlPlane:", StringComparison.Ordinal))
            {
                continue;
            }

            var cell = line.Split('|')[1];
            settings.Add(Placeholder().Replace(cell.Trim().Trim('`'), "<>"));
        }

        return settings;
    }

    // String, int and bool properties are settings. A list adds an index and a dictionary adds a key. Other classes hold more settings.
    private static IEnumerable<string> BoundSettings(Type type, string prefix)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = $"{prefix}:{property.Name}";
            var propertyType = property.PropertyType;
            if (propertyType == typeof(string) || propertyType == typeof(int) || propertyType == typeof(bool))
            {
                yield return name;
            }
            else if (propertyType == typeof(Dictionary<string, string>))
            {
                yield return $"{name}:<>";
            }
            else if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(List<>))
            {
                foreach (var nested in BoundSettings(propertyType.GetGenericArguments()[0], $"{name}:<>"))
                {
                    yield return nested;
                }
            }
            else if (propertyType.IsClass)
            {
                foreach (var nested in BoundSettings(propertyType, name))
                {
                    yield return nested;
                }
            }
        }
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

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Placeholder();
}
