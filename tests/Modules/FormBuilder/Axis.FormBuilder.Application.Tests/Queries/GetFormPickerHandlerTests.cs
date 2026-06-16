using Axis.FormBuilder.Application.Queries.GetFormPicker;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.FormBuilder.Application.Tests.Queries;

public class GetFormPickerHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly IFormRepository _formRepo = Substitute.For<IFormRepository>();

    private GetFormPickerHandler CreateHandler() => new(_formRepo);

    [Fact]
    public async Task Handle_WhenFormsExist_ReturnsAlphabetizedPickerItems()
    {
        FormDefinition beta = FormDefinition.Create("Beta Form", null, TenantId, "user-123");
        FormDefinition alpha = FormDefinition.Create("Alpha Form", null, TenantId, "user-123");
        alpha.AddField("name", "Name", FormFieldType.Text, true, null);
        _formRepo.GetAllAsync(TenantId, Arg.Any<CancellationToken>()).Returns([beta, alpha]);

        IReadOnlyList<GetFormPickerDto> result = await CreateHandler().Handle(
            new GetFormPickerQuery(TenantId),
            CancellationToken.None);

        result.Select(item => item.Name).Should().Equal("Alpha Form", "Beta Form");
        result[0].FieldCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenNoFormsExist_ReturnsEmptyList()
    {
        _formRepo.GetAllAsync(TenantId, Arg.Any<CancellationToken>()).Returns([]);

        IReadOnlyList<GetFormPickerDto> result = await CreateHandler().Handle(
            new GetFormPickerQuery(TenantId),
            CancellationToken.None);

        result.Should().BeEmpty();
    }
}
