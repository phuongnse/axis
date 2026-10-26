using System.Security.Cryptography;
using System.Text;

namespace Axis.Data.Naming;

/// <summary>
/// Physical names of entity tables, derived from entity ids and field names, never from labels.
/// Every name fits PostgreSQL's 63-byte identifier limit by construction: entity and field names
/// are at most 60 ASCII characters, ids are 32 hex characters and constraint and index names hash the column or columns.
/// A junction table's own constraint names use a shorter hash, because its name is longer.
/// </summary>
public static class EntityNaming
{
    /// <summary>The schema that holds every entity table, separate from the <c>axis</c> system tables.</summary>
    public const string Schema = "entities";

    /// <summary>The primary key column of every entity table.</summary>
    public const string IdColumn = "id";

    /// <summary>The row version column of every entity table except a child table, starting at 1 for each row.</summary>
    public const string VersionColumn = "version";

    /// <summary>
    /// The nullable column of every entity table except a child table that holds the id of the release
    /// that computed the row's stored computed values. A row written before the column existed is null.
    /// </summary>
    public const string ComputedReleaseColumn = "computed_release_id";

    /// <summary>The owner record's id in a child table, with a foreign key that deletes the row with its owner.</summary>
    public const string OwnerColumn = "owner_id";

    /// <summary>The row's order in its child collection, the zero-based index in the array.</summary>
    public const string PositionColumn = "position";

    /// <summary>The linked record's id in a junction table, with a foreign key that refuses a delete of the target.</summary>
    public const string TargetColumn = "target_id";

    /// <summary>The prefix of every index the planner owns. The planner drops an index with this prefix that the release does not declare.</summary>
    public const string IndexPrefix = "ix_";

    /// <summary><c>e_</c> and the entity id as 32 lowercase hex characters (34 characters).</summary>
    public static string Table(Guid entityId) => "e_" + entityId.ToString("N");

    /// <summary><c>f_</c> and the field name in lowercase (at most 62 characters).</summary>
    public static string Column(string fieldName) => "f_" + fieldName.ToLowerInvariant();

    /// <summary><c>pk_</c> and the table name (37 characters).</summary>
    public static string PrimaryKey(string table) => "pk_" + table;

    /// <summary><c>uq_</c>, the table name and a hash of the column name (54 characters).</summary>
    public static string Unique(string table, string column) => $"uq_{table}_{Hash(column)}";

    /// <summary><c>fk_</c>, the table name and a hash of the column name (54 characters).</summary>
    public static string ForeignKey(string table, string column) => $"fk_{table}_{Hash(column)}";

    /// <summary><c>ix_</c>, the table name and a hash of the comma-joined column names, in index order (54 characters).</summary>
    public static string Index(string table, IReadOnlyList<string> columns) => $"{IndexPrefix}{table}_{Hash(string.Join(',', columns))}";

    /// <summary><c>m_</c>, the entity id, <c>_</c> and a hash of the lowercase field name (51 characters).</summary>
    public static string Junction(Guid entityId, string fieldName) => $"m_{entityId:N}_{Hash(fieldName.ToLowerInvariant())}";

    /// <summary><c>m_</c>, the entity id and <c>_</c>, the start of the name of every junction table of the entity (36 characters).</summary>
    public static string JunctionPrefix(Guid entityId) => $"m_{entityId:N}_";

    /// <summary><c>fk_</c>, the junction table name and the first 8 hex characters of the column hash (63 characters).</summary>
    public static string JunctionForeignKey(string junction, string column) => $"fk_{junction}_{Hash(column)[..8]}";

    /// <summary><c>ix_</c>, the junction table name and the first 8 hex characters of the column hash (63 characters).</summary>
    public static string JunctionIndex(string junction, string column) => $"ix_{junction}_{Hash(column)[..8]}";

    /// <summary>Quotes an identifier for SQL, doubling any embedded double quote.</summary>
    public static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>The quoted, schema-qualified name of a table.</summary>
    public static string QualifiedTable(string table) => Quote(Schema) + "." + Quote(table);

    // The first 16 lowercase hex characters of SHA-256 over the UTF-8 text.
    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
}
