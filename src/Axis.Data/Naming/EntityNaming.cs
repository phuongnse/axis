using System.Security.Cryptography;
using System.Text;

namespace Axis.Data.Naming;

/// <summary>
/// Physical names of entity tables, derived from entity ids and field names, never from labels.
/// Every name fits PostgreSQL's 63-byte identifier limit by construction: entity and field names
/// are at most 60 ASCII characters, ids are 32 hex characters and constraint names hash the column.
/// </summary>
public static class EntityNaming
{
    /// <summary>The schema that holds every entity table, separate from the <c>axis</c> system tables.</summary>
    public const string Schema = "entities";

    /// <summary>The primary key column of every entity table.</summary>
    public const string IdColumn = "id";

    /// <summary>The row version column of every entity table except a child table, starting at 1 for each row.</summary>
    public const string VersionColumn = "version";

    /// <summary>The owner record's id in a child table, with a foreign key that deletes the row with its owner.</summary>
    public const string OwnerColumn = "owner_id";

    /// <summary>The row's order in its child collection, the zero-based index in the array.</summary>
    public const string PositionColumn = "position";

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

    /// <summary>Quotes an identifier for SQL, doubling any embedded double quote.</summary>
    public static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>The quoted, schema-qualified name of a table.</summary>
    public static string QualifiedTable(string table) => Quote(Schema) + "." + Quote(table);

    // The first 16 lowercase hex characters of SHA-256 over the UTF-8 column name.
    private static string Hash(string column) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(column)))[..16];
}
