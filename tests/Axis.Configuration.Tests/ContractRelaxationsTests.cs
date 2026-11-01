using Axis.Configuration.Compilation;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Tests;

public sealed class ContractRelaxationsTests
{
    private static readonly ContractResource _full = new()
    {
        Id = Guid.Parse("5c1d3a0e-8a0f-4e57-9a43-1e0f6d0c2b71"),
        Kind = "contract",
        Name = "RequestContract",
        FormatVersion = 1,
        Entity = "Request",
        Lifecycle = new ContractLifecycleDefinition
        {
            Field = "status",
            States =
            [
                new LifecycleStateDefinition { Name = "draft", Initial = true, Deletable = true },
                new LifecycleStateDefinition { Name = "submitted" },
            ],
            Transitions =
            [
                new LifecycleTransitionDefinition { Name = "submit", From = ["draft"], To = "submitted", Guard = "amount > 0", Message = new TextReference("a.msg") },
                new LifecycleTransitionDefinition { Name = "approve", From = ["submitted"], To = "approved", Guard = "amount > 1" },
            ],
        },
        Validations =
        [
            new ContractValidationDefinition { Name = "positive", Expression = "amount >= 0", Message = new TextReference("a.positive"), Field = "amount" },
            new ContractValidationDefinition { Name = "titled", Expression = "title != ''", Message = new TextReference("a.titled"), Field = "title" },
        ],
        Effects =
        [
            new ContractEffectDefinition { Name = "recorded", On = "approve", Create = new ContractEffectCreateDefinition { Entity = "Entry" } },
            new ContractEffectDefinition { Name = "logged", On = "submit", Create = new ContractEffectCreateDefinition { Entity = "Log" } },
        ],
        Ceiling = new ContractCeilingDefinition
        {
            Slots = new Dictionary<string, IReadOnlyList<CeilingEntryDefinition>>
            {
                ["viewer"] = [new CeilingEntryDefinition { Actions = ["read"], Fields = ["title"], Filter = "amount > 10" }],
                ["editor"] = [new CeilingEntryDefinition { Actions = ["read", "update"] }],
            },
            Others = [new CeilingEntryDefinition { Actions = ["read"] }],
        },
    };

    /// <summary>
    /// What relaxing the first point of a kind, in ordinal order, must do to <see cref="_full"/>. A kind that has
    /// no assertion here fails the test that relaxes it, so a new kind of point cannot get past
    /// <see cref="ContractRelaxations.Apply"/> unhandled.
    /// </summary>
    private static readonly Dictionary<string, Action<ContractResource>> _expectations = new()
    {
        // Request/transitions/approve
        ["transitions"] = relaxed =>
        {
            var transitions = relaxed.Lifecycle!.Transitions;
            Assert.Equal("amount > 0", transitions[0].Guard);
            Assert.Null(transitions[1].Guard);
            Assert.Equal("submitted", transitions[1].From.Single());
        },
        // Request/states/submitted/initial
        ["states/initial"] = relaxed =>
        {
            var states = relaxed.Lifecycle!.States;
            Assert.True(states[1].Initial);
            Assert.NotEqual(true, states[1].Deletable);
            Assert.Equal("amount > 0", relaxed.Lifecycle.Transitions[0].Guard);
        },
        // Request/states/submitted/deletable
        ["states/deletable"] = relaxed =>
        {
            var states = relaxed.Lifecycle!.States;
            Assert.True(states[1].Deletable);
            Assert.NotEqual(true, states[1].Initial);
        },
        // Request/validations/positive
        ["validations"] = relaxed =>
        {
            Assert.Equal(["titled"], relaxed.Validations!.Select(validation => validation.Name));
            Assert.Equal(2, relaxed.Effects!.Count);
        },
        // Request/effects/logged
        ["effects"] = relaxed =>
        {
            Assert.Equal(["recorded"], relaxed.Effects!.Select(effect => effect.Name));
            Assert.Equal(2, relaxed.Validations!.Count);
        },
        // Request/ceiling/editor
        ["ceiling"] = relaxed =>
        {
            var editor = Assert.Single(relaxed.Ceiling!.Slots["editor"]);
            Assert.Equal(PolicyActions.Of("entity"), editor.Actions);
            Assert.Null(editor.Fields);
            Assert.Null(editor.Filter);
            var viewer = Assert.Single(relaxed.Ceiling.Slots["viewer"]);
            Assert.Equal(["read"], viewer.Actions);
            Assert.Equal("amount > 10", viewer.Filter);
            Assert.Equal(["read"], Assert.Single(relaxed.Ceiling.Others!).Actions);
        },
        // Request/ceiling/others
        ["ceiling/others"] = relaxed =>
        {
            var others = Assert.Single(relaxed.Ceiling!.Others!);
            Assert.Equal(PolicyActions.Of("entity"), others.Actions);
            Assert.Null(others.Fields);
            Assert.Null(others.Filter);
            Assert.Equal("amount > 10", Assert.Single(relaxed.Ceiling.Slots["viewer"]).Filter);
        },
    };

    [Fact]
    public void One_point_of_each_kind_that_the_contract_gives_is_relaxed_and_has_an_expectation()
    {
        var byKind = ContractPoints.Of([_full])
            .GroupBy(KindOf)
            .ToDictionary(group => group.Key, group => group.Order(StringComparer.Ordinal).First(), StringComparer.Ordinal);

        Assert.Equal(
            ["ceiling", "ceiling/others", "effects", "states/deletable", "states/initial", "transitions", "validations"],
            byKind.Keys.Order(StringComparer.Ordinal));
        foreach (var (kind, point) in byKind)
        {
            Assert.True(_expectations.TryGetValue(kind, out var expect), $"The kind '{kind}' of the point '{point}' has no expectation.");
            var relaxed = ContractRelaxations.Apply(_full, new HashSet<string> { point });
            Assert.NotSame(_full, relaxed);
            expect(relaxed);
        }
    }

    [Fact]
    public void An_empty_set_gives_the_same_contract()
    {
        Assert.Same(_full, ContractRelaxations.Apply(_full, new HashSet<string>()));
    }

    [Fact]
    public void A_point_of_another_entity_changes_nothing_even_when_the_name_starts_the_same()
    {
        var points = new HashSet<string> { "Claim/transitions/submit", "RequestLine/transitions/submit", "Request2/transitions/submit" };

        Assert.Same(_full, ContractRelaxations.Apply(_full, points));
    }

    [Fact]
    public void A_point_that_names_no_element_changes_nothing()
    {
        var relaxed = ContractRelaxations.Apply(_full, new HashSet<string> { "Request/transitions/missing", "Request/ceiling/missing" });

        Assert.Equal(_full.Lifecycle!.Transitions, relaxed.Lifecycle!.Transitions);
        Assert.Equal(_full.Lifecycle.States, relaxed.Lifecycle.States);
        Assert.Equal(_full.Validations, relaxed.Validations);
        Assert.Equal(_full.Effects, relaxed.Effects);
        Assert.Equal(_full.Ceiling!.Slots, relaxed.Ceiling!.Slots);
        Assert.Equal(_full.Ceiling.Others, relaxed.Ceiling.Others);
    }

    [Theory]
    [InlineData("Request/lifecycle/x")]
    [InlineData("Request/states/draft/other")]
    [InlineData("Request/states/draft")]
    [InlineData("Request/transitions")]
    public void A_point_of_an_unknown_kind_throws_and_names_the_point(string point)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ContractRelaxations.Apply(_full, new HashSet<string> { point }));

        Assert.Contains($"'{point}'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Relaxing_the_last_validation_or_effect_leaves_an_empty_list_and_the_original_is_not_changed()
    {
        var contract = _full with { Validations = [_full.Validations![0]], Effects = [_full.Effects![0]] };

        var relaxed = ContractRelaxations.Apply(contract, new HashSet<string> { "Request/validations/positive", "Request/effects/recorded" });

        Assert.Empty(relaxed.Validations!);
        Assert.Empty(relaxed.Effects!);
        Assert.Single(contract.Validations!);
        Assert.Single(contract.Effects!);
        Assert.Equal("amount > 0", _full.Lifecycle!.Transitions[0].Guard);
    }

    private static string KindOf(string point)
    {
        var parts = point[(point.IndexOf('/', StringComparison.Ordinal) + 1)..].Split('/');
        return parts[0] switch
        {
            "states" => $"states/{parts[^1]}",
            "ceiling" when parts[1] == "others" => "ceiling/others",
            _ => parts[0],
        };
    }
}
