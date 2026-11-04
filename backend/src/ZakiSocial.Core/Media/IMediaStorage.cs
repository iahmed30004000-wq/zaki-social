namespace ZakiSocial.Core.Media;

/// <summary>تخزين الملفات (S3 / MinIO). المفاتيح مسبوقة بمعرّف المساحة.</summary>
public interface IMediaStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}
