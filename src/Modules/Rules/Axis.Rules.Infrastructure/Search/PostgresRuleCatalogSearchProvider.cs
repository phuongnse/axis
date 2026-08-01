using System.Data;
using Axis.Rules.Application.Search;
using Axis.Rules.Domain;
using Axis.Rules.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Rules.Infrastructure.Search;

internal sealed class PostgresRuleCatalogSearchProvider(RulesDbContext context)
    : IRuleCatalogSearchProvider
{
    public async Task<RuleCatalogSearchPage> SearchAsync(
        Guid workspaceId,
        IReadOnlyList<RuleTextSearchDocument> systemDocuments,
        bool includeWorkspace,
        RuleLifecycleStatus? workspaceStatus,
        int skip,
        int take,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new RuleCatalogSearchPage([], 0);

        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using NpgsqlCommand command =
                ((NpgsqlConnection)context.Database.GetDbConnection()).CreateCommand();
            command.CommandText =
                """
                WITH system_documents AS (
                    SELECT
                        'System'::text AS origin,
                        document.key,
                        axis_unaccent(lower(document.title)) AS title,
                        axis_unaccent(lower(document.title || ' ' || document.content)) AS content
                    FROM unnest(@system_keys::text[], @system_titles::text[], @system_contents::text[])
                        AS document(key, title, content)
                ),
                workspace_documents AS (
                    SELECT
                        'Workspace'::text AS origin,
                        definition_key AS key,
                        search_title AS title,
                        search_text AS content
                    FROM rule_definitions
                    WHERE
                        @include_workspace
                        AND workspace_id = @workspace_id
                        AND (@status IS NULL OR status = @status)
                ),
                documents AS (
                    SELECT * FROM system_documents
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
                    SELECT origin, key, relevance
                    FROM matches
                    ORDER BY relevance DESC, origin ASC, key ASC
                    OFFSET @skip
                    LIMIT @take
                )
                SELECT
                    (SELECT count(*)::integer FROM matches) AS total_count,
                    page.origin,
                    page.key
                FROM (SELECT 1) AS anchor
                LEFT JOIN page ON true
                ORDER BY page.relevance DESC, page.origin ASC, page.key ASC;
                """;
            command.Parameters.Add(
                new NpgsqlParameter<string[]>("system_keys", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    TypedValue = systemDocuments.Select(document => document.Key).ToArray(),
                });
            command.Parameters.Add(
                new NpgsqlParameter<string[]>("system_titles", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    TypedValue = systemDocuments.Select(document => document.Title).ToArray(),
                });
            command.Parameters.Add(
                new NpgsqlParameter<string[]>("system_contents", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    TypedValue = systemDocuments.Select(document => document.Content).ToArray(),
                });
            command.Parameters.AddWithValue("include_workspace", NpgsqlDbType.Boolean, includeWorkspace);
            command.Parameters.AddWithValue("workspace_id", NpgsqlDbType.Uuid, workspaceId);
            command.Parameters.Add(
                new NpgsqlParameter<string?>("status", NpgsqlDbType.Text)
                {
                    TypedValue = workspaceStatus?.ToString(),
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
}
