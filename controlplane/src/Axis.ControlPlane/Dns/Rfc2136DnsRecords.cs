using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Axis.ControlPlane.Dns;

/// <summary>
/// Sets and removes records with RFC 2136 dynamic updates, one update over TCP for each call, signed with TSIG HMAC-SHA256
/// (RFC 8945). A set deletes the name's <c>CNAME</c> record set and adds the new record in the same update, so the record is
/// never missing or doubled partway through. The answer must be signed too: an unsigned or wrongly signed success is a failure.
/// </summary>
internal sealed class Rfc2136DnsRecords : IDnsRecords
{
    private const ushort TypeCname = 5;
    private const ushort TypeSoa = 6;
    private const ushort TypeTsig = 250;
    private const ushort ClassIn = 1;
    private const ushort ClassAny = 255;
    private const ushort UpdateFlags = 5 << 11;
    private const ushort Fudge = 300;
    private const string Algorithm = "hmac-sha256";
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    private readonly ControlPlaneDnsOptions _options;
    private readonly TimeProvider _time;
    private readonly byte[] _key;
    private readonly string _zone;
    private readonly string _keyNameText;
    private readonly byte[] _keyName;
    private readonly byte[] _algorithm = DnsWire.EncodeName(Algorithm);

    public Rfc2136DnsRecords(ControlPlaneDnsOptions options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        _options = options;
        _time = time;
        try
        {
            _key = Convert.FromBase64String(options.Key ?? string.Empty);
            _zone = (options.Zone ?? string.Empty).TrimEnd('.').ToLowerInvariant();
            _keyNameText = (options.KeyName ?? string.Empty).TrimEnd('.').ToLowerInvariant();
            _keyName = DnsWire.EncodeName(_keyNameText);
            _ = DnsWire.EncodeName(_zone);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"The settings {ControlPlaneOptions.SectionName}:{nameof(ControlPlaneOptions.Dns)} do not hold a valid zone, key name and base64 key.");
        }
    }

    public Task SetCnameAsync(string name, string target, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var owner = InZone(name);
        return SendAsync(owner, BuildUpdate(owner, DnsWire.EncodeName(target)), cancellationToken);
    }

    public Task RemoveCnameAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var owner = InZone(name);
        return SendAsync(owner, BuildUpdate(owner, null), cancellationToken);
    }

    private string InZone(string name)
    {
        var owner = name.TrimEnd('.').ToLowerInvariant();
        if (owner != _zone && !owner.EndsWith("." + _zone, StringComparison.Ordinal))
        {
            throw new ArgumentException("The name is not in the zone of the DNS server.", nameof(name));
        }

        return owner;
    }

    // The update of RFC 2136 section 2, with the TSIG record of RFC 8945 in the additional section.
    private (byte[] Message, ushort Id, byte[] Mac) BuildUpdate(string owner, byte[]? target)
    {
        var id = (ushort)RandomNumberGenerator.GetInt32(0, 65536);
        var ownerName = DnsWire.EncodeName(owner);
        using var unsigned = new MemoryStream();
        DnsWire.Write(unsigned, id);
        DnsWire.Write(unsigned, UpdateFlags);
        DnsWire.Write(unsigned, (ushort)1);
        DnsWire.Write(unsigned, (ushort)0);
        DnsWire.Write(unsigned, (ushort)(target is null ? 1 : 2));
        DnsWire.Write(unsigned, (ushort)0);
        unsigned.Write(DnsWire.EncodeName(_zone));
        DnsWire.Write(unsigned, TypeSoa);
        DnsWire.Write(unsigned, ClassIn);

        // Delete the record set: class ANY, time to live 0, no data.
        unsigned.Write(ownerName);
        DnsWire.Write(unsigned, TypeCname);
        DnsWire.Write(unsigned, ClassAny);
        DnsWire.Write(unsigned, 0u);
        DnsWire.Write(unsigned, (ushort)0);
        if (target is not null)
        {
            unsigned.Write(ownerName);
            DnsWire.Write(unsigned, TypeCname);
            DnsWire.Write(unsigned, ClassIn);
            DnsWire.Write(unsigned, (uint)IDnsRecords.TimeToLive);
            DnsWire.Write(unsigned, (ushort)target.Length);
            unsigned.Write(target);
        }

        var timeSigned = _time.GetUtcNow().ToUnixTimeSeconds();
        using var toSign = new MemoryStream();
        toSign.Write(unsigned.GetBuffer(), 0, (int)unsigned.Length);
        WriteVariables(toSign, timeSigned, Fudge, 0, []);
        var mac = HMACSHA256.HashData(_key, toSign.ToArray());

        using var signed = new MemoryStream();
        signed.Write(unsigned.GetBuffer(), 0, (int)unsigned.Length);
        signed.Write(_keyName);
        DnsWire.Write(signed, TypeTsig);
        DnsWire.Write(signed, ClassAny);
        DnsWire.Write(signed, 0u);
        DnsWire.Write(signed, (ushort)(_algorithm.Length + 6 + 2 + 2 + mac.Length + 2 + 2 + 2));
        signed.Write(_algorithm);
        DnsWire.WriteUInt48(signed, timeSigned);
        DnsWire.Write(signed, Fudge);
        DnsWire.Write(signed, (ushort)mac.Length);
        signed.Write(mac);
        DnsWire.Write(signed, id);
        DnsWire.Write(signed, (ushort)0);
        DnsWire.Write(signed, (ushort)0);
        var message = signed.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(10), 1);
        return (message, id, mac);
    }

    // The TSIG variables that the MAC covers after the message (RFC 8945 section 4.3.3).
    private void WriteVariables(MemoryStream stream, long timeSigned, ushort fudge, ushort error, byte[] other)
    {
        stream.Write(_keyName);
        DnsWire.Write(stream, ClassAny);
        DnsWire.Write(stream, 0u);
        stream.Write(_algorithm);
        DnsWire.WriteUInt48(stream, timeSigned);
        DnsWire.Write(stream, fudge);
        DnsWire.Write(stream, error);
        DnsWire.Write(stream, (ushort)other.Length);
        stream.Write(other);
    }

    private async Task SendAsync(string owner, (byte[] Message, ushort Id, byte[] Mac) update, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_options.Server!, _options.Port, timeout.Token);
            var stream = client.GetStream();
            var request = new byte[update.Message.Length + 2];
            BinaryPrimitives.WriteUInt16BigEndian(request, (ushort)update.Message.Length);
            update.Message.CopyTo(request, 2);
            await stream.WriteAsync(request, timeout.Token);

            var length = new byte[2];
            await stream.ReadExactlyAsync(length, timeout.Token);
            var response = new byte[BinaryPrimitives.ReadUInt16BigEndian(length)];
            await stream.ReadExactlyAsync(response, timeout.Token);
            Check(owner, response, update.Id, update.Mac);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DnsUpdateException($"The DNS server did not answer the update of {owner} within {_timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.");
        }
        catch (Exception exception) when (exception is IOException or SocketException)
        {
            throw new DnsUpdateException($"The update of {owner} could not be sent to the DNS server.", exception);
        }
        catch (FormatException exception)
        {
            throw new DnsUpdateException($"The answer of the DNS server to the update of {owner} is malformed.", exception);
        }
    }

    // Reads the answer: the answer code first, then the signature of a success.
    private void Check(string owner, byte[] response, ushort id, byte[] requestMac)
    {
        var message = response.AsSpan();
        var position = 0;
        var answerId = DnsWire.ReadUInt16(message, ref position);
        var flags = DnsWire.ReadUInt16(message, ref position);
        var zones = DnsWire.ReadUInt16(message, ref position);
        var prerequisites = DnsWire.ReadUInt16(message, ref position);
        var updates = DnsWire.ReadUInt16(message, ref position);
        var additional = DnsWire.ReadUInt16(message, ref position);
        if (answerId != id || (flags & 0x8000) == 0 || ((flags >> 11) & 0xF) != (UpdateFlags >> 11))
        {
            throw new DnsUpdateException($"The DNS server answered the update of {owner} with a message that is not its answer.");
        }

        for (var index = 0; index < zones; index++)
        {
            DnsWire.ReadName(message, ref position);
            DnsWire.Skip(message, ref position, 4);
        }

        for (var index = 0; index < prerequisites + updates; index++)
        {
            SkipRecord(message, ref position);
        }

        Tsig? tsig = null;
        var tsigStart = 0;
        for (var index = 0; index < additional; index++)
        {
            var start = position;
            var name = DnsWire.ReadName(message, ref position);
            var type = DnsWire.ReadUInt16(message, ref position);
            DnsWire.Skip(message, ref position, 6);
            var dataLength = DnsWire.ReadUInt16(message, ref position);
            var dataEnd = position + dataLength;
            if (dataEnd > message.Length)
            {
                throw new FormatException();
            }

            if (type == TypeTsig && index == additional - 1)
            {
                tsigStart = start;
                tsig = ReadTsig(message[..dataEnd], position, name);
            }

            position = dataEnd;
        }

        var code = flags & 0xF;
        if (code != 0)
        {
            var tsigError = tsig is { Error: not 0 } ? $" (TSIG {TsigErrorName(tsig.Error)})" : string.Empty;
            throw new DnsUpdateException($"The DNS server refused the update of {owner}: it answered {CodeName(code)}{tsigError}.");
        }

        if (tsig is null)
        {
            throw new DnsUpdateException($"The DNS server answered the update of {owner} without a signature.");
        }

        var now = _time.GetUtcNow().ToUnixTimeSeconds();
        var signedByUs = tsig.KeyName == _keyNameText && tsig.Algorithm == Algorithm && tsig.Error == 0;
        if (!signedByUs || Math.Abs(now - tsig.TimeSigned) > tsig.Fudge || !SignatureMatches(response, tsigStart, requestMac, tsig))
        {
            throw new DnsUpdateException($"The DNS server answered the update of {owner} with a signature that does not match.");
        }
    }

    private bool SignatureMatches(byte[] response, int tsigStart, byte[] requestMac, Tsig tsig)
    {
        using var toSign = new MemoryStream();
        DnsWire.Write(toSign, (ushort)requestMac.Length);
        toSign.Write(requestMac);
        toSign.Write(response, 0, tsigStart);
        var message = toSign.GetBuffer();
        var additionalOffset = 2 + requestMac.Length + 10;
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(additionalOffset), (ushort)(BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(10)) - 1));
        WriteVariables(toSign, tsig.TimeSigned, tsig.Fudge, tsig.Error, tsig.Other);
        var expected = HMACSHA256.HashData(_key, toSign.ToArray());
        return CryptographicOperations.FixedTimeEquals(expected, tsig.Mac);
    }

    private static Tsig ReadTsig(ReadOnlySpan<byte> message, int position, string keyName)
    {
        var algorithm = DnsWire.ReadName(message, ref position);
        var timeSigned = DnsWire.ReadUInt48(message, ref position);
        var fudge = DnsWire.ReadUInt16(message, ref position);
        var macLength = DnsWire.ReadUInt16(message, ref position);
        var macStart = position;
        DnsWire.Skip(message, ref position, macLength);
        DnsWire.Skip(message, ref position, 2);
        var error = DnsWire.ReadUInt16(message, ref position);
        var otherLength = DnsWire.ReadUInt16(message, ref position);
        var otherStart = position;
        DnsWire.Skip(message, ref position, otherLength);
        return new Tsig(
            keyName,
            algorithm,
            timeSigned,
            fudge,
            message.Slice(macStart, macLength).ToArray(),
            error,
            message.Slice(otherStart, otherLength).ToArray());
    }

    private static void SkipRecord(ReadOnlySpan<byte> message, ref int position)
    {
        DnsWire.ReadName(message, ref position);
        DnsWire.Skip(message, ref position, 8);
        var dataLength = DnsWire.ReadUInt16(message, ref position);
        DnsWire.Skip(message, ref position, dataLength);
    }

    private static string CodeName(int code) => code switch
    {
        1 => "FORMERR",
        2 => "SERVFAIL",
        3 => "NXDOMAIN",
        4 => "NOTIMP",
        5 => "REFUSED",
        6 => "YXDOMAIN",
        7 => "YXRRSET",
        8 => "NXRRSET",
        9 => "NOTAUTH",
        10 => "NOTZONE",
        _ => $"RCODE {code.ToString(CultureInfo.InvariantCulture)}",
    };

    private static string TsigErrorName(int error) => error switch
    {
        16 => "BADSIG",
        17 => "BADKEY",
        18 => "BADTIME",
        19 => "BADMODE",
        20 => "BADNAME",
        21 => "BADALG",
        22 => "BADTRUNC",
        _ => $"error {error.ToString(CultureInfo.InvariantCulture)}",
    };

    private sealed record Tsig(string KeyName, string Algorithm, long TimeSigned, ushort Fudge, byte[] Mac, ushort Error, byte[] Other);
}
