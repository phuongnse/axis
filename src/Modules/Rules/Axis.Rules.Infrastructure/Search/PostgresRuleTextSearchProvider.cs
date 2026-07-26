using System.Data;
using Axis.Rules.Application.Search;
using Axis.Rules.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Rules.Infrastructure.Search;

internal sealed class PostgresRuleTextSearchProvider(RulesDbContext context)
    : IRuleTextSearchProvider
{
    public async Task<IReadOnlyList<RuleTextSearchMatch>> SearchAsync(
        IReadOnlyList<RuleTextSearchDocument> documents,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (documents.Count == 0 || string.IsNullOrWhiteSpace(query))
            return [];

        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using NpgsqlCommand command =
                ((NpgsqlConnection)context.Database.GetDbConnection()).CreateCommand();
            command.CommandText =
                """
                WITH documents AS (
                    SELECT *
                    FROM unnest(@keys::text[], @titles::text[], @contents::text[])
                        AS document(key, title, content)
                ),
                normalized AS (
                    SELECT
                        key,
                        axis_unaccent(lower(title)) AS title,
                        axis_unaccent(lower(title || ' ' || content)) AS content
                    FROM documents
                ),
                search_query AS (
                    SELECT
                        axis_unaccent(lower(@query)) AS text,
                        websearch_to_tsquery('simple', axis_unaccent(lower(@query))) AS terms
                ),
                ranked AS (
                    SELECT
                        normalized.key,
                        (
                            CASE WHEN normalized.title = search_query.text THEN 8.0 ELSE 0.0 END
                            + CASE WHEN normalized.title LIKE search_query.text || '%' THEN 4.0 ELSE 0.0 END
                            + ts_rank_cd(
                                to_tsvector('simple', normalized.content),
                                search_query.terms) * 4.0
                            + strict_word_similarity(search_query.text, normalized.content) * 2.0
                            + similarity(search_query.text, normalized.title)
                        )::double precision AS relevance
                    FROM normalized
                    CROSS JOIN search_query
                    WHERE
                        normalized.title = search_query.text
                        OR normalized.title LIKE search_query.text || '%'
                        OR to_tsvector('simple', normalized.content) @@ search_query.terms
                        OR normalized.title % search_query.text
                        OR normalized.content LIKE '%' || search_query.text || '%'
                        OR strict_word_similarity(search_query.text, normalized.content) >= 0.35
                )
                SELECT key, relevance
                FROM ranked
                ORDER BY relevance DESC, key ASC;
                """;
            command.Parameters.Add(
                new NpgsqlParameter<string[]>("keys", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    TypedValue = documents.Select(document => document.Key).ToArray(),
                });
            command.Parameters.Add(
                new NpgsqlParameter<string[]>("titles", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    TypedValue = documents.Select(document => document.Title).ToArray(),
                });
            command.Parameters.Add(
                new NpgsqlParameter<string[]>("contents", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    TypedValue = documents.Select(document => document.Content).ToArray(),
                });
            command.Parameters.Add(
                new NpgsqlParameter<string>("query", NpgsqlDbType.Text)
                {
                    TypedValue = query.Trim(),
                });

            List<RuleTextSearchMatch> matches = [];
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                matches.Add(new RuleTextSearchMatch(
                    reader.GetString(0),
                    reader.GetDouble(1)));
            }

            return matches;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
