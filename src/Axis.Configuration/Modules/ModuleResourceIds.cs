using System.Security.Cryptography;
using System.Text;

namespace Axis.Configuration.Modules;

/// <summary>
/// Derives the model id of a module's resource for one application, so two applications that use
/// the same module never share a table or a counter. The id is a UUID v5 of the application id
/// and the id written in the module file, so it stays the same when the module version changes.
/// </summary>
public static class ModuleResourceIds
{
    /// <summary>Derives the id of the module resource <paramref name="resourceId"/> in the application <paramref name="applicationId"/>.</summary>
    public static Guid Derive(Guid applicationId, Guid resourceId) =>
        Create(applicationId, resourceId.ToString("D"));

    /// <summary>Creates a name-based UUID version 5, as RFC 9562 defines it, from a namespace and a name.</summary>
    public static Guid Create(Guid namespaceId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Span<byte> input = new byte[16 + Encoding.UTF8.GetByteCount(name)];
        namespaceId.TryWriteBytes(input, bigEndian: true, out _);
        Encoding.UTF8.GetBytes(name, input[16..]);
        Span<byte> hash = stackalloc byte[20];

        // UUID v5 is defined over SHA-1. It names resources and is not used for security.
#pragma warning disable CA5350
        SHA1.HashData(input, hash);
#pragma warning restore CA5350
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash[..16], bigEndian: true);
    }
}
