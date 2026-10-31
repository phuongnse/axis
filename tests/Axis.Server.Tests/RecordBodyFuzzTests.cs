using System.Net;
using System.Text;
using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Expressions.Tests;
using Axis.Processes.Records;
using Axis.Server.Records;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Xunit.Sdk;

namespace Axis.Server.Tests;

/// <summary>
/// Fuzz tests of the record API's request bodies up to the database: the parsing of a create and an update body
/// and the checks of a create that need no storage, on the <c>Specimen</c> entity of the FilesApp fixture, which has
/// every field type. A refusal must map to a 400 problem. See <see cref="RecordBodyFuzz"/> for the bodies.
/// </summary>
public sealed class RecordBodyFuzzTests
{
    private static readonly RecordActor _requester = new("requester", ["employee"]);

    private static readonly Lazy<(ApplicationModel Application, EntityModel Specimen)> _fixture = new(() =>
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "FilesApp"));
        Assert.Empty(result.Diagnostics);
        var application = Assert.IsType<ApplicationModel>(result.Model);
        return (application, Assert.Single(application.Entities, entity => entity.Name == "Specimen"));
    });

    [Fact]
    public void The_unmutated_bodies_parse_and_pass_the_create_checks()
    {
        var (application, specimen) = _fixture.Value;
        var random = new Random(Fuzz.DefaultSeed);

        for (var index = 0; index < 50; index++)
        {
            var body = RecordBodyFuzz.Valid(random, RecordBodyIds.None);

            var parse = RecordCommand.ParseCreate(application, specimen, _requester, body);
            Assert.True(parse.Failure is null, $"Body {RecordBodyFuzz.Show(body)} was refused: {Describe(parse.Failure)}");
            var prepared = RecordCommand.PrepareCreate(application, specimen, _requester, parse.Input!, form: null, DateTimeOffset.UtcNow);
            Assert.True(prepared.Errors is null, $"Body {RecordBodyFuzz.Show(body)} failed a check: {string.Join("; ", prepared.Errors?.Keys ?? [])}");
        }
    }

    [Fact]
    public void The_edited_bodies_reach_both_the_success_path_and_the_refusals()
    {
        var (application, specimen) = _fixture.Value;
        var random = new Random(Fuzz.DefaultSeed);
        var accepted = 0;
        var refused = 0;

        for (var index = 0; index < 500; index++)
        {
            if (RecordCommand.ParseCreate(application, specimen, _requester, RecordBodyFuzz.Create(random, RecordBodyIds.None)).Failure is null)
            {
                accepted++;
            }
            else
            {
                refused++;
            }
        }

        Assert.InRange(accepted, 50, 450);
        Assert.InRange(refused, 50, 450);
    }

    [Fact]
    public void Create_body_is_accepted_or_refused_with_a_400_problem()
    {
        var (application, specimen) = _fixture.Value;

        Fuzz.Run(
            random => RecordBodyFuzz.Create(random, RecordBodyIds.None),
            body =>
            {
                var parse = RecordCommand.ParseCreate(application, specimen, _requester, body);
                if (parse.Failure is { } failure)
                {
                    AssertBadRequest(RecordEndpoints.WriteFailure(failure));
                    return;
                }

                var prepared = RecordCommand.PrepareCreate(application, specimen, _requester, parse.Input!, form: null, DateTimeOffset.UtcNow);
                if (prepared.Errors is { } errors)
                {
                    AssertBadRequest(RecordEndpoints.WriteFailure(new RecordCommandResult(RecordCommandOutcome.Invalid, Errors: errors)));
                }
            },
            RecordBodyFuzz.Show);
    }

    [Fact]
    public void Update_body_is_parsed_or_refused_with_a_400_problem()
    {
        var (application, specimen) = _fixture.Value;

        Fuzz.Run(
            random => RecordBodyFuzz.Update(random, random.Next(1, 5), RecordBodyIds.None),
            body =>
            {
                if (RecordCommand.ParseUpdate(application, specimen, _requester, body).Failure is { } failure)
                {
                    AssertBadRequest(RecordEndpoints.WriteFailure(failure));
                }
            },
            RecordBodyFuzz.Show);
    }

    [Fact]
    public void A_body_that_is_not_UTF_8_is_shown_as_base64()
    {
        byte[] body = [(byte)'{', 0xC3, 0x28, (byte)'}'];

        Assert.Equal("base64:e8MofQ==", RecordBodyFuzz.Show(body));
        Assert.Equal("\"{}\"", RecordBodyFuzz.Show(Encoding.UTF8.GetBytes("{}")));
    }

    [Fact]
    public void The_same_seed_draws_the_same_bodies() =>
        Assert.Equal(DrawWith(new Random(7)).ToList(), DrawWith(new Random(7)).ToList());

    private static IEnumerable<string> DrawWith(Random random) =>
        Enumerable.Range(0, 30).Select(_ => RecordBodyFuzz.Show(RecordBodyFuzz.Create(random, RecordBodyIds.None)));

    private static string Describe(RecordCommandResult? failure) =>
        failure is null ? "" : $"{failure.Outcome} {string.Join("; ", failure.Errors?.Select(error => $"{error.Key}: {string.Join(", ", error.Value)}") ?? [])}";

    // A refusal of a body is a 400 with problem details, never another status.
    private static void AssertBadRequest(IResult result)
    {
        var status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;
        if (status != (int)HttpStatusCode.BadRequest)
        {
            throw new XunitException($"The refusal answered {status}, not 400.");
        }

        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        var problem = Assert.IsAssignableFrom<ProblemDetails>(value);
        if (problem is not HttpValidationProblemDetails { Errors.Count: > 0 })
        {
            throw new XunitException("The 400 holds no errors by pointer.");
        }
    }
}
