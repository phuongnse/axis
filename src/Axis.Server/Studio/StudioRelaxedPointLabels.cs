using System.Text.Json.Nodes;

namespace Axis.Server.Studio;

/// <summary>
/// Proposed: the words Studio shows for the relaxed points of a release, read from the module files that release
/// stores, so a point reads as its module, entity and part were labelled when the release was published, never as its
/// technical name. A file that does not parse, or a label that no text holds, leaves that part without words, and
/// Studio falls back to the name as written.
/// </summary>
internal static class StudioRelaxedPointLabels
{
    /// <summary>The points with their words, from the release's <c>modules/</c> files by path.</summary>
    public static IReadOnlyList<StudioRelaxedPoint> Describe(
        IReadOnlyList<StudioRelaxedPoint> points, IReadOnlyDictionary<string, string> files)
    {
        if (points.Count == 0)
        {
            return points;
        }

        var modules = new Dictionary<string, ModuleFiles>(StringComparer.Ordinal);
        return [.. points.Select(point =>
        {
            if (!modules.TryGetValue(point.Module, out var module))
            {
                module = ModuleFiles.Read(point.Module, files);
                modules[point.Module] = module;
            }

            return module.Describe(point);
        })];
    }

    private sealed class ModuleFiles
    {
        private readonly Dictionary<string, Dictionary<string, string>> _texts = new(StringComparer.Ordinal);
        private readonly List<JsonNode> _contracts = [];
        private readonly List<JsonNode> _entities = [];
        private JsonNode? _manifest;

        public static ModuleFiles Read(string module, IReadOnlyDictionary<string, string> files)
        {
            var result = new ModuleFiles();
            var prefix = $"modules/{module}/";
            foreach (var (path, content) in files)
            {
                if (!path.StartsWith(prefix, StringComparison.Ordinal) || Parse(content) is not { } node)
                {
                    continue;
                }

                switch (node["kind"]?.GetValue<string>())
                {
                    case "module":
                        result._manifest = node;
                        break;
                    case "contract":
                        result._contracts.Add(node);
                        break;
                    case "entity":
                        result._entities.Add(node);
                        break;
                    case "text" when node["locale"]?.GetValue<string>() is { } locale && node["texts"] is JsonObject texts:
                        var map = result._texts.TryGetValue(locale, out var existing) ? existing : result._texts[locale] = new(StringComparer.Ordinal);
                        foreach (var (key, value) in texts)
                        {
                            if (value?.GetValue<string>() is { } text)
                            {
                                map[key] = text;
                            }
                        }

                        break;
                }
            }

            return result;
        }

        public StudioRelaxedPoint Describe(StudioRelaxedPoint point)
        {
            // A point is <entity>/<part>/<name>, and a state's point ends in /initial or /deletable.
            var segments = point.Point.Split('/');
            var moduleTitle = _manifest?["label"]?.GetValue<string>();
            if (segments.Length < 3)
            {
                return point with { ModuleTitle = moduleTitle };
            }

            var entity = segments[0];
            var name = segments[2];
            var contract = _contracts.FirstOrDefault(node => node["entity"]?.GetValue<string>() == entity);
            var entityTitles = Titles(_entities.FirstOrDefault(node => node["name"]?.GetValue<string>() == entity)?["label"]);
            var (part, label) = segments[1] switch
            {
                "transitions" => ("transition", Named(contract?["lifecycle"]?["transitions"], name)?["label"]),
                "states" when segments.Length == 4 && segments[3] is "initial" or "deletable" =>
                    (segments[3], Named(contract?["lifecycle"]?["states"], name)?["label"]),
                "validations" => ("validation", Named(contract?["validations"], name)?["label"]),
                "effects" => ("effect", Named(contract?["effects"], name)?["label"]),
                "ceiling" when name == "others" => ("others", null),
                "ceiling" => ("ceiling", Named(_manifest?["roles"], name)?["label"]),
                _ => ((string?)null, (JsonNode?)null),
            };
            return point with { ModuleTitle = moduleTitle, Part = part, EntityTitles = entityTitles, Titles = Titles(label) };
        }

        private Dictionary<string, string>? Titles(JsonNode? label)
        {
            if (label?["textKey"]?.GetValue<string>() is not { } key)
            {
                return null;
            }

            var titles = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (locale, texts) in _texts)
            {
                if (texts.TryGetValue(key, out var text))
                {
                    titles[locale] = text;
                }
            }

            return titles.Count > 0 ? titles : null;
        }

        private static JsonNode? Named(JsonNode? list, string name) =>
            list is JsonArray items ? items.FirstOrDefault(item => item?["name"]?.GetValue<string>() == name) : null;

        private static JsonNode? Parse(string content)
        {
            try
            {
                return JsonNode.Parse(content);
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }
    }
}
