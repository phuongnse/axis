using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetTenantSlugPreview;

public sealed record GetTenantSlugPreviewQuery(string TenantName) : IQuery<TenantSlugPreviewDto>;

public sealed record TenantSlugPreviewDto(string Slug);

public sealed class GetTenantSlugPreviewHandler(ITenantSlugGenerator slugGenerator)
    : IQueryHandler<GetTenantSlugPreviewQuery, TenantSlugPreviewDto>
{
    public Task<TenantSlugPreviewDto> Handle(
        GetTenantSlugPreviewQuery query,
        CancellationToken cancellationToken)
    {
        string slug = slugGenerator.GenerateBaseSlug(query.TenantName);
        if (string.IsNullOrWhiteSpace(slug))
            slug = "tenant";

        return Task.FromResult(new TenantSlugPreviewDto(slug));
    }
}
