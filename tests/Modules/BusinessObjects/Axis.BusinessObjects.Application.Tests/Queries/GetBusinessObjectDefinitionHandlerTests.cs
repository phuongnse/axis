using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Application.Queries.GetBusinessObjectDefinition;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.BusinessObjects.Application.Tests.Queries;

public sealed class GetBusinessObjectDefinitionHandlerTests
{
    private readonly BusinessObjectDefinitionHandlerTestContext _context = new();

    [Fact]
    public async Task GetBusinessObjectDefinition_WhenRepositoryReturnsNull_ReturnsNotFound()
    {
        GetBusinessObjectDefinitionHandler sut = new(_context.CurrentUser, _context.Repository);

        Result<BusinessObjectDefinitionDetailDto> result = await sut.Handle(
            new GetBusinessObjectDefinitionQuery(Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ProblemCode.Should().Be(BusinessObjectsProblemCodes.BusinessObjectDefinitionNotFound);
    }
}
