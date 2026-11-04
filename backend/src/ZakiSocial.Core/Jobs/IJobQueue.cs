namespace ZakiSocial.Core.Jobs;

/// <summary>
/// طابور المهام (Redis + Hangfire). الـ API ممنوع تنشر مباشرة: أي نشر بيمر من هون، والعامل هو اللي ينفّذ.
/// </summary>
public interface IJobQueue
{
    /// <summary>ينشر هدف منشور (حساب واحد). attempt = رقم المحاولة (1 أول مرة).</summary>
    void EnqueuePublish(Guid workspaceId, Guid targetId, int attempt = 1, TimeSpan? delay = null);

    /// <summary>معالجة وسيط مرفوع (FFmpeg) بالخلفية.</summary>
    void EnqueueMedia(Guid workspaceId, Guid mediaId);
}
