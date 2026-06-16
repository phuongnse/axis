using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetWorkspaceSlugPreview;

public sealed record GetWorkspaceSlugPreviewQuery(string WorkspaceName) : IQuery<WorkspaceSlugPreviewDto>;

public sealed record WorkspaceSlugPreviewDto(string Slug);

public sealed class GetWorkspaceSlugPreviewHandler(IWorkspaceSlugGenerator slugGenerator)
    : IQueryHandler<GetWorkspaceSlugPreviewQuery, WorkspaceSlugPreviewDto>
{
    public Task<WorkspaceSlugPreviewDto> Handle(
        GetWorkspaceSlugPreviewQuery query,
        CancellationToken cancellationToken)
    {
        string slug = slugGenerator.GenerateBaseSlug(query.WorkspaceName);
        if (string.IsNullOrWhiteSpace(slug))
            slug = "workspace";

        return Task.FromResult(new WorkspaceSlugPreviewDto(slug));
    }
}
