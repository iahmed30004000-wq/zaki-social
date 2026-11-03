namespace ZakiSocial.Core.Health;

/// <summary>نتيجة فحص الصحة. كل حقل "ok" أو "fail".</summary>
public sealed record HealthReport(string Status, string Database, string Redis, DateTimeOffset Time);

public interface IHealthService
{
    Task<HealthReport> CheckAsync(CancellationToken ct);
}
