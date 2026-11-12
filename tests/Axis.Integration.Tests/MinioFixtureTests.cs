using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Amazon.S3;
using Amazon.S3.Model;

namespace Axis.Integration.Tests;

/// <summary>
/// The helpers that create a bucket, read an object from MinIO and upload one to it try again when MinIO drops a connection or answers that the client
/// disconnected, and fail when every try does. The waits between tries grow from half a second. A create or an upload that fails with any other S3 error
/// fails at once. A bucket create that finds the bucket already owned fails on its first try, but counts as created on a retry.
/// </summary>
public sealed class MinioFixtureTests
{
    private static readonly Func<TimeSpan, CancellationToken, Task> _noWait = (_, _) => Task.CompletedTask;

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
            CancellationToken,
            _noWait);

        Assert.Equal(bytes, result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_body_that_is_cut_short_every_time_fails_after_five_attempts()
    {
        var calls = 0;

        await Assert.ThrowsAsync<HttpIOException>(() => MinioFixture.ReadWholeAsync(
            _ =>
            {
                calls++;
                return Task.FromResult(new GetObjectResponse { ResponseStream = new CutShortStream(3) });
            },
            CancellationToken,
            _noWait));

        Assert.Equal(5, calls);
    }

    [Fact]
    public async Task A_read_whose_server_answers_client_disconnected_three_times_returns_the_whole_object()
    {
        byte[] bytes = [1, 2, 3, 4, 5, 6, 7, 8];
        var calls = 0;
        var waits = new List<TimeSpan>();

        var result = await MinioFixture.ReadWholeAsync(
            _ =>
            {
                calls++;
                if (calls <= 3)
                {
                    throw new AmazonS3Exception("Client disconnected before response was ready") { ErrorCode = "ClientDisconnected" };
                }

                return Task.FromResult(new GetObjectResponse { ResponseStream = new MemoryStream(bytes) });
            },
            CancellationToken,
            Recording(waits));

        Assert.Equal(bytes, result);
        Assert.Equal(4, calls);
        Assert.Equal<TimeSpan>([TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], waits);
    }

    [Fact]
    public async Task A_read_answered_with_status_499_every_time_fails_after_five_attempts()
    {
        var calls = 0;
        var waits = new List<TimeSpan>();

        await Assert.ThrowsAsync<AmazonS3Exception>(() => MinioFixture.ReadWholeAsync(
            _ =>
            {
                calls++;
                throw new AmazonS3Exception("x") { StatusCode = (HttpStatusCode)499 };
            },
            CancellationToken,
            Recording(waits)));

        Assert.Equal(5, calls);
        Assert.Equal<TimeSpan>([TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], waits);
    }

    [Fact]
    public async Task An_upload_whose_connection_is_dropped_once_stores_the_whole_object()
    {
        byte[] bytes = [1, 2, 3, 4, 5, 6, 7, 8];
        var calls = 0;
        using var stored = new MemoryStream();

        await MinioFixture.UploadWholeAsync(
            async (request, token) =>
            {
                calls++;
                if (calls == 1)
                {
                    await request.InputStream.ReadExactlyAsync(new byte[3], token);
                    throw new AmazonS3Exception("Client disconnected before response was ready") { ErrorCode = "ClientDisconnected" };
                }

                await request.InputStream.CopyToAsync(stored, token);
            },
            "bucket",
            "key",
            bytes,
            CancellationToken,
            _noWait);

        Assert.Equal(bytes, stored.ToArray());
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task An_upload_whose_connection_is_reset_is_sent_again()
    {
        byte[] bytes = [1, 2, 3, 4, 5, 6, 7, 8];
        var calls = 0;
        using var stored = new MemoryStream();

        await MinioFixture.UploadWholeAsync(
            async (request, token) =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new IOException("Connection reset by peer", new SocketException((int)SocketError.ConnectionReset));
                }

                await request.InputStream.CopyToAsync(stored, token);
            },
            "bucket",
            "key",
            bytes,
            CancellationToken,
            _noWait);

        Assert.Equal(bytes, stored.ToArray());
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task An_upload_failing_with_another_S3_error_fails_after_one_attempt()
    {
        var calls = 0;

        await Assert.ThrowsAsync<AmazonS3Exception>(() => MinioFixture.UploadWholeAsync(
            (_, _) =>
            {
                calls++;
                throw new AmazonS3Exception("The specified bucket does not exist") { ErrorCode = "NoSuchBucket" };
            },
            "bucket",
            "key",
            [1, 2, 3],
            CancellationToken,
            _noWait));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task An_upload_whose_connection_is_dropped_every_time_fails_after_five_attempts()
    {
        var calls = 0;

        await Assert.ThrowsAsync<IOException>(() => MinioFixture.UploadWholeAsync(
            (_, _) =>
            {
                calls++;
                throw new IOException("Connection reset by peer", new SocketException((int)SocketError.ConnectionReset));
            },
            "bucket",
            "key",
            [1, 2, 3],
            CancellationToken,
            _noWait));

        Assert.Equal(5, calls);
    }

    [Fact]
    public async Task A_bucket_create_whose_connection_is_reset_creates_the_bucket()
    {
        var calls = 0;
        PutBucketRequest? created = null;

        await MinioFixture.CreateBucketAsync(
            (request, _) =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new IOException("Connection reset by peer", new SocketException((int)SocketError.ConnectionReset));
                }

                created = request;
                return Task.CompletedTask;
            },
            "bucket",
            CancellationToken,
            wait: _noWait);

        Assert.Equal("bucket", created?.BucketName);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_bucket_create_whose_connection_is_dropped_every_time_fails_after_five_attempts()
    {
        var calls = 0;

        await Assert.ThrowsAsync<IOException>(() => MinioFixture.CreateBucketAsync(
            (_, _) =>
            {
                calls++;
                throw new IOException("Connection reset by peer", new SocketException((int)SocketError.ConnectionReset));
            },
            "bucket",
            CancellationToken,
            wait: _noWait));

        Assert.Equal(5, calls);
    }

    [Fact]
    public async Task A_bucket_create_retried_after_a_dropped_answer_counts_the_bucket_already_owned_as_created()
    {
        var calls = 0;

        await MinioFixture.CreateBucketAsync(
            (_, _) =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new AmazonS3Exception("Client disconnected before response was ready") { ErrorCode = "ClientDisconnected" };
                }

                throw new AmazonS3Exception("Your previous request to create the named bucket succeeded and you already own it.") { ErrorCode = "BucketAlreadyOwnedByYou" };
            },
            "bucket",
            CancellationToken,
            wait: _noWait);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_bucket_already_owned_on_the_first_try_fails_at_once()
    {
        var calls = 0;

        await Assert.ThrowsAsync<AmazonS3Exception>(() => MinioFixture.CreateBucketAsync(
            (_, _) =>
            {
                calls++;
                throw new AmazonS3Exception("Your previous request to create the named bucket succeeded and you already own it.") { ErrorCode = "BucketAlreadyOwnedByYou" };
            },
            "bucket",
            CancellationToken,
            wait: _noWait));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_bucket_created_with_object_lock_asks_MinIO_for_object_lock()
    {
        PutBucketRequest? created = null;

        await MinioFixture.CreateBucketAsync(
            (request, _) =>
            {
                created = request;
                return Task.CompletedTask;
            },
            "bucket",
            CancellationToken,
            objectLock: true,
            wait: _noWait);

        Assert.NotNull(created);
        Assert.True(created.ObjectLockEnabledForBucket);
    }

    private static Func<TimeSpan, CancellationToken, Task> Recording(List<TimeSpan> waits) =>
        (delay, _) =>
        {
            waits.Add(delay);
            return Task.CompletedTask;
        };

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
