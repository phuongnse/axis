using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetTeamAccountSlugPreview;

public sealed record GetTeamAccountSlugPreviewQuery(string TeamAccountName) : IQuery<TeamAccountSlugPreviewDto>;

public sealed record TeamAccountSlugPreviewDto(string Slug);

public sealed class GetTeamAccountSlugPreviewHandler(ITeamAccountSlugGenerator slugGenerator)
    : IQueryHandler<GetTeamAccountSlugPreviewQuery, TeamAccountSlugPreviewDto>
{
    public Task<TeamAccountSlugPreviewDto> Handle(
        GetTeamAccountSlugPreviewQuery query,
        CancellationToken cancellationToken)
    {
        string slug = slugGenerator.GenerateBaseSlug(query.TeamAccountName);
        if (string.IsNullOrWhiteSpace(slug))
            slug = "team-account";

        return Task.FromResult(new TeamAccountSlugPreviewDto(slug));
    }
}
