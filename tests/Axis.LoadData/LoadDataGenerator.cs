using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Axis.LoadData;

/// <summary>
/// Writes an application of the typical size in docs/domain/knowledge.md, with seed records in every
/// entity. It is the data of the load test in docs/reference/performance.md. The counts are fixed.
/// One seeded random generator draws every id and value, in a fixed order, so the same seed writes
/// the same files and another seed changes ids and values but not the shape.
/// The application also declares a connector, an event, two triggers and two processes. A change to the quantity of an
/// Entity00 record raises the event and then calls the connector, so the run makes connector calls and event deliveries.
/// </summary>
#pragma warning disable CA5394 // Seeded data for a test application, not security.
public static class LoadDataGenerator
{
    public const int Entities = 40;
    public const int FieldsPerEntity = 25;
    public const int DataSources = 160;
    public const int Rules = 260;
    public const int ProcessesPerEntity = 11;
    public const int RecordsPerEntity = 100;
    public const int TextKeys = 1_600;

    /// <summary>The name of the <c>url</c> binding that holds the base URL of the connector. The tenant sets it.</summary>
    public const string ConnectorUrlBinding = "LoadServiceUrl";

    private const int ValidationsPerEntity = 2;
    private const int MaxQuantity = 1_000;

    private static readonly string[] _locales = ["en", "vi"];

    private static readonly string[] _editActions = ["read", "create", "update", "delete"];
    private static readonly string[] _readActions = ["read"];
    private static readonly string[] _startActions = ["start"];
    private static readonly string[] _invokeActions = ["invoke"];
    private static readonly string[] _updateKinds = ["update"];
    private static readonly string[] _quantityFields = ["quantity"];

    private static readonly string[] _words =
    [
        "alpha", "bravo", "cedar", "delta", "ember", "falcon", "garnet", "harbor", "iris", "juniper", "kestrel", "lotus",
        "meadow", "nectar", "onyx", "pebble", "quartz", "raven", "saffron", "tundra", "umber", "violet", "willow", "zephyr",
    ];

    private static readonly JsonSerializerOptions _options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>
    /// Writes the application into <paramref name="folder"/>, which is created when it does not exist.
    /// Throws <see cref="InvalidOperationException"/> when it exists and holds any file or folder.
    /// </summary>
    public static void Write(string folder, int seed)
    {
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
        {
            throw new InvalidOperationException($"The folder '{folder}' is not empty. Choose a new folder or an empty one.");
        }

        Directory.CreateDirectory(folder);
        new Writer(folder, new Random(seed)).Write();
    }

    private sealed class Writer(string folder, Random random)
    {
        private readonly SortedSet<string> _textKeys = new(StringComparer.Ordinal);

        public void Write()
        {
            WriteFile("application.json", new { id = NewId(), kind = "application", name = "LoadData", formatVersion = 1 });

            // A site needs an identity provider. The accounts provider is Axis's own and has no settings.
            WriteFile("identity-providers/accounts.json", new { id = NewId(), kind = "identityProvider", name = "accounts", formatVersion = 1, type = "accounts" });

            WriteRules();
            WriteEntities();
            WriteDataSources();
            WriteForms();
            WriteRoles();
            WritePolicies();
            WriteProcesses();
            WriteEndpoints();
            WriteConnectors();
            WriteEvents();
            WriteTriggers();
            WritePages();
            WriteSite();
            WriteSeeds();
            WriteTexts();
        }

        private void WriteRules()
        {
            for (var k = 0; k < Rules; k++)
            {
                // Every tenth rule calls the one before it. Each rule holds for every quantity from 0 to 1,000.
                var expression = $"value >= 0 and value <= {MaxQuantity + k}";
                if (k % 10 == 9)
                {
                    expression = $"Rule{k - 1:D3}(value) and {expression}";
                }

                WriteFile($"rules/rule-{k:D3}.json", new
                {
                    id = NewId(),
                    kind = "rule",
                    name = $"Rule{k:D3}",
                    formatVersion = 1,
                    parameters = new[] { new { name = "value", type = "integer" } },
                    resultType = "boolean",
                    expression,
                });
            }
        }

        private void WriteEntities()
        {
            for (var i = 0; i < Entities; i++)
            {
                var validations = new List<object>();
                for (var j = 0; j < ValidationsPerEntity; j++)
                {
                    validations.Add(new
                    {
                        expression = $"Rule{(ValidationsPerEntity * i) + j:D3}(quantity)",
                        message = Label($"entity{i:D2}.check{j}"),
                        field = "quantity",
                    });
                }

                WriteFile($"entities/entity-{i:D2}.json", new
                {
                    id = NewId(),
                    kind = "entity",
                    name = EntityName(i),
                    formatVersion = 1,
                    label = Label($"entity{i:D2}.label"),
                    displayField = "name",
                    fields = Fields(i),
                    validations,
                });
            }
        }

        private List<object> Fields(int index)
        {
            List<object> fields =
            [
                new { name = "name", type = "text", required = true, maxLength = 100, label = FieldLabel(index, "name") },
                index == 0
                    ? new { name = "parent", type = "text", maxLength = 100, label = FieldLabel(index, "parent") }
                    : new { name = "parent", type = "reference", target = EntityName(index - 1), label = FieldLabel(index, "parent") },
                new { name = "status", type = "enum", values = new[] { "open", "closed" }, label = FieldLabel(index, "status") },
                new { name = "quantity", type = "integer", label = FieldLabel(index, "quantity") },
                new { name = "price", type = "decimal", precision = 18, scale = 2, label = FieldLabel(index, "price") },
                new { name = "total", type = "decimal", precision = 18, scale = 2, expression = "quantity * price", label = FieldLabel(index, "total") },
                new { name = "dueOn", type = "date", label = FieldLabel(index, "dueOn") },
                new { name = "active", type = "boolean", label = FieldLabel(index, "active") },
            ];
            if (index > 0)
            {
                fields.Add(new { name = "owner", type = "reference", target = EntityName(0), label = FieldLabel(index, "owner") });
            }

            for (var extra = 1; fields.Count < FieldsPerEntity; extra++)
            {
                var name = $"extra{extra:D2}";
                fields.Add(new { name, type = "text", maxLength = 200, label = FieldLabel(index, name) });
            }

            return fields;
        }

        private void WriteDataSources()
        {
            for (var d = 0; d < DataSources; d++)
            {
                var entity = d % Entities;
                var fields = new List<object>
                {
                    new { name = "name", path = "name" },
                    new { name = "quantity", path = "quantity" },
                    new { name = "status", path = "status" },
                };
                if (entity > 0)
                {
                    fields.Add(new { name = "parent", path = "parent" });
                }

                WriteFile($"data-sources/data-source-{d:D3}.json", new
                {
                    id = NewId(),
                    kind = "dataSource",
                    name = $"DataSource{d:D3}",
                    formatVersion = 1,
                    entity = EntityName(entity),
                    fields,
                    parameters = new[] { new { name = "minQuantity", type = "integer" } },
                    filter = $"(minQuantity is null or quantity >= minQuantity) and Rule{d % Rules:D3}(quantity)",
                    sort = "name",
                    pageSize = 20,
                });
            }
        }

        private void WriteForms()
        {
            for (var i = 0; i < Entities; i++)
            {
                WriteFile($"forms/entity-{i:D2}-review.json", new
                {
                    id = NewId(),
                    kind = "form",
                    name = $"Entity{i:D2}Review",
                    formatVersion = 1,
                    entity = EntityName(i),
                    sections = new[]
                    {
                        new
                        {
                            title = Label($"entity{i:D2}.section"),
                            fields = new[]
                            {
                                new { field = "name", readOnly = true },
                                new { field = "status", readOnly = false },
                                new { field = "quantity", readOnly = false },
                            },
                        },
                    },
                });
            }
        }

        private void WriteRoles()
        {
            foreach (var role in new[] { "clerk", "reviewer", "auditor", "load-caller" })
            {
                WriteFile($"roles/{role}.json", new
                {
                    id = NewId(),
                    kind = "role",
                    name = role,
                    formatVersion = 1,
                    label = Label($"role.{role}"),
                });
            }
        }

        private void WritePolicies()
        {
            var entities = Enumerable.Range(0, Entities).Select(EntityName).ToList();
            var dataSources = Enumerable.Range(0, DataSources).Select(d => $"DataSource{d:D3}").ToList();
            var processes = Enumerable.Range(0, Entities)
                .SelectMany(i => Enumerable.Range(0, ProcessesPerEntity).Select(p => ProcessName(i, p)))
                .ToList();

            var clerk = new List<object>();
            clerk.AddRange(entities.Select(entity => new { entity, actions = _editActions }));
            clerk.AddRange(dataSources.Select(dataSource => new { dataSource, actions = _readActions }));
            clerk.AddRange(processes.Select(process => new { process, actions = _startActions }));
            WritePolicy("clerk-access", "ClerkAccess", "clerk", clerk);

            foreach (var role in new[] { "reviewer", "auditor" })
            {
                var grants = new List<object>();
                grants.AddRange(entities.Select(entity => new { entity, actions = _readActions }));
                grants.AddRange(dataSources.Select(dataSource => new { dataSource, actions = _readActions }));
                WritePolicy($"{role}-access", $"{char.ToUpperInvariant(role[0])}{role[1..]}Access", role, grants);
            }

            // The machine caller of the inbound endpoint. It holds no other grant.
            WritePolicy("load-caller-access", "LoadCallerAccess", "load-caller", [new { endpoint = "CreateEntity00", actions = _invokeActions }]);
        }

        private void WritePolicy(string file, string name, string role, List<object> grants) =>
            WriteFile($"policies/{file}.json", new { id = NewId(), kind = "policy", name, formatVersion = 1, roles = new[] { role }, grants });

        private void WriteProcesses()
        {
            for (var i = 0; i < Entities; i++)
            {
                for (var p = 0; p < ProcessesPerEntity; p++)
                {
                    WriteFile($"processes/entity-{i:D2}-process-{p:D2}.json", new
                    {
                        id = NewId(),
                        kind = "process",
                        name = ProcessName(i, p),
                        formatVersion = 1,
                        entity = EntityName(i),
                        start = p == 0 ? "review" : "update",
                        steps = p == 0 ? ReviewSteps(i) : UpdateSteps(p),
                    });
                }
            }
        }

        /// <summary>The inbound endpoint that creates an Entity00 record and starts its first process without waiting.</summary>
        private void WriteEndpoints() =>
            WriteFile("endpoints/create-entity-00.json", new
            {
                id = NewId(),
                kind = "endpoint",
                name = "CreateEntity00",
                formatVersion = 1,
                entity = EntityName(0),
                process = ProcessName(0, 1),
                input = new object[]
                {
                    new { name = "name", type = "text", required = true, maxLength = 100 },
                    new { name = "quantity", type = "integer", required = true },
                },
                values = new { name = "name", quantity = "quantity" },
            });

        /// <summary>The connector that the stub service of the load stack answers. It has no authentication and its operation has no outputs.</summary>
        private void WriteConnectors() =>
            WriteFile("connectors/load-service.json", new
            {
                id = NewId(),
                kind = "connector",
                name = "LoadService",
                formatVersion = 1,
                baseUrl = new { binding = ConnectorUrlBinding },
                idempotency = "Idempotency-Key",
                operations = new[]
                {
                    new
                    {
                        name = "notify",
                        method = "POST",
                        path = "/notifications",
                        input = new object[]
                        {
                            new { name = "name", type = "text", required = true },
                            new { name = "quantity", type = "integer" },
                        },
                    },
                },
            });

        private void WriteEvents() =>
            WriteFile("events/entity-00-changed.json", new
            {
                id = NewId(),
                kind = "event",
                name = "Entity00Changed",
                formatVersion = 1,
                entity = EntityName(0),
                fields = new[] { new { name = "quantity", type = "integer" } },
            });

        /// <summary>
        /// An update of the quantity of an Entity00 record starts the process that raises the event. The worker delivers the
        /// event to the async trigger, which starts the process that calls the connector. The triggers start both processes
        /// as the system, so no policy grants them.
        /// </summary>
        private void WriteTriggers()
        {
            WriteFile("processes/entity-00-announce.json", new
            {
                id = NewId(),
                kind = "process",
                name = "Entity00Announce",
                formatVersion = 1,
                entity = EntityName(0),
                start = "announce",
                steps = new object[]
                {
                    new { name = "announce", type = "operation", operation = "raiseEvent", @event = "Entity00Changed", fields = new { quantity = "quantity" }, next = "done" },
                    new { name = "done", type = "end" },
                },
            });

            WriteFile("processes/entity-00-notify.json", new
            {
                id = NewId(),
                kind = "process",
                name = "Entity00Notify",
                formatVersion = 1,
                entity = EntityName(0),
                start = "notify",
                steps = new object[]
                {
                    new
                    {
                        name = "notify",
                        type = "operation",
                        operation = "callConnector",
                        connector = new { name = "LoadService", operation = "notify" },
                        input = new { name = "name", quantity = "quantity" },
                        retry = new { attempts = 3, backoff = "PT5S" },
                        next = "done",
                    },
                    new { name = "done", type = "end" },
                },
            });

            WriteFile("triggers/entity-00-quantity-changed.json", new
            {
                id = NewId(),
                kind = "trigger",
                name = "Entity00QuantityChanged",
                formatVersion = 1,
                type = "dataChange",
                entity = EntityName(0),
                on = _updateKinds,
                fields = _quantityFields,
                process = "Entity00Announce",
            });

            WriteFile("triggers/entity-00-changed-delivered.json", new
            {
                id = NewId(),
                kind = "trigger",
                name = "Entity00ChangedDelivered",
                formatVersion = 1,
                type = "event",
                @event = "Entity00Changed",
                mode = "async",
                process = "Entity00Notify",
            });
        }

        private List<object> ReviewSteps(int index) =>
        [
            new
            {
                name = "review",
                type = "task",
                label = Label($"entity{index:D2}.review"),
                assignee = new { role = "reviewer" },
                form = $"Entity{index:D2}Review",
                outcomes = new[]
                {
                    new { name = "approve", label = Label("task.approve"), next = "markClosed" },
                    new { name = "reject", label = Label("task.reject"), next = "markOpen" },
                },
            },
            new { name = "markClosed", type = "operation", operation = "updateRecord", set = new { status = "'closed'" }, next = "done" },
            new { name = "markOpen", type = "operation", operation = "updateRecord", set = new { status = "'open'" }, next = "done" },
            new { name = "done", type = "end" },
        ];

        private static List<object> UpdateSteps(int process) =>
        [
            new { name = "update", type = "operation", operation = "updateRecord", set = new { status = process % 2 == 0 ? "'open'" : "'closed'" }, next = "done" },
            new { name = "done", type = "end" },
        ];

        private void WritePages()
        {
            for (var i = 0; i < Entities; i++)
            {
                var form = $"Entity{i:D2}Form";
                WriteFile($"pages/entity-{i:D2}-list.json", new
                {
                    id = NewId(),
                    kind = "page",
                    name = $"Entity{i:D2}List",
                    formatVersion = 1,
                    title = Label($"entity{i:D2}.listTitle"),
                    widgets = new[]
                    {
                        new
                        {
                            type = "table",
                            dataSource = $"DataSource{i:D3}",
                            rowActions = new[] { new { type = "navigate", page = form, label = Label("actions.open"), record = "id" } },
                            toolbarActions = new[] { new { type = "navigate", page = form, label = Label("actions.new"), record = (string?)null } },
                        },
                    },
                });

                WriteFile($"pages/entity-{i:D2}-form.json", new
                {
                    id = NewId(),
                    kind = "page",
                    name = form,
                    formatVersion = 1,
                    title = Label($"entity{i:D2}.formTitle"),
                    widgets = new[] { new { type = "form", form = $"Entity{i:D2}Review" } },
                });
            }
        }

        private void WriteSite() =>
            WriteFile("sites/load.json", new
            {
                id = NewId(),
                kind = "site",
                name = "Load",
                formatVersion = 1,
                path = "load",
                title = Label("site.title"),
                locales = new { @default = "en", fallback = "en", available = _locales },
                navigation = Enumerable.Range(0, Entities)
                    .Select(i => new { page = $"Entity{i:D2}List", label = Label($"entity{i:D2}.label") })
                    .ToArray(),
            });

        private void WriteSeeds()
        {
            var recordIds = new List<string>[Entities];
            for (var i = 0; i < Entities; i++)
            {
                var ids = new List<string>(RecordsPerEntity);
                var records = new List<object>(RecordsPerEntity);
                for (var r = 0; r < RecordsPerEntity; r++)
                {
                    var id = NewId();
                    ids.Add(id);
                    records.Add(new { id, values = Values(i, recordIds) });
                }

                recordIds[i] = ids;
                WriteFile($"seeds/entity-{i:D2}.json", new
                {
                    id = NewId(),
                    kind = "seed",
                    name = $"Entity{i:D2}Seed",
                    formatVersion = 1,
                    entity = EntityName(i),
                    records,
                });
            }
        }

        /// <summary>The values of one record. A reference points at a record of an earlier seed. The total is computed, so it is never set.</summary>
        private Dictionary<string, object> Values(int index, List<string>[] recordIds)
        {
            var values = new Dictionary<string, object>
            {
                ["name"] = $"{Word()} {random.Next(1, 10_000)}",
                ["parent"] = index == 0 ? Word() : Pick(recordIds[index - 1]),
                ["status"] = random.Next(2) == 0 ? "open" : "closed",
                ["quantity"] = random.Next(0, MaxQuantity + 1),
                ["price"] = random.Next(0, 1_000_000) / 100m,
                ["dueOn"] = new DateOnly(2026, 1, 1).AddDays(random.Next(365)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["active"] = random.Next(2) == 0,
            };
            if (index > 0)
            {
                values["owner"] = Pick(recordIds[0]);
            }

            values["extra01"] = Word();
            if (random.Next(2) == 0)
            {
                values["extra02"] = $"{Word()} {Word()}";
            }

            return values;
        }

        private void WriteTexts()
        {
            for (var filler = 0; _textKeys.Count < TextKeys; filler++)
            {
                _textKeys.Add($"common.text{filler:D4}");
            }

            foreach (var locale in _locales)
            {
                WriteFile($"texts/{locale}.json", new
                {
                    id = NewId(),
                    kind = "text",
                    name = $"Texts{char.ToUpperInvariant(locale[0])}{locale[1..]}",
                    formatVersion = 1,
                    locale,
                    texts = _textKeys.ToDictionary(key => key, key => $"{key} ({locale})"),
                });
            }
        }

        private object FieldLabel(int index, string field) => Label($"entity{index:D2}.{field}");

        private object Label(string textKey)
        {
            _textKeys.Add(textKey);
            return new { textKey };
        }

        private string Word() => _words[random.Next(_words.Length)];

        private string Pick(List<string> ids) => ids[random.Next(ids.Count)];

        /// <summary>A version 4 UUID drawn from the seeded generator.</summary>
        private string NewId()
        {
            var bytes = new byte[16];
            random.NextBytes(bytes);
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            return new Guid(bytes, bigEndian: true).ToString();
        }

        private void WriteFile(string relativePath, object resource)
        {
            var path = Path.Combine(folder, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(resource, _options) + "\n");
        }

        private static string EntityName(int index) => $"Entity{index:D2}";

        private static string ProcessName(int entity, int process) => $"Entity{entity:D2}Process{process:D2}";
    }
}
