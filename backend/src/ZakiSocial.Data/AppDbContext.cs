using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ZakiSocial.Core.Entities;

namespace ZakiSocial.Data;

/// <summary>
/// سياق قاعدة البيانات. كل جدول تابع لمساحة فيه workspace_id، والعزل مطبّق بـ Row-Level Security
/// (هجرة EnableRowLevelSecurity). الجداول والأعمدة بأسماء snake_case.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<SocialAccount> SocialAccounts => Set<SocialAccount>();
    public DbSet<Proxy> Proxies => Set<Proxy>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostTarget> PostTargets => Set<PostTarget>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<Prompt> Prompts => Set<Prompt>();
    public DbSet<PromptVersion> PromptVersions => Set<PromptVersion>();
    public DbSet<AnalyticsSnapshot> AnalyticsSnapshots => Set<AnalyticsSnapshot>();
    public DbSet<ResponderRule> ResponderRules => Set<ResponderRule>();
    public DbSet<CollectedItem> CollectedData => Set<CollectedItem>();
    public DbSet<AppLog> AppLogs => Set<AppLog>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<AiUsage> AiUsages => Set<AiUsage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ModelConfiguration.Apply(modelBuilder);
}

public static class DbOptions
{
    /// <summary>إعداد موحّد للتطبيق وللأدوات: بوستغرس + أسماء snake_case.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
}

/// <summary>يستعمله أمر dotnet ef وقت إنشاء الهجرات؛ الاتصال من متغير البيئة بدون أي سر بالكود.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                         ?? "Host=localhost;Database=zaki_social;Username=zaki";
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DbOptions.Configure(builder, connection);
        return new AppDbContext(builder.Options);
    }
}
