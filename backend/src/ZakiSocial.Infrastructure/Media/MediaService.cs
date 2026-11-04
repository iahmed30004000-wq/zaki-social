using Microsoft.EntityFrameworkCore;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Jobs;
using ZakiSocial.Core.Media;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;
using ZakiSocial.Infrastructure.Auth;

namespace ZakiSocial.Infrastructure.Media;

public sealed record MediaDto(Guid Id, string Kind, string OriginalName, string Status, string? Error, bool AiGenerated, DateTimeOffset? ApprovedAt,
    long SizeBytes, int? Width, int? Height, double? DurationSeconds, bool HasThumbnail, DateTimeOffset CreatedAt);

/// <summary>
/// مكتبة الوسائط. الرفع بيخزّن الأصل ويحط مهمة معالجة بالطابور (FFmpeg عند العامل): الملفات الكبيرة ما بتتعالج داخل الطلب.
/// </summary>
public sealed class MediaService(AppDbContext db, ITenantContext tenant, IMediaStorage storage, IJobQueue queue)
{
    public const long MaxImageBytes = 25L * 1024 * 1024;
    public const long MaxVideoBytes = 300L * 1024 * 1024;

    private static readonly Dictionary<string, string> Images = new(StringComparer.OrdinalIgnoreCase)
        { ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["image/webp"] = ".webp" };
    private static readonly Dictionary<string, string> Videos = new(StringComparer.OrdinalIgnoreCase)
        { ["video/mp4"] = ".mp4", ["video/quicktime"] = ".mov", ["video/webm"] = ".webm" };

    private Guid Workspace => tenant.WorkspaceId ?? throw new AuthException(403, "no_workspace");

    public async Task<IReadOnlyList<MediaDto>> ListAsync(CancellationToken ct) =>
        (await db.MediaAssets.Where(m => m.WorkspaceId == Workspace).OrderByDescending(m => m.CreatedAt).Take(200).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<MediaDto> GetAsync(Guid id, CancellationToken ct) => ToDto(await FindAsync(id, ct));

    /// <summary>رفع ملف من مستخدم (اعتماد بشري ضمني). contentType من الطلب، ونتحقق منه ومن الحجم.</summary>
    public async Task<MediaDto> UploadAsync(Stream content, string fileName, string contentType, long length, CancellationToken ct)
    {
        string kind;
        string ext;
        if (Images.TryGetValue(contentType, out ext!)) kind = "image";
        else if (Videos.TryGetValue(contentType, out ext!)) kind = "video";
        else throw new AuthException(415, "media_type_unsupported");
        if (length <= 0) throw new AuthException(400, "file_empty");
        if (length > (kind == "image" ? MaxImageBytes : MaxVideoBytes)) throw new AuthException(413, "file_too_large");

        var id = Guid.NewGuid();
        var key = $"{Workspace}/{id}/original{ext}";
        await storage.PutAsync(key, content, contentType, ct);

        var asset = new MediaAsset
        {
            Id = id, WorkspaceId = Workspace, StorageKey = key, Kind = kind, AiGenerated = false, ApprovedAt = DateTimeOffset.UtcNow,
            OriginalName = SafeName(fileName), ContentType = contentType, SizeBytes = length, Status = "processing", CreatedAt = DateTimeOffset.UtcNow,
        };
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync(ct);
        queue.EnqueueMedia(Workspace, id);
        return ToDto(asset);
    }

    /// <summary>وسيط مولّد بالذكاء الاصطناعي (الخطوة 10): يدخل المكتبة معالجاً وبدون اعتماد.</summary>
    public async Task<MediaDto> AddGeneratedAsync(byte[] bytes, string contentType, string prompt, CancellationToken ct)
    {
        using var stream = new MemoryStream(bytes);
        var uploaded = await UploadAsync(stream, "ai-image.png", contentType, bytes.Length, ct);
        var asset = await FindAsync(uploaded.Id, ct);
        asset.AiGenerated = true;
        asset.ApprovedAt = null;
        asset.OriginalName = $"ai: {SafeName(prompt)}";
        await db.SaveChangesAsync(ct);
        return ToDto(asset);
    }

    public async Task<MediaDto> ApproveAsync(Guid id, CancellationToken ct)
    {
        var asset = await FindAsync(id, ct);
        asset.ApprovedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(asset);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var asset = await FindAsync(id, ct);
        var inUse = await (from pm in db.PostMedia
                           join p in db.Posts on pm.PostId equals p.Id
                           where pm.MediaId == id && (p.Status == PostStatus.Scheduled || p.Status == PostStatus.Publishing)
                           select pm.PostId).AnyAsync(ct);
        if (inUse) throw new AuthException(409, "media_in_use");

        foreach (var key in new[] { asset.StorageKey, asset.ProcessedKey, asset.ThumbnailKey }.Where(k => !string.IsNullOrEmpty(k)))
            await storage.DeleteAsync(key!, ct);
        db.MediaAssets.Remove(asset);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>variant: file (المعالج، وإلا الأصل) أو thumb.</summary>
    public async Task<(Stream Stream, string ContentType)> OpenAsync(Guid id, string variant, CancellationToken ct)
    {
        var asset = await FindAsync(id, ct);
        if (variant == "thumb")
        {
            if (asset.ThumbnailKey is null) throw new AuthException(404, "not_found");
            return (await storage.OpenReadAsync(asset.ThumbnailKey, ct), "image/jpeg");
        }
        var key = asset.ProcessedKey ?? asset.StorageKey;
        var type = asset.ProcessedKey is null ? asset.ContentType : asset.Kind == "video" ? "video/mp4" : "image/jpeg";
        return (await storage.OpenReadAsync(key, ct), type);
    }

    private async Task<MediaAsset> FindAsync(Guid id, CancellationToken ct) =>
        await db.MediaAssets.FirstOrDefaultAsync(m => m.Id == id && m.WorkspaceId == Workspace, ct) ?? throw new AuthException(404, "not_found");

    private static MediaDto ToDto(MediaAsset m) => new(m.Id, m.Kind, m.OriginalName, m.Status, m.Error, m.AiGenerated, m.ApprovedAt,
        m.SizeBytes, m.Width, m.Height, m.DurationSeconds, m.ThumbnailKey is not null, m.CreatedAt);

    private static string SafeName(string name)
    {
        name = Path.GetFileName(name ?? "");
        return name.Length > 120 ? name[..120] : name;
    }
}
