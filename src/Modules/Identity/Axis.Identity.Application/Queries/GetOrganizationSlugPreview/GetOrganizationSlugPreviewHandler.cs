using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetOrganizationSlugPreview;

public sealed record GetOrganizationSlugPreviewQuery(string OrgName) : IQuery<OrganizationSlugPreviewDto>;

public sealed record OrganizationSlugPreviewDto(string Slug);

public sealed class GetOrganizationSlugPreviewHandler(IOrganizationSlugGenerator slugGenerator)
    : IQueryHandler<GetOrganizationSlugPreviewQuery, OrganizationSlugPreviewDto>
{
    public Task<OrganizationSlugPreviewDto> Handle(
        GetOrganizationSlugPreviewQuery query,
        CancellationToken cancellationToken)
    {
        string slug = slugGenerator.GenerateBaseSlug(query.OrgName);
        if (string.IsNullOrWhiteSpace(slug))
            slug = "organization";

        return Task.FromResult(new OrganizationSlugPreviewDto(slug));
    }
}
