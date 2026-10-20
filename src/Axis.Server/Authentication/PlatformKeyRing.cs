using System.Xml.Linq;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Npgsql;

namespace Axis.Server.Authentication;

/// <summary>
/// Keeps the ASP.NET data protection keys in <c>platform.data_protection_keys</c>, so every server replica and every
/// restart reads the same keys. Each key is encrypted with <c>Platform:SecretKey</c>, bound to the key's own id, so
/// the table holds no plain key material. The connection is the platform reader role, which may insert here and
/// nothing more. <see cref="IXmlRepository"/> is synchronous, so the calls are too.
/// </summary>
internal sealed class PlatformKeyRing(NpgsqlDataSource dataSource, TenantSecretCipher cipher) : IXmlRepository
{
    /// <summary>Reads and decrypts every stored key. A key that does not decrypt throws and names the setting.</summary>
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var command = dataSource.CreateCommand("SELECT id, xml FROM platform.data_protection_keys ORDER BY friendly_name");
        using var reader = command.ExecuteReader();

        var elements = new List<XElement>();
        while (reader.Read())
        {
            var id = reader.GetGuid(0);
            var xml = cipher.DecryptDataProtectionKey(id, (byte[])reader[1]);
            elements.Add(XElement.Parse(xml.Reveal()));
        }

        return elements;
    }

    /// <summary>
    /// Encrypts and inserts a key. The row id is the key's own id. An element without one, such as a revocation, gets
    /// a new id.
    /// </summary>
    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrEmpty(friendlyName);

        var id = Guid.TryParse((string?)element.Attribute("id"), out var keyId) ? keyId : Guid.NewGuid();
        var stored = cipher.EncryptDataProtectionKey(id, element.ToString(SaveOptions.DisableFormatting));

        using var command = dataSource.CreateCommand("INSERT INTO platform.data_protection_keys (id, friendly_name, xml) VALUES (@id, @name, @xml)");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", friendlyName);
        command.Parameters.AddWithValue("xml", stored);
        command.ExecuteNonQuery();
    }
}
