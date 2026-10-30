using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Records;
using Axis.Expressions.Tests;
using Axis.Server.Tests;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Fuzz tests of the record API's request bodies over HTTP on real PostgreSQL, on the <c>Specimen</c> entity of the
/// FilesApp fixture in tenant A, which has every field type. <c>requester</c> holds every grant on it, so no answer
/// is a 403. Every answer must be one the API documents, and a record that the API accepts must read back as a
/// valid record. See <see cref="RecordBodyFuzz"/> for the bodies and <see cref="Fuzz"/> for the seed and the budget.
/// </summary>
public sealed class RecordBodyFuzzTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Specimens = "/api/apps/FilesApp/entities/Specimen/records";
    private const string Tags = "/api/apps/FilesApp/entities/SpecimenTag/records";
    private const string Attachments = "/api/apps/FilesApp/entities/Specimen/fields/attachment/files";

    private static readonly HttpStatusCode[] _allowed =
    [
        HttpStatusCode.OK,
        HttpStatusCode.Created,
        HttpStatusCode.BadRequest,
        HttpStatusCode.NotFound,
        HttpStatusCode.Conflict,
        HttpStatusCode.RequestEntityTooLarge,
        HttpStatusCode.UnsupportedMediaType,
    ];

    private static readonly string?[] _otherContentTypes = ["text/plain", null, "application/json; charset=utf-8"];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private EntityModel Specimen
    {
        get
        {
            Assert.True(fixture.FilesModel.TryGetEntity("Specimen", out var specimen));
            return specimen;
        }
    }

    [Fact]
    public async Task Unmutated_bodies_are_written()
    {
        var ids = await PrepareAsync(files: false);
        var random = new Random(Fuzz.DefaultSeed + 1);

        for (var index = 0; index < 5; index++)
        {
            var body = RecordBodyFuzz.Valid(random, ids);

            using var response = await SendAsync(HttpMethod.Post, Specimens, body, "application/json");

            Assert.True(response.StatusCode == HttpStatusCode.Created, $"Body {RecordBodyFuzz.Show(body)} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(CancellationToken)}");
            await AssertStoredRecordIsValidAsync(await ReadIdAsync(response));
        }
    }

    [Fact]
    public async Task Create_answers_an_allowed_status_and_an_accepted_body_leaves_a_valid_record()
    {
        var ids = await PrepareAsync(files: true);
        var accepted = 0;

        await Fuzz.RunAsync(
            random => Draw(random, RecordBodyFuzz.Create(random, ids)),
            async input =>
            {
                using var response = await SendAsync(HttpMethod.Post, Specimens, input.Body, input.ContentType);

                await AssertAllowedAsync(response);
                if (response.IsSuccessStatusCode)
                {
                    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                    accepted++;
                    await AssertStoredRecordIsValidAsync(await ReadIdAsync(response));
                }
            },
            Show,
            perInput: TimeSpan.FromSeconds(15));

        Assert.True(accepted > 0, "The fuzzer sent no body that the API accepts.");
    }

    [Fact]
    public async Task Update_answers_an_allowed_status_and_an_accepted_body_leaves_a_valid_record()
    {
        var ids = await PrepareAsync(files: true);
        var (record, version) = await CreateBaseAsync();
        var accepted = 0;

        await Fuzz.RunAsync(
            // The body usually names the version of the last write, so a share of the updates is accepted.
            random => Draw(random, RecordBodyFuzz.Update(random, version, ids)),
            async input =>
            {
                using var response = await SendAsync(HttpMethod.Patch, $"{Specimens}/{record:D}", input.Body, input.ContentType);

                await AssertAllowedAsync(response);
                if (response.IsSuccessStatusCode)
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    using var written = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
                    version = written.RootElement.GetProperty("version").GetInt64();
                    accepted++;
                    await AssertStoredRecordIsValidAsync(record);
                }
            },
            Show,
            perInput: TimeSpan.FromSeconds(15));

        Assert.True(accepted > 0, "The fuzzer sent no body that the API accepts.");
    }

    // Three tags, and three uploaded files that wait to be attached when a fuzzed body names one.
    private async Task<RecordBodyIds> PrepareAsync(bool files)
    {
        var tags = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            using var response = await SendAsync(HttpMethod.Post, Tags, Encoding.UTF8.GetBytes($$"""{ "values": { "name": "Tag {{index}}" } }"""), "application/json");
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            tags.Add(await ReadIdAsync(response));
        }

        var uploaded = new List<Guid>();
        for (var index = 0; files && index < 3; index++)
        {
            using var request = Request(HttpMethod.Post, Attachments, HostA);
            var form = new MultipartFormDataContent();
            var part = new ByteArrayContent(Encoding.UTF8.GetBytes($"file {index}"));
            part.Headers.TryAddWithoutValidation("Content-Type", "text/plain");
            form.Add(part, "file", $"file{index}.txt");
            request.Content = form;
            using var response = await fixture.Client.SendAsync(request, CancellationToken);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            uploaded.Add(await ReadIdAsync(response));
        }

        return new RecordBodyIds(tags, uploaded);
    }

    private async Task<(Guid Id, long Version)> CreateBaseAsync()
    {
        using var response = await SendAsync(HttpMethod.Post, Specimens, Encoding.UTF8.GetBytes("""{ "values": { "title": "Base" } }"""), "application/json");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return (body.RootElement.GetProperty("id").GetGuid(), body.RootElement.GetProperty("version").GetInt64());
    }

    // One input in eight is sent with another content type, or with none.
    private static FuzzRequest Draw(Random random, byte[] body) =>
        new(body, random.Next(8) == 0 ? _otherContentTypes[random.Next(_otherContentTypes.Length)] : "application/json");

    private static string Show(FuzzRequest input) =>
        $"{RecordBodyFuzz.Show(input.Body)} as {input.ContentType ?? "no content type"}";

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, byte[] body, string? contentType)
    {
        using var request = Request(method, path, HostA);
        request.Content = new ByteArrayContent(body);
        if (contentType is not null)
        {
            request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        return await fixture.Client.SendAsync(request, CancellationToken);
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    // The status is one the record API documents, and a refusal is a problem without leaks. A 400 names its errors.
    private static async Task AssertAllowedAsync(HttpResponseMessage response)
    {
        Assert.Contains(response.StatusCode, _allowed);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        using var problem = await ReadProblemAsync(response, response.StatusCode);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            Assert.True(problem.RootElement.TryGetProperty("errors", out var errors) && errors.EnumerateObject().Any(), "A 400 holds no errors.");
        }
    }

    // The record reads back, passes the validations of its entity, and its values pass the rules of a create body.
    private async Task AssertStoredRecordIsValidAsync(Guid id)
    {
        using var read = await fixture.Client.SendAsync(Request($"{Specimens}/{id:D}", HostA), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var model = fixture.FilesModel;
        var specimen = Specimen;
        await using var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await connection.OpenAsync(CancellationToken);
        var stored = await RecordQueries.GetAsync(connection, model, specimen, id, CancellationToken);
        Assert.NotNull(stored);

        var sets = await RecordSets.ReadAsync(connection, model, specimen, stored, [], CancellationToken);
        var failures = RecordValidator.ValidateUpdate(model, specimen, stored, [], [], DateTimeOffset.UtcNow, sets);
        Assert.True(failures is null, $"The stored record fails a validation: {string.Join("; ", failures?.Select(failure => $"{failure.Key}: {string.Join(", ", failure.Value)}") ?? [])}");

        var body = new JsonObject { ["values"] = AsCreateValues(model, specimen, stored) };
        var parsed = RecordInputParser.Parse(Encoding.UTF8.GetBytes(body.ToJsonString()), specimen, model, RecordOperation.Create);
        Assert.True(
            parsed.Errors is null,
            $"The stored values are not a valid create body: {string.Join("; ", parsed.Errors?.Select(error => $"{error.Key}: {string.Join(", ", error.Value)}") ?? [])} in {body.ToJsonString()}");
    }

    // The stored values as the values of a create body: the declared fields that a body may set, and the declared
    // fields of each row. The file field is left out because an uploaded file attaches once, and so is the unique code.
    private static JsonObject AsCreateValues(ApplicationModel model, EntityModel specimen, Axis.Data.Records.Record stored)
    {
        var values = new JsonObject();
        foreach (var field in specimen.Fields)
        {
            if (field.IsComputed || field.Sequence is not null || field.Creator || field.Name is "attachment" or "code"
                || !stored.Values.TryGetValue(field.Name, out var value))
            {
                continue;
            }

            if (field.Type == FieldType.ChildCollection)
            {
                var child = model.FindEntity(field.Target!.Id)!;
                var rows = new JsonArray();
                foreach (var row in (value as JsonArray) ?? [])
                {
                    var kept = new JsonObject();
                    foreach (var childField in child.Fields.Where(childField => (row as JsonObject)?.ContainsKey(childField.Name) == true))
                    {
                        kept[childField.Name] = row![childField.Name]?.DeepClone();
                    }

                    rows.Add(kept);
                }

                values[field.Name] = rows;
            }
            else
            {
                values[field.Name] = value?.DeepClone();
            }
        }

        return values;
    }

    private sealed record FuzzRequest(byte[] Body, string? ContentType);
}
