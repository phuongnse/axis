namespace Axis.Tenancy.Keys;

/// <summary>The two kinds of key that a tenant has.</summary>
public enum TenantKeyKind
{
    /// <summary>Encrypts a value.</summary>
    Data,

    /// <summary>Makes the lookup value that finds an encrypted value.</summary>
    Lookup,
}

/// <summary>
/// One numbered key of a tenant: 32 bytes. Its text form is only the kind and the number, so logging or serializing it
/// never shows the bytes. Read the bytes with <see cref="Reveal"/> only at the moment of a call.
/// </summary>
public sealed class TenantKey
{
    /// <summary>The size of a key in bytes.</summary>
    public const int Size = 32;

    private readonly byte[] _bytes;

    public TenantKey(TenantKeyKind kind, int number, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        if (bytes.Length != Size)
        {
            throw new ArgumentException($"A tenant key is {Size} bytes.", nameof(bytes));
        }

        Kind = kind;
        Number = number;
        _bytes = [.. bytes];
    }

    public TenantKeyKind Kind { get; }

    /// <summary>The key's number, from 1. The newest key of a kind has the highest number.</summary>
    public int Number { get; }

    /// <summary>Returns the key bytes. Pass them straight to the call that needs them, and never store or log them.</summary>
    public ReadOnlySpan<byte> Reveal() => _bytes;

    public override string ToString() => $"{(Kind == TenantKeyKind.Data ? "data" : "lookup")} key {Number}";
}
