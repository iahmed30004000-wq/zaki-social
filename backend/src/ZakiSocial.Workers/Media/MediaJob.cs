using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ZakiSocial.Core.Media;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;

namespace ZakiSocial.Workers.Media;

/// <summary>عامل الوسائط: ينزّل الأصل، يعالجه بـ FFmpeg، يرفع النسخة الموحّدة والمصغّر، ويحدّث الحالة (ready/failed).</summary>
public sealed class MediaJob(AppDbContext db, ITenantContext tenant, IMediaStorage storage, FfmpegProcessor ffmpeg, ILogger<MediaJob> logger)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(Guid workspaceId, Guid mediaId, CancellationToken ct)
    {
        tenant.UseWorkspace(workspaceId, PublishJob.WorkerRole);
        var asset = await db.MediaAssets.FirstOrDefaultAsync(m => m.Id == mediaId && m.WorkspaceId == workspaceId, ct);
        if (asset is null || asset.Status != "processing") return;

        var dir = Path.Combine(Path.GetTempPath(), "zaki-media", mediaId.ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "original" + Path.GetExtension(asset.StorageKey));
            await using (var source = await storage.OpenReadAsync(asset.StorageKey, ct))
            await using (var file = File.Create(input))
                await source.CopyToAsync(file, ct);

            await ffmpeg.ProbeAsync(input, ct); // ملف تالف بيفشل هون بدري
            var isVideo = asset.Kind == "video";
            var processed = Path.Combine(dir, isVideo ? "processed.mp4" : "processed.jpg");
            var thumb = Path.Combine(dir, "thumb.jpg");
            if (isVideo) await ffmpeg.ProcessVideoAsync(input, processed, ct);
            else await ffmpeg.ProcessImageAsync(input, processed, ct);
            await ffmpeg.ThumbnailAsync(processed, thumb, isVideo, ct);
            var info = await ffmpeg.ProbeAsync(processed, ct);

            var prefix = $"{workspaceId}/{mediaId}";
            var processedKey = $"{prefix}/{(isVideo ? "processed.mp4" : "processed.jpg")}";
            var thumbKey = $"{prefix}/thumb.jpg";
            await using (var s = File.OpenRead(processed)) await storage.PutAsync(processedKey, s, isVideo ? "video/mp4" : "image/jpeg", ct);
            await using (var s = File.OpenRead(thumb)) await storage.PutAsync(thumbKey, s, "image/jpeg", ct);

            asset.ProcessedKey = processedKey;
            asset.ThumbnailKey = thumbKey;
            asset.Width = info.Width;
            asset.Height = info.Height;
            asset.DurationSeconds = info.DurationSeconds;
            asset.Status = "ready";
            asset.Error = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            asset.Status = "failed";
            asset.Error = ex.Message.Length <= 200 ? ex.Message : ex.Message[..200];
            logger.LogWarning("media {Media} processing failed: {Error}", mediaId, ex.GetType().Name);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { /* تنظيف مؤقت فقط */ }
        }
        await db.SaveChangesAsync(ct);
    }
}
