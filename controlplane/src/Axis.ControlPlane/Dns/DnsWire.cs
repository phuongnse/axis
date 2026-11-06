using System.Buffers.Binary;
using System.Text;

namespace Axis.ControlPlane.Dns;

/// <summary>The few parts of the DNS wire format (RFC 1035) that the dynamic update needs.</summary>
internal static class DnsWire
{
    /// <summary>A host name as labels, each led by its length and the whole ended by an empty label. No compression.</summary>
    /// <exception cref="ArgumentException">The name is empty, not ASCII, or has a label of more than 63 bytes.</exception>
    public static byte[] EncodeName(string name)
    {
        var bytes = new List<byte>(name.Length + 2);
        foreach (var label in name.TrimEnd('.').ToLowerInvariant().Split('.'))
        {
            if (label.Length is 0 or > 63 || !label.All(char.IsAscii))
            {
                throw new ArgumentException("The name is not a valid host name.", nameof(name));
            }

            bytes.Add((byte)label.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(label));
        }

        bytes.Add(0);
        if (bytes.Count > 255)
        {
            throw new ArgumentException("The name is longer than 255 bytes.", nameof(name));
        }

        return [.. bytes];
    }

    /// <summary>Reads the name at <paramref name="position"/>, follows compression pointers and moves past the name.</summary>
    /// <exception cref="FormatException">The message ends inside the name, or its pointers loop.</exception>
    public static string ReadName(ReadOnlySpan<byte> message, ref int position)
    {
        var labels = new List<string>();
        var cursor = position;
        var jumped = false;
        var jumps = 0;
        while (true)
        {
            if (cursor >= message.Length)
            {
                throw new FormatException();
            }

            var length = message[cursor];
            if (length == 0)
            {
                if (!jumped)
                {
                    position = cursor + 1;
                }

                return string.Join('.', labels);
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (cursor + 1 >= message.Length)
                {
                    throw new FormatException();
                }

                if (!jumped)
                {
                    position = cursor + 2;
                }

                jumped = true;
                cursor = ((length & 0x3F) << 8) | message[cursor + 1];
                if (++jumps > 32 || cursor >= message.Length)
                {
                    throw new FormatException();
                }

                continue;
            }

            if ((length & 0xC0) != 0 || cursor + 1 + length > message.Length)
            {
                throw new FormatException();
            }

            labels.Add(Encoding.ASCII.GetString(message.Slice(cursor + 1, length)).ToLowerInvariant());
            cursor += 1 + length;
        }
    }

    public static ushort ReadUInt16(ReadOnlySpan<byte> message, ref int position)
    {
        Require(message, position, 2);
        var value = BinaryPrimitives.ReadUInt16BigEndian(message[position..]);
        position += 2;
        return value;
    }

    /// <summary>Reads a 48-bit number, the width of the TSIG time.</summary>
    public static long ReadUInt48(ReadOnlySpan<byte> message, ref int position)
    {
        Require(message, position, 6);
        var value = ((long)BinaryPrimitives.ReadUInt16BigEndian(message[position..]) << 32) | BinaryPrimitives.ReadUInt32BigEndian(message[(position + 2)..]);
        position += 6;
        return value;
    }

    public static void Skip(ReadOnlySpan<byte> message, ref int position, int count)
    {
        Require(message, position, count);
        position += count;
    }

    public static void Write(MemoryStream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    public static void Write(MemoryStream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    public static void WriteUInt48(MemoryStream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        stream.Write(bytes[2..]);
    }

    private static void Require(ReadOnlySpan<byte> message, int position, int count)
    {
        if (position < 0 || count < 0 || position + count > message.Length)
        {
            throw new FormatException();
        }
    }
}
