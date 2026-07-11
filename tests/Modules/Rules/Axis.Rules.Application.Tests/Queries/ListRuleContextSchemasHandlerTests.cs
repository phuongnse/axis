using Axis.Rules.Application.Queries.ListRuleContextSchemas;
using Axis.Rules.Contracts;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Rules.Application.Tests.Queries;

public sealed class ListRuleContextSchemasHandlerTests
{
    private readonly RuleDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task List_WhenWorkspaceExists_ReturnsProviderSchemas()
    {
        ListRuleContextSchemasHandler sut = new(_context.CurrentUser, _context.ContextRegistry);

        Result<IReadOnlyList<RuleContextSchemaDto>> result = await sut.Handle(
            new ListRuleContextSchemasQuery(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(schema =>
            schema.ContextKey == RuleDefinitionHandlerTestContext.Schema.ContextKey);
    }
}
