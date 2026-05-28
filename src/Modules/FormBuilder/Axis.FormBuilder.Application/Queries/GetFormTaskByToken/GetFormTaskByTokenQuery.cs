using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Queries.GetFormTaskByToken;

/// <summary>Load form definition for a standalone submission page (public, token-based).</summary>
public sealed record GetFormTaskByTokenQuery(Guid AccessToken) : IQuery<FormTaskByTokenDto?>;
