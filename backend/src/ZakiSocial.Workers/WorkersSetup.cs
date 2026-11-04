using Hangfire;
using Hangfire.Redis.StackExchange;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZakiSocial.Core.Jobs;

namespace ZakiSocial.Workers;

public sealed class HangfireJobQueue(IBackgroundJobClient client) : IJobQueue
{
    public void EnqueuePublish(Guid workspaceId, Guid targetId, int attempt = 1, TimeSpan? delay = null)
    {
        if (delay is { } d && d > TimeSpan.Zero)
            client.Schedule<PublishJob>(j => j.RunAsync(workspaceId, targetId, attempt, CancellationToken.None), d);
        else
            client.Enqueue<PublishJob>(j => j.RunAsync(workspaceId, targetId, attempt, CancellationToken.None));
    }
}

public static class WorkersSetup
{
    /// <summary>
    /// الطابور على ريديس. Workers:Enabled=true بيشغّل عامل التنفيذ داخل نفس العملية (الافتراضي)؛
    /// وبنقدر لاحقاً نشغّل نسخة ثانية بس للعمّال لما يزيد الحمل.
    /// </summary>
    public static IServiceCollection AddZakiWorkers(this IServiceCollection services, IConfiguration configuration)
    {
        var redis = configuration["Redis:ConnectionString"] ?? "localhost:6379";
        services.AddHangfire(c => c
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseRedisStorage(redis, new RedisStorageOptions { Prefix = "hangfire:zaki:" }));

        if (configuration.GetValue("Workers:Enabled", true))
        {
            services.AddHostedService<RecurringJobsRegistrar>();
            services.AddHangfireServer(o => o.WorkerCount = configuration.GetValue("Workers:Concurrency", 4));
        }

        services.AddScoped<PublishJob>();
        services.AddScoped<SchedulerJob>();
        services.AddSingleton<IJobQueue, HangfireJobQueue>();
        return services;
    }
}
