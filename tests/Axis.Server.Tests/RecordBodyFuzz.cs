using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Axis.Expressions.Tests;

namespace Axis.Server.Tests;

/// <summary>The ids that a fuzzed body may name: tags for <c>tag</c> and <c>tags</c>, uploaded files for <c>attachment</c>. Empty lists give random ids.</summary>
internal sealed record RecordBodyIds(IReadOnlyList<Guid> Tags, IReadOnlyList<Guid> Files)
{
    public static RecordBodyIds None { get; } = new([], []);
}

/// <summary>
/// Draws the request bodies that the record API fuzz tests send for the <c>Specimen</c> entity of the FilesApp
/// fixture, which has every field type. A body starts as a valid create or update body with random values. In
/// one input of eight it is returned as it is. Otherwise it gets one to four edits of its JSON tree, and in one
/// input of four also edits of its bytes. The file has no database code, so the unit and the integration tests
/// both link it. The same seed gives the same bodies.
/// </summary>
internal static class RecordBodyFuzz
{
    // The body of a record write is limited to 64 KB.
    private const int MaxBytes = 64 * 1024 - 1;

    private const string Placeholder = "@@raw";

    private static readonly string[] _edgeTokens =
    [
        "1e1000000", "-0", "9223372036854775808", "-9223372036854775809", "0", "1", "-1", "1.5", "1e400", "0.000001", "123456.789",
        "99999999999999999999999999", "0.1e1", "5e0",
        "\"2026-02-30\"", "\"2026-10-06T24:00:00Z\"", "\"2026-01-01\"", "\"2026-01-01T00:00:00+14:01\"", "\"0001-01-01T00:00:00+14:00\"",
        "\"9999-12-31T23:59:59.9999999Z\"", "\"2026-01-01T00:00:60Z\"", "\"2026-01-01t10:00:00z\"",
        "\"\\ud800\"", "\"a\\u0000b\"", "\"\\u00e9\\u00e9\"", "\"\\ud83d\\ude00\\ud83d\\ude00\\ud83d\\ude00\\ud83d\\ude00\\ud83d\\ude00\\ud83d\\ude00\\ud83d\\ude00\\ud83d\\ude00\\ud83d\\ude00\\ud83d\\ude00\\ud83d\\ude00\"",
        "\"" + new string('a', 5000) + "\"",
        "\"OPEN\"", "\"Open\"", "\"open \"", "\"closed\"", "\"VND\"",
        "\"not-a-guid\"", "\"00000000-0000-0000-0000-000000000000\"", "\"{3b7c1d52-4e8a-4f60-9a1b-7d2e5c8f0a02}\"", "\"3b7c1d524e8a4f609a1b7d2e5c8f0a02\"",
        "\"1\"", "\"abc\"", "\"\"",
        "null", "{}", "[]", "true", "false", "[null]", "[[]]", "{\"a\":1}",
    ];

    private static readonly string[] _versionTokens = ["0", "-1", "1.5", "\"1\"", "1e400", "null", "9223372036854775807", "9223372036854775808"];

    private static readonly string[] _rowTokens = ["1", "\"x\"", "null", "[]", "[{}]", "true"];

    private static readonly string[] _unknownNames = ["unknown", "Title", "TITLE", "values", "Values", "id", "version", "lineCount", "LineCount", "", "a/b", "a~b", "title ", "\u00e9"];

    private static readonly byte[][] _byteInserts = [[0x00], [0xFF], [0xC3, 0x28], [(byte)'{'], [(byte)'"'], [(byte)'\\']];

    private static readonly UTF8Encoding _strictUtf8 = new(false, true);

    /// <summary>A create body: a valid one, or a valid one with edits.</summary>
    public static byte[] Create(Random random, RecordBodyIds ids) => Draw(random, update: false, version: 0, ids);

    /// <summary>An update body for a record at <paramref name="version"/>: a valid one, or a valid one with edits.</summary>
    public static byte[] Update(Random random, long version, RecordBodyIds ids) => Draw(random, update: true, version, ids);

    /// <summary>A valid create body, without edits.</summary>
    public static byte[] Valid(Random random, RecordBodyIds ids) => Serialize(Base(random, update: false, version: 0, ids), []);

    /// <summary>Writes a body for a failure message: a C# literal of its text, or <c>base64:</c> and its bytes when they are not UTF-8.</summary>
    public static string Show(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            return Fuzz.Literal(_strictUtf8.GetString(body));
        }
        catch (DecoderFallbackException)
        {
            return "base64:" + Convert.ToBase64String(body);
        }
    }

    private static byte[] Draw(Random random, bool update, long version, RecordBodyIds ids)
    {
        var root = Base(random, update, version, ids);
        if (random.Next(8) == 0)
        {
            return Serialize(root, []);
        }

        var raws = new List<string>();
        for (var edits = random.Next(1, 5); edits > 0; edits--)
        {
            Edit(random, root, update, ids, raws);
        }

        var body = Encoding.UTF8.GetBytes(Text(root, raws));
        if (random.Next(4) == 0)
        {
            for (var edits = random.Next(1, 4); edits > 0; edits--)
            {
                body = ByteEdit(random, body);
            }
        }

        return body.Length > MaxBytes ? body[..MaxBytes] : body;
    }

    // A valid body: each field has a random valid value or is left out, and a create always names the required title.
    private static JsonObject Base(Random random, bool update, long version, RecordBodyIds ids)
    {
        var values = new JsonObject();
        if (!update || random.Next(5) != 0)
        {
            values["title"] = Word(random, 1, 20);
        }

        if (random.Next(2) == 0)
        {
            values["code"] = Hex(random, 8);
        }

        if (random.Next(2) == 0)
        {
            values["quantity"] = random.Next(1, 1001);
        }

        if (random.Next(2) == 0)
        {
            values["ratio"] = random.Next(-999_999, 1_000_000) / 100m;
        }

        if (random.Next(2) == 0)
        {
            values["cost"] = random.NextInt64(0, 1_000_000_000_000);
        }

        var closed = random.Next(2) == 0;
        if (random.Next(4) != 0)
        {
            values["status"] = closed ? "closed" : "open";
        }
        else
        {
            closed = false;
        }

        // A closed specimen may not be active, so a valid body never sets both.
        if (random.Next(2) == 0)
        {
            values["active"] = !closed && random.Next(2) == 0;
        }

        if (random.Next(2) == 0)
        {
            values["dueOn"] = Date(random);
        }

        if (random.Next(2) == 0)
        {
            values["startedAt"] = DateTimeText(random);
        }

        if (random.Next(2) == 0)
        {
            values["tag"] = PickId(random, ids.Tags).ToString("D");
        }

        if (random.Next(2) == 0)
        {
            var tags = new JsonArray();
            foreach (var tag in Distinct(random, ids.Tags, random.Next(0, 3)))
            {
                tags.Add(tag.ToString("D"));
            }

            values["tags"] = tags;
        }

        if (random.Next(10) == 0)
        {
            values["attachment"] = PickId(random, ids.Files).ToString("D");
        }

        if (random.Next(2) == 0)
        {
            var lines = new JsonArray();
            for (var count = random.Next(0, 4); count > 0; count--)
            {
                lines.Add(Line(random));
            }

            values["lines"] = lines;
        }

        var root = new JsonObject();
        if (update)
        {
            // A write names the version it read: usually the one it was given, now and then another.
            root["version"] = random.Next(10) == 0 ? Math.Max(1, version + random.Next(-2, 3)) : version;
        }

        root["values"] = values;
        return root;
    }

    private static JsonObject Line(Random random)
    {
        var line = new JsonObject { ["name"] = Word(random, 1, 10) };
        if (random.Next(3) != 0)
        {
            line["amount"] = random.Next(-100_000_000, 100_000_000) / 1000m;
        }

        return line;
    }

    private static void Edit(Random random, JsonObject root, bool update, RecordBodyIds ids, List<string> raws)
    {
        var slots = Slots(root);
        switch (random.Next(9))
        {
            case 0:
            case 1:
                Replace(random, slots, Raw(_edgeTokens[random.Next(_edgeTokens.Length)], raws));
                break;
            case 2:
                Delete(random, slots);
                break;
            case 3:
                Unknown(random, root);
                break;
            case 4:
                SetComputed(random, root);
                break;
            case 5:
                Replace(random, slots, Raw(Nested(70), raws));
                break;
            case 6:
                Tags(random, root, ids);
                break;
            case 7:
                BadRow(random, root, raws);
                break;
            default:
                if (update || random.Next(2) == 0)
                {
                    root["version"] = Raw(_versionTokens[random.Next(_versionTokens.Length)], raws);
                }
                else
                {
                    Lines(random, root);
                }

                break;
        }
    }

    private static void Replace(Random random, List<Slot> slots, JsonNode value)
    {
        if (slots.Count > 0)
        {
            slots[random.Next(slots.Count)].Set(value);
        }
    }

    private static void Delete(Random random, List<Slot> slots)
    {
        if (slots.Count > 0)
        {
            slots[random.Next(slots.Count)].Remove();
        }
    }

    // Adds a property the entity does not declare, or one with another letter case, to the body or its values.
    private static void Unknown(Random random, JsonObject root)
    {
        var target = random.Next(2) == 0 || root["values"] is not JsonObject values ? root : values;
        JsonNode? value = random.Next(3) switch
        {
            0 => "x",
            1 => random.Next(100),
            _ => null,
        };
        target[_unknownNames[random.Next(_unknownNames.Length)]] = value;
    }

    private static void SetComputed(Random random, JsonObject root)
    {
        if (root["values"] is JsonObject values)
        {
            values["lineCount"] = random.Next(0, 10);
        }
    }

    private static void Tags(Random random, JsonObject root, RecordBodyIds ids)
    {
        if (root["values"] is not JsonObject values)
        {
            return;
        }

        var tags = new JsonArray();
        switch (random.Next(3))
        {
            case 0:
                // More ids than a set may hold.
                for (var index = 0; index < 1001; index++)
                {
                    tags.Add(RandomId(random).ToString("D"));
                }

                break;
            case 1:
                // The same id twice.
                var id = PickId(random, ids.Tags).ToString("D");
                tags.Add(id);
                tags.Add(id);
                break;
            default:
                // More ids than the validation allows.
                foreach (var tag in Distinct(random, ids.Tags, random.Next(4, 8)))
                {
                    tags.Add(tag.ToString("D"));
                }

                break;
        }

        values["tags"] = tags;
    }

    // Gives the rows of lines a shape that is not an object, or sets a row to a bad value.
    private static void BadRow(Random random, JsonObject root, List<string> raws)
    {
        if (root["values"] is not JsonObject values)
        {
            return;
        }

        if (values["lines"] is not JsonArray lines)
        {
            lines = [];
            values["lines"] = lines;
        }

        var bad = Raw(_rowTokens[random.Next(_rowTokens.Length)], raws);
        if (lines.Count == 0 || random.Next(2) == 0)
        {
            lines.Add(bad);
        }
        else
        {
            lines[random.Next(lines.Count)] = bad;
        }
    }

    // More rows than the validation allows.
    private static void Lines(Random random, JsonObject root)
    {
        if (root["values"] is JsonObject values)
        {
            var lines = new JsonArray();
            for (var count = random.Next(6, 12); count > 0; count--)
            {
                lines.Add(Line(random));
            }

            values["lines"] = lines;
        }
    }

    private static string Nested(int depth) => new string('[', depth) + new string(']', depth);

    // A raw JSON token goes into the tree as a placeholder string, which Text replaces by the token itself.
    // The tree could not hold a token that is not valid in a .NET string, such as "\ud800", or a number with a huge exponent.
    private static JsonValue Raw(string token, List<string> raws)
    {
        raws.Add(token);
        return JsonValue.Create($"{Placeholder}{raws.Count - 1}@@")!;
    }

    private static string Text(JsonObject root, List<string> raws)
    {
        var text = root.ToJsonString();
        for (var index = 0; index < raws.Count; index++)
        {
            text = text.Replace($"\"{Placeholder}{index}@@\"", raws[index], StringComparison.Ordinal);
        }

        return text;
    }

    private static byte[] Serialize(JsonObject root, List<string> raws) => Encoding.UTF8.GetBytes(Text(root, raws));

    private static byte[] ByteEdit(Random random, byte[] body)
    {
        if (body.Length == 0)
        {
            return body;
        }

        var at = random.Next(body.Length);
        var length = Math.Min(random.Next(1, 40), body.Length - at);
        switch (random.Next(4))
        {
            case 0:
                return [.. body[..at], .. body[(at + length)..]];
            case 1:
                return [.. body[..at], .. _byteInserts[random.Next(_byteInserts.Length)], .. body[at..]];
            case 2:
                return body[..at];
            default:
                // A copy of a span next to itself, which gives duplicate keys when the span holds one.
                return [.. body[..(at + length)], .. body[at..]];
        }
    }

    // Every place in the tree that holds a value: an object property or an array item.
    private static List<Slot> Slots(JsonNode root)
    {
        var slots = new List<Slot>();
        Collect(root, slots);
        return slots;
    }

    private static void Collect(JsonNode node, List<Slot> slots)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj.ToList())
                {
                    slots.Add(new Slot(obj, name, 0));
                    if (child is not null)
                    {
                        Collect(child, slots);
                    }
                }

                break;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    slots.Add(new Slot(array, null, index));
                    if (array[index] is { } child)
                    {
                        Collect(child, slots);
                    }
                }

                break;
        }
    }

    private static string Word(Random random, int minLength, int maxLength)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -_.é";
        var builder = new StringBuilder();
        for (var count = random.Next(minLength, maxLength + 1); count > 0; count--)
        {
            builder.Append(alphabet[random.Next(alphabet.Length)]);
        }

        return builder.ToString();
    }

    private static string Hex(Random random, int length)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < length; index++)
        {
            builder.Append(random.Next(16).ToString("x", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string Date(Random random) =>
        new DateOnly(random.Next(1990, 2060), random.Next(1, 13), random.Next(1, 29)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string DateTimeText(Random random)
    {
        var text = $"{Date(random)}T{random.Next(0, 24):00}:{random.Next(0, 60):00}:{random.Next(0, 60):00}";
        if (random.Next(2) == 0)
        {
            text += "." + random.Next(0, 1_000_000).ToString("000000", CultureInfo.InvariantCulture)[..random.Next(1, 7)];
        }

        return random.Next(2) == 0
            ? text + "Z"
            : text + (random.Next(2) == 0 ? "+" : "-") + $"{random.Next(0, 14):00}:{random.Next(0, 60):00}";
    }

    private static Guid RandomId(Random random)
    {
        var bytes = new byte[16];
        random.NextBytes(bytes);
        return new Guid(bytes);
    }

    private static Guid PickId(Random random, IReadOnlyList<Guid> known) =>
        known.Count == 0 ? RandomId(random) : known[random.Next(known.Count)];

    private static HashSet<Guid> Distinct(Random random, IReadOnlyList<Guid> known, int count)
    {
        var chosen = new HashSet<Guid>();
        for (var attempt = 0; attempt < count * 4 && chosen.Count < count; attempt++)
        {
            chosen.Add(PickId(random, known));
        }

        return chosen;
    }

    // A place in the tree: the property of an object or the item of an array.
    private readonly record struct Slot(JsonNode Parent, string? Name, int Index)
    {
        public void Set(JsonNode value)
        {
            if (Parent is JsonObject obj)
            {
                obj[Name!] = value;
            }
            else if (Parent is JsonArray array && Index < array.Count)
            {
                array[Index] = value;
            }
        }

        public void Remove()
        {
            if (Parent is JsonObject obj)
            {
                obj.Remove(Name!);
            }
            else if (Parent is JsonArray array && Index < array.Count)
            {
                array.RemoveAt(Index);
            }
        }
    }
}
