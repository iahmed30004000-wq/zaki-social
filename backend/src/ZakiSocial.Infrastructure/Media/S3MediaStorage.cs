using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using ZakiSocial.Core.Media;

namespace ZakiSocial.Infrastructure.Media;

/// <summary>S3 متوافق (MinIO محلياً وعلى السيرفر). المفاتيح من البيئة: Storage__Endpoint/AccessKey/SecretKey/Bucket.</summary>
public sealed class S3MediaStorage : IMediaStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly SemaphoreSlim _bucketLock = new(1, 1);
    private bool _bucketReady;

    public S3MediaStorage(IConfiguration configuration)
    {
        var endpoint = configuration["Storage:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint)) throw new InvalidOperationException("Storage:Endpoint must be set (environment variable Storage__Endpoint).");
        _bucket = configuration["Storage:Bucket"] ?? "zaki-media";
        _client = new AmazonS3Client(
            configuration["Storage:AccessKey"] ?? "",
            configuration["Storage:SecretKey"] ?? "",
            new AmazonS3Config { ServiceURL = endpoint, ForcePathStyle = true, AuthenticationRegion = configuration["Storage:Region"] ?? "us-east-1" });
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = _bucket, Key = key, InputStream = content, ContentType = contentType, AutoCloseStream = false }, ct);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        var response = await _client.GetObjectAsync(_bucket, key, ct);
        return new ResponseStream(response);
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        await _client.DeleteObjectAsync(_bucket, key, ct);
    }

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (_bucketReady) return;
        await _bucketLock.WaitAsync(ct);
        try
        {
            if (_bucketReady) return;
            var buckets = await _client.ListBucketsAsync(ct);
            if (!buckets.Buckets.Any(b => b.BucketName == _bucket))
                await _client.PutBucketAsync(new PutBucketRequest { BucketName = _bucket }, ct);
            _bucketReady = true;
        }
        finally
        {
            _bucketLock.Release();
        }
    }

    public void Dispose() => _client.Dispose();

    /// <summary>يغلّف استجابة S3 لحتى ينغلق الاتصال مع الستريم.</summary>
    private sealed class ResponseStream(GetObjectResponse response) : Stream
    {
        private readonly Stream _inner = response.ResponseStream;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => response.ContentLength;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _inner.ReadAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) response.Dispose();
            base.Dispose(disposing);
        }
    }
}
