using System.Data;
using Axis.Rules.Application;
using Axis.Rules.Application.Search;
using Axis.Rules.Domain;
using Axis.Rules.Infrastructure.Persistence;
using Axis.Shared.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Rules.Infrastructure.Search;

internal sealed class PostgresRuleCatalogSearchProvider(RulesDbContext context)
    : IRuleCatalogSearchProvider
{
    public async Task<RuleCatalogSearchPage> SearchAsync(
        Guid workspaceId,
        IReadOnlyList<RuleTextSearchDocument> builtInDocuments,
        bool includeWorkspace,
        RuleLifecycleStatus? lifecycleStatus,
        RuleDefinitionSortField? sortBy,
        CollectionSortDirection? sortDirection,
        int skip,
        int take,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new RuleCatalogSearchPage([], 0);

        (string pageOrder, string resultOrder) = SortOrder(sortBy, sortDirection);

        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using NpgsqlCommand command =
                ((NpgsqlConnection)context.Database.GetDbConnection()).CreateCommand();
            command.CommandText =
                $$"""
                WITH built_in_documents AS (
                    SELECT
                        'BuiltIn'::text AS origin,
                        document.key,
                        document.title AS sort_title,
                        document.status AS sort_status,
                        1::integer AS sort_active_version,
                        1::integer AS sort_latest_version,
                        NULL::integer AS sort_revision,
                        'Axis built-in catalog'::text AS sort_created_by,
                        NULL::timestamp with time zone AS sort_created_at,
                        'Axis built-in catalog'::text AS sort_modified_by,
                        NULL::timestamp with time zone AS sort_modified_at,
                        axis_unaccent(lower(document.title)) AS title,
                        axis_unaccent(lower(document.title || ' ' || document.content)) AS content
                    FROM unnest(
                        @built_in_keys::text[],
                        @built_in_titles::text[],
                        @built_in_contents::text[],
                        @built_in_statuses::text[])
                        AS document(key, title, content, status)
                ),
                workspace_documents AS (
                    SELECT
                        'Workspace'::text AS origin,
                        definition_key AS key,
                        name AS sort_title,
                        CASE
                            WHEN archived_at IS NOT NULL THEN 'Archived'
                            WHEN active_version IS NOT NULL THEN 'Active'
                            WHEN latest_published_version IS NOT NULL THEN 'Inactive'
                            ELSE 'Draft'
                        END AS sort_status,
                        active_version AS sort_active_version,
                        latest_published_version AS sort_latest_version,
                        revision AS sort_revision,
                        created_by_actor_display_name AS sort_created_by,
                        created_at AS sort_created_at,
                        updated_by_actor_display_name AS sort_modified_by,
                        updated_at AS sort_modified_at,
                        search_title AS title,
                        search_text AS content
                    FROM rule_definitions
                    WHERE
                        @include_workspace
                        AND workspace_id = @workspace_id
                        AND (
                            @lifecycle_status IS NULL
                            OR (@lifecycle_status = 'Draft'
                                AND archived_at IS NULL
                                AND latest_published_version IS NULL)
                            OR (@lifecycle_status = 'Inactive'
                                AND archived_at IS NULL
                                AND latest_published_version IS NOT NULL
                                AND active_version IS NULL)
                            OR (@lifecycle_status = 'Active'
                                AND archived_at IS NULL
                                AND active_version IS NOT NULL)
                            OR (@lifecycle_status = 'Archived'
                                AND archived_at IS NOT NULL)
                        )
                ),
                documents AS (
                    SELECT * FROM built_in_documents
                    UNION ALL
                    SELECT * FROM workspace_documents
                ),
                search_query AS (
                    SELECT
                        axis_unaccent(lower(@query)) AS text,
                        websearch_to_tsquery('simple', axis_unaccent(lower(@query))) AS terms
                ),
                matches AS (
                    SELECT
                        documents.origin,
                        documents.key,
                        documents.sort_title,
                        documents.sort_status,
                        documents.sort_active_version,
                        documents.sort_latest_version,
                        documents.sort_revision,
                        documents.sort_created_by,
                        documents.sort_created_at,
                        documents.sort_modified_by,
                        documents.sort_modified_at,
                        (
                            CASE WHEN documents.title = search_query.text THEN 8.0 ELSE 0.0 END
                            + CASE WHEN documents.title LIKE search_query.text || '%' THEN 4.0 ELSE 0.0 END
                            + ts_rank_cd(
                                to_tsvector('simple', documents.content),
                                search_query.terms) * 4.0
                            + strict_word_similarity(search_query.text, documents.content) * 2.0
                            + similarity(search_query.text, documents.title)
                        )::double precision AS relevance
                    FROM documents
                    CROSS JOIN search_query
                    WHERE
                        documents.title = search_query.text
                        OR documents.title LIKE search_query.text || '%'
                        OR to_tsvector('simple', documents.content) @@ search_query.terms
                        OR documents.title % search_query.text
                        OR documents.content LIKE '%' || search_query.text || '%'
                        OR strict_word_similarity(search_query.text, documents.content) >= 0.35
                ),
                page AS (
                    SELECT origin, key, sort_title, sort_status,
                        sort_active_version, sort_latest_version, sort_revision,
                        sort_created_by, sort_created_at, sort_modified_by, sort_modified_at,
                        relevance
                    FROM matches
                    ORDER BY {{pageOrder}}
                    OFFSET @skip
                    LIMIT @take
                )
                SELECT
                    (SELECT count(*)::integer FROM matches) AS total_count,
                    page.origin,
                    page.key
                FROM (SELECT 1) AS anchor
                LEFT JOIN page ON true
                ORDER BY {{resultOrder}};
                """;
            command.Parameters.Add(
                new NpgsqlParameter<string[]>("built_in_keys", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    TypedValue = builtInDocuments.Select(document => document.Key).ToArray(),
                });
            command.Parameters.Add(
                new NpgsqlParameter<string[]>("built_in_titles", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    TypedValue = builtInDocuments.Select(document => document.Title).ToArray(),
                });
            command.Parameters.Add(
                new NpgsqlParameter<string[]>("built_in_contents", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    TypedValue = builtInDocuments.Select(document => document.Content).ToArray(),
                });
            command.Parameters.Add(
                new NpgsqlParameter<string[]>("built_in_statuses", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    TypedValue = builtInDocuments.Select(document => document.Status).ToArray(),
                });
            command.Parameters.AddWithValue("include_workspace", NpgsqlDbType.Boolean, includeWorkspace);
            command.Parameters.AddWithValue("workspace_id", NpgsqlDbType.Uuid, workspaceId);
            command.Parameters.Add(
                new NpgsqlParameter<string?>("lifecycle_status", NpgsqlDbType.Text)
                {
                    TypedValue = lifecycleStatus?.ToString(),
                });
            command.Parameters.AddWithValue("skip", NpgsqlDbType.Integer, skip);
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, take);
            command.Parameters.AddWithValue("query", NpgsqlDbType.Text, query.Trim());

            List<RuleCatalogSearchItem> items = [];
            int totalCount = 0;
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                totalCount = reader.GetInt32(0);
                if (await reader.IsDBNullAsync(1, cancellationToken))
                    continue;

                items.Add(new RuleCatalogSearchItem(
                    Enum.Parse<RuleOrigin>(reader.GetString(1)),
                    reader.GetString(2)));
            }

            return new RuleCatalogSearchPage(items, totalCount);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static (string PageOrder, string ResultOrder) SortOrder(
        RuleDefinitionSortField? sortBy,
        CollectionSortDirection? sortDirection)
    {
        if ((sortBy is null) != (sortDirection is null))
            throw new ArgumentException("A rule definition sort field and direction must be provided together.");

        return (sortBy, sortDirection) switch
        {
            (null, null) => (
                "relevance DESC, origin ASC, key ASC",
                "page.relevance DESC, page.origin ASC, page.key ASC"),
            (RuleDefinitionSortField.Name, CollectionSortDirection.Ascending) => (
                "sort_title COLLATE \"C\" ASC, key ASC, origin ASC",
                "page.sort_title COLLATE \"C\" ASC, page.key ASC, page.origin ASC"),
            (RuleDefinitionSortField.Name, CollectionSortDirection.Descending) => (
                "sort_title COLLATE \"C\" DESC, key ASC, origin ASC",
                "page.sort_title COLLATE \"C\" DESC, page.key ASC, page.origin ASC"),
            (RuleDefinitionSortField.Origin, CollectionSortDirection.Ascending) => (
                "origin ASC, sort_title COLLATE \"C\" ASC, key ASC",
                "page.origin ASC, page.sort_title COLLATE \"C\" ASC, page.key ASC"),
            (RuleDefinitionSortField.Origin, CollectionSortDirection.Descending) => (
                "origin DESC, sort_title COLLATE \"C\" ASC, key ASC",
                "page.origin DESC, page.sort_title COLLATE \"C\" ASC, page.key ASC"),
            (RuleDefinitionSortField.Status, CollectionSortDirection.Ascending) => (
                "sort_status COLLATE \"C\" ASC, sort_title COLLATE \"C\" ASC, key ASC, origin ASC",
                "page.sort_status COLLATE \"C\" ASC, page.sort_title COLLATE \"C\" ASC, page.key ASC, page.origin ASC"),
            (RuleDefinitionSortField.Status, CollectionSortDirection.Descending) => (
                "sort_status COLLATE \"C\" DESC, sort_title COLLATE \"C\" ASC, key ASC, origin ASC",
                "page.sort_status COLLATE \"C\" DESC, page.sort_title COLLATE \"C\" ASC, page.key ASC, page.origin ASC"),
            (RuleDefinitionSortField.ActiveVersion, CollectionSortDirection.Ascending) => (
                "sort_active_version ASC NULLS LAST, sort_title COLLATE \"C\" ASC, key ASC, origin ASC",
                "page.sort_active_version ASC NULLS LAST, page.sort_title COLLATE \"C\" ASC, page.key ASC, page.origin ASC"),
            (RuleDefinitionSortField.ActiveVersion, CollectionSortDirection.Descending) => (
                "sort_active_version DESC NULLS LAST, sort_title COLLATE \"C\" ASC, key ASC, origin ASC",
                "page.sort_active_version DESC NULLS LAST, page.sort_title COLLATE \"C\" ASC, page.key ASC, page.origin ASC"),
            (RuleDefinitionSortField.LatestVersion, CollectionSortDirection.Ascending) => (
                "sort_latest_version ASC NULLS LAST, sort_title COLLATE \"C\" ASC, key ASC, origin ASC",
                "page.sort_latest_version ASC NULLS LAST, page.sort_title COLLATE \"C\" ASC, page.key ASC, page.origin ASC"),
            (RuleDefinitionSortField.LatestVersion, CollectionSortDirection.Descending) => (
                "sort_latest_version DESC NULLS LAST, sort_title COLLATE \"C\" ASC, key ASC, origin ASC",
                "page.sort_latest_version DESC NULLS LAST, page.sort_title COLLATE \"C\" ASC, page.key ASC, page.origin ASC"),
            (RuleDefinitionSortField.Revision, CollectionSortDirection.Ascending) => (
                "sort_revision ASC NULLS LAST, sort_title COLLATE \"C\" ASC, key ASC, origin ASC",
                "page.sort_revision ASC NULLS LAST, page.sort_title COLLATE \"C\" ASC, page.key ASC, page.origin ASC"),
            (RuleDefinitionSortField.Revision, CollectionSortDirection.Descending) => (
                "sort_revision DESC NULLS LAST, sort_title COLLATE \"C\" ASC, key ASC, origin ASC",
                "page.sort_revision DESC NULLS LAST, page.sort_title COLLATE \"C\" ASC, page.key ASC, page.origin ASC"),
            (RuleDefinitionSortField.CreatedBy, CollectionSortDirection.Ascending) => (
                "sort_created_by COLLATE \"C\" ASC NULLS LAST, sort_title COLLATE \"C\" ASC, key ASC, origin ASC",
                "page.sort_created_by COLLATE \"C\" ASC NULLS LAST, page.sort_title COLLATE \"C\" ASC, page.key ASC, page.origin ASC"),
            (RuleDefinitionSortField.CreatedBy, CollectionSortDirection.Descending) => (
                "sort_created_by COLLATE \"C\" DESC NULLS LAST, sort_title COLLATE \"C\" ASC, key ASC, origin ASC",
                "page.sort_created_by COLLATE \"C\" DESC NULLS LAST, page.sort_title COLLATE \"C\" ASC, page.key ASC, page.origin ASC"),
            (RuleDefinitionSortField.CreatedAt, CollectionSortDirection.Ascending) => (
                "sort_created_at ASC NULLS LAST, sort_title COLLATE \"C\" ASC, key ASC, origin ASC",
                "page.sort_created_at ASC NULLS LAST, page.sort_title COLLATE \"C\" ASC, page.key ASC, page.origin ASC"),
            (RuleDefinitionSortField.CreatedAt, CollectionSortDirection.Descending) => (
                "sort_created_at DESC NULLS LAST, sort_title COLLATE \"C\" ASC, key ASC, origin ASC",
                "page.sort_created_at DESC NULLS LAST, page.sort_title COLLATE \"C\" ASC, page.key ASC, page.origin ASC"),
            (RuleDefinitionSortField.ModifiedBy, CollectionSortDirection.Ascending) => (
                "sort_modified_by COLLATE \"C\" ASC NULLS LAST, sort_title COLLATE \"C\" ASC, key ASC, origin ASC",
                "page.sort_modified_by COLLATE \"C\" ASC NULLS LAST, page.sort_title COLLATE \"C\" ASC, page.key ASC, page.origin ASC"),
            (RuleDefinitionSortField.ModifiedBy, CollectionSortDirection.Descending) => (
                "sort_modified_by COLLATE \"C\" DESC NULLS LAST, sort_title COLLATE \"C\" ASC, key ASC, origin ASC",
                "page.sort_modified_by COLLATE \"C\" DESC NULLS LAST, page.sort_title COLLATE \"C\" ASC, page.key ASC, page.origin ASC"),
            (RuleDefinitionSortField.ModifiedAt, CollectionSortDirection.Ascending) => (
                "sort_modified_at ASC NULLS LAST, sort_title COLLATE \"C\" ASC, key ASC, origin ASC",
                "page.sort_modified_at ASC NULLS LAST, page.sort_title COLLATE \"C\" ASC, page.key ASC, page.origin ASC"),
            (RuleDefinitionSortField.ModifiedAt, CollectionSortDirection.Descending) => (
                "sort_modified_at DESC NULLS LAST, sort_title COLLATE \"C\" ASC, key ASC, origin ASC",
                "page.sort_modified_at DESC NULLS LAST, page.sort_title COLLATE \"C\" ASC, page.key ASC, page.origin ASC"),
            _ => throw new ArgumentOutOfRangeException(nameof(sortBy)),
        };
    }
}
