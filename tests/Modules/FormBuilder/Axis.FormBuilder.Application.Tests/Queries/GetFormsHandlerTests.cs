using Axis.FormBuilder.Application.Queries.GetForms;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.Shared.Application;
using FluentAssertions;
using NSubstitute;

namespace Axis.FormBuilder.Application.Tests.Queries;

public class GetFormsHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly IFormRepository _repo = Substitute.For<IFormRepository>();
    private readonly GetFormsHandler _handler;

    public GetFormsHandlerTests() => _handler = new GetFormsHandler(_repo);

    [Fact]
    public async Task Handle_WhenFormsExist_ReturnsPagedSummaryList()
    {
        FormDefinition a = FormDefinition.Create("Alpha Form", null, TenantId, "user");
        FormDefinition b = FormDefinition.Create("Beta Form", "desc", TenantId, "user");
        _repo.GetAllAsync(TenantId, Arg.Any<CancellationToken>()).Returns([a, b]);

        PagedResult<FormSummaryDto> result = await _handler.Handle(
            new GetFormsQuery(TenantId, Page: 1, PageSize: 20), CancellationToken.None);

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items.Should().Contain(d => d.Name == "Alpha Form");
        result.Items.Should().Contain(d => d.Name == "Beta Form" && d.Description == "desc");
    }

    [Fact]
    public async Task Handle_WhenNoForms_ReturnsEmptyPage()
    {
        _repo.GetAllAsync(TenantId, Arg.Any<CancellationToken>()).Returns([]);

        PagedResult<FormSummaryDto> result = await _handler.Handle(
            new GetFormsQuery(TenantId, Page: 1, PageSize: 20), CancellationToken.None);

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenPageSizeExceedsCap_CapsAt100()
    {
        FormDefinition[] forms = Enumerable.Range(1, 5)
            .Select(i => FormDefinition.Create($"Form {i}", null, TenantId, "user"))
            .ToArray();
        _repo.GetAllAsync(TenantId, Arg.Any<CancellationToken>()).Returns(forms);

        PagedResult<FormSummaryDto> result = await _handler.Handle(
            new GetFormsQuery(TenantId, Page: 1, PageSize: 200), CancellationToken.None);

        result.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task Handle_WhenPageIsTwo_ReturnsSecondPage()
    {
        FormDefinition[] forms = Enumerable.Range(1, 3)
            .Select(i => FormDefinition.Create($"Form {i}", null, TenantId, "user"))
            .ToArray();
        _repo.GetAllAsync(TenantId, Arg.Any<CancellationToken>()).Returns(forms);

        PagedResult<FormSummaryDto> result = await _handler.Handle(
            new GetFormsQuery(TenantId, Page: 2, PageSize: 2), CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Handle_WhenPageIsZeroOrNegative_ClampsToPageOne()
    {
        FormDefinition form = FormDefinition.Create("Form", null, TenantId, "user");
        _repo.GetAllAsync(TenantId, Arg.Any<CancellationToken>()).Returns([form]);

        PagedResult<FormSummaryDto> result = await _handler.Handle(
            new GetFormsQuery(TenantId, Page: -5, PageSize: 10), CancellationToken.None);

        result.Page.Should().Be(1);
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WhenPageSizeIsZeroOrNegative_ClampsToOne()
    {
        FormDefinition[] forms = Enumerable.Range(1, 3)
            .Select(i => FormDefinition.Create($"Form {i}", null, TenantId, "user"))
            .ToArray();
        _repo.GetAllAsync(TenantId, Arg.Any<CancellationToken>()).Returns(forms);

        PagedResult<FormSummaryDto> result = await _handler.Handle(
            new GetFormsQuery(TenantId, Page: 1, PageSize: -1), CancellationToken.None);

        result.PageSize.Should().Be(1);
        result.Items.Should().HaveCount(1);
    }
}
