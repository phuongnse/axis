using Axis.Objects.Application;
using Axis.Objects.Application.Queries.GetObjectDefinition;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Objects.Application.Tests.Queries;

public sealed class GetObjectDefinitionHandlerTests
{
    private readonly ObjectDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task GetObjectDefinition_WhenRepositoryReturnsNull_ReturnsNotFound()
    {
        GetObjectDefinitionHandler sut = new(_context.CurrentUser, _context.Repository);

        Result<ObjectDefinitionDetailDto> result = await sut.Handle(
            new GetObjectDefinitionQuery(Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ProblemCode.Should().Be(ObjectsProblemCodes.ObjectDefinitionNotFound);
    }
}
