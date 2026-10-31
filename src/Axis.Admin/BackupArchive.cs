using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Axis.Admin;

/// <summary>
/// The encrypted container of a tenant backup. The file starts with <c>AXISBAK1</c> and 8 random bytes, the nonce prefix. The tar
/// follows in chunks of 1 MiB. Each chunk is the AES-256-GCM ciphertext and its 16-byte tag. A chunk's nonce is the prefix and its
/// number as 4 bytes, big-endian, from 0. Its associated data is one byte, 1 for the last chunk and 0 for the others, so an archive that
/// is cut at a chunk boundary does not decrypt. A reader that stops early has not checked the rest of the file.
/// </summary>
internal static class BackupArchive
{
    public const int ChunkSize = 1024 * 1024;

    public const int PrefixSize = 8;

    public const int TagSize = 16;

    public const int KeySize = 32;

    /// <summary>The bytes before the first chunk: the magic and the nonce prefix.</summary>
    public const int HeaderSize = 16;

    private const int NonceSize = 12;

    private static ReadOnlySpan<byte> Magic => "AXISBAK1"u8;

    /// <summary>Returns a write-only stream that writes the header and then the encrypted chunks to <paramref name="inner"/>. Disposing it writes the last chunk, and does not dispose <paramref name="inner"/>.</summary>
    public static Stream Encrypt(Stream inner, byte[] key) => new EncryptStream(inner, key);

    /// <summary>
    /// Returns a read-only stream of the decrypted tar. It reads the header now and a chunk at a time after that, and does not dispose <paramref name="inner"/>.
    /// </summary>
    /// <exception cref="BackupArchiveException">The header is not an encrypted backup's. A bad chunk throws the same while reading.</exception>
    public static Stream Decrypt(Stream inner, byte[] key) => new DecryptStream(inner, key);

    private static void Nonce(Span<byte> nonce, ReadOnlySpan<byte> prefix, long counter)
    {
        prefix.CopyTo(nonce);
        BinaryPrimitives.WriteUInt32BigEndian(nonce[PrefixSize..], (uint)counter);
    }

    private sealed class EncryptStream : Stream
    {
        private readonly Stream _inner;
        private readonly AesGcm _aes;
        private readonly byte[] _prefix = RandomNumberGenerator.GetBytes(PrefixSize);
        private readonly byte[] _plain = new byte[ChunkSize];
        private readonly byte[] _sealed = new byte[ChunkSize + TagSize];

        private bool _headerWritten;
        private bool _disposed;
        private int _filled;
        private long _counter;

        public EncryptStream(Stream inner, byte[] key)
        {
            _inner = inner;
            _aes = new AesGcm(key, TagSize);
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            while (!buffer.IsEmpty)
            {
                if (_filled == ChunkSize)
                {
                    WriteHeader();
                    _inner.Write(Seal(last: false).Span);
                }

                buffer = Take(buffer);
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            while (!buffer.IsEmpty)
            {
                if (_filled == ChunkSize)
                {
                    await WriteHeaderAsync(cancellationToken);
                    await _inner.WriteAsync(Seal(last: false), cancellationToken);
                }

                buffer = Take(buffer);
            }
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                try
                {
                    await WriteHeaderAsync(CancellationToken.None);
                    await _inner.WriteAsync(Seal(last: true), CancellationToken.None);
                }
                finally
                {
                    _aes.Dispose();
                }
            }

            await base.DisposeAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                try
                {
                    WriteHeader();
                    _inner.Write(Seal(last: true).Span);
                }
                finally
                {
                    _aes.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        private ReadOnlySpan<byte> Take(ReadOnlySpan<byte> buffer)
        {
            var count = Math.Min(buffer.Length, ChunkSize - _filled);
            buffer[..count].CopyTo(_plain.AsSpan(_filled));
            _filled += count;
            return buffer[count..];
        }

        private ReadOnlyMemory<byte> Take(ReadOnlyMemory<byte> buffer)
        {
            var count = Math.Min(buffer.Length, ChunkSize - _filled);
            buffer[..count].Span.CopyTo(_plain.AsSpan(_filled));
            _filled += count;
            return buffer[count..];
        }

        private void WriteHeader()
        {
            if (!_headerWritten)
            {
                _headerWritten = true;
                _inner.Write(Header());
            }
        }

        private async ValueTask WriteHeaderAsync(CancellationToken cancellationToken)
        {
            if (!_headerWritten)
            {
                _headerWritten = true;
                await _inner.WriteAsync(Header(), cancellationToken);
            }
        }

        private byte[] Header()
        {
            var header = new byte[HeaderSize];
            Magic.CopyTo(header);
            _prefix.CopyTo(header, Magic.Length);
            return header;
        }

        // Encrypts the buffered bytes as the next chunk and returns the ciphertext and the tag.
        private ReadOnlyMemory<byte> Seal(bool last)
        {
            if (_counter > uint.MaxValue)
            {
                throw new InvalidOperationException("the archive is too large to encrypt");
            }

            Span<byte> nonce = stackalloc byte[NonceSize];
            Nonce(nonce, _prefix, _counter);
            var count = _filled;
            _aes.Encrypt(nonce, _plain.AsSpan(0, count), _sealed.AsSpan(0, count), _sealed.AsSpan(count, TagSize), [last ? (byte)1 : (byte)0]);
            _counter++;
            _filled = 0;
            return _sealed.AsMemory(0, count + TagSize);
        }
    }

    private sealed class DecryptStream : Stream
    {
        private const int RawSize = ChunkSize + TagSize;

        private readonly Stream _inner;
        private readonly AesGcm _aes;
        private readonly byte[] _prefix = new byte[PrefixSize];
        private readonly byte[] _raw = new byte[RawSize];
        private readonly byte[] _plain = new byte[ChunkSize];

        private int _plainLength;
        private int _plainPosition;
        private long _counter;
        private int _carry = -1;
        private bool _done;
        private bool _disposed;

        public DecryptStream(Stream inner, byte[] key)
        {
            _inner = inner;
            _aes = new AesGcm(key, TagSize);
            try
            {
                Span<byte> header = stackalloc byte[HeaderSize];
                if (inner.ReadAtLeast(header, HeaderSize, throwOnEndOfStream: false) < HeaderSize || !header[..Magic.Length].SequenceEqual(Magic))
                {
                    throw new BackupArchiveException(BackupArchiveProblem.NotEncrypted, "the archive is not an encrypted backup");
                }

                header[Magic.Length..].CopyTo(_prefix);
            }
            catch
            {
                _aes.Dispose();
                throw;
            }
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            while (_plainPosition == _plainLength)
            {
                if (_done)
                {
                    return 0;
                }

                ReadChunk();
            }

            return Copy(buffer);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            while (_plainPosition == _plainLength)
            {
                if (_done)
                {
                    return 0;
                }

                await ReadChunkAsync(cancellationToken);
            }

            return Copy(buffer.Span);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _aes.Dispose();
            }

            base.Dispose(disposing);
        }

        private int Copy(Span<byte> buffer)
        {
            var count = Math.Min(buffer.Length, _plainLength - _plainPosition);
            _plain.AsSpan(_plainPosition, count).CopyTo(buffer);
            _plainPosition += count;
            return count;
        }

        // A byte that the last chunk's check read ahead is the first byte of this chunk.
        private int Start()
        {
            if (_carry < 0)
            {
                return 0;
            }

            _raw[0] = (byte)_carry;
            _carry = -1;
            return 1;
        }

        private void ReadChunk()
        {
            var have = Start();
            have += _inner.ReadAtLeast(_raw.AsSpan(have, RawSize - have), RawSize - have, throwOnEndOfStream: false);
            var last = true;
            if (have == RawSize)
            {
                var next = _inner.ReadByte();
                last = next < 0;
                _carry = next;
            }

            Open(have, last);
        }

        private async ValueTask ReadChunkAsync(CancellationToken cancellationToken)
        {
            var have = Start();
            have += await _inner.ReadAtLeastAsync(_raw.AsMemory(have, RawSize - have), RawSize - have, throwOnEndOfStream: false, cancellationToken);
            var last = true;
            if (have == RawSize)
            {
                var one = new byte[1];
                var read = await _inner.ReadAsync(one, cancellationToken);
                last = read == 0;
                _carry = last ? -1 : one[0];
            }

            Open(have, last);
        }

        // Decrypts the chunk in the buffer. The first chunk that fails means a wrong key, and a later one a changed, missing or moved chunk.
        private void Open(int rawLength, bool last)
        {
            var length = rawLength - TagSize;
            try
            {
                if (length < 0 || _counter > uint.MaxValue)
                {
                    throw new CryptographicException();
                }

                Span<byte> nonce = stackalloc byte[NonceSize];
                Nonce(nonce, _prefix, _counter);
                _aes.Decrypt(nonce, _raw.AsSpan(0, length), _raw.AsSpan(length, TagSize), _plain.AsSpan(0, length), [last ? (byte)1 : (byte)0]);
            }
            catch (CryptographicException)
            {
                throw _counter == 0
                    ? new BackupArchiveException(BackupArchiveProblem.WrongKey, "the backup key does not open the archive")
                    : new BackupArchiveException(BackupArchiveProblem.Corrupt, "the archive does not decrypt: a chunk is changed, missing or out of order");
            }

            _counter++;
            _plainLength = length;
            _plainPosition = 0;
            _done = last;
        }
    }
}

/// <summary>Why a backup archive does not open.</summary>
internal enum BackupArchiveProblem
{
    NotEncrypted,
    WrongKey,
    Corrupt,
}

/// <summary>
/// An archive that is not an encrypted backup, or that the key does not open. The message holds no secret, so a step prints it. It is not an
/// <see cref="IOException"/>, because the restore swallows a closed pipe.
/// </summary>
internal sealed class BackupArchiveException(BackupArchiveProblem problem, string message) : InvalidOperationException(message)
{
    public BackupArchiveProblem Problem { get; } = problem;
}
