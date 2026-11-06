using System.Net.Http;
using Amazon.S3.Model;

namespace Axis.Integration.Tests;

/// <summary>The helper that reads an object back from MinIO reads it again when MinIO ends the response early, and fails when every attempt does.</summary>
public sealed class MinioFixtureTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_body_cut_short_is_read_again_from_the_start()
    {
        byte[] bytes = [1, 2, 3, 4, 5, 6, 7, 8];
        var calls = 0;

        var result = await MinioFixture.ReadWholeAsync(
            _ =>
            {
                calls++;
                Stream body = calls == 1 ? new CutShortStream(3) : new MemoryStream(bytes);
                return Task.FromResult(new GetObjectResponse { ResponseStream = body });
            },
            CancellationToken);

        Assert.Equal(bytes, result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_body_that_is_cut_short_every_time_fails_after_three_attempts()
    {
        var calls = 0;

        await Assert.ThrowsAsync<HttpIOException>(() => MinioFixture.ReadWholeAsync(
            _ =>
            {
                calls++;
                return Task.FromResult(new GetObjectResponse { ResponseStream = new CutShortStream(3) });
            },
            CancellationToken));

        Assert.Equal(3, calls);
    }

    // Returns the given number of bytes, then ends as MinIO does when it closes a response early.
    private sealed class CutShortStream(int length) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_position >= length)
            {
                throw new HttpIOException(HttpRequestError.ResponseEnded);
            }

            var count = Math.Min(buffer.Length, length - _position);
            buffer[..count].Clear();
            _position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
