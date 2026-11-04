using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZakiSocial.Core.Entities;

namespace ZakiSocial.Data;

/// <summary>تعريف الجداول بالتفصيل (أنواع الأعمدة، القيم الافتراضية، القيود، الفهارس).</summary>
internal static class ModelConfiguration
{
    private const string NewId = "gen_random_uuid()";
    private const string Now = "now()";

    public static void Apply(ModelBuilder b)
    {
        b.Entity<Workspace>(e =>
        {
            e.ToTable("workspaces");
            Identity(e);
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.Plan).IsRequired().HasDefaultValue("internal");
            e.Property(x => x.CreatedAt).HasDefaultValueSql(Now);
        });

        b.Entity<User>(e =>
        {
            e.ToTable("users");
            Identity(e);
            e.Property(x => x.Email).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql(Now);
            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<Membership>(e =>
        {
            e.ToTable("memberships", t => t.HasCheckConstraint("ck_memberships_role", "role IN ('owner','editor','viewer')"));
            e.HasKey(x => new { x.UserId, x.WorkspaceId });
            e.Property(x => x.Role).IsRequired();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            Tenant(e);
        });

        b.Entity<Proxy>(e =>
        {
            e.ToTable("proxies", t => t.HasCheckConstraint("ck_proxies_status", "status IN ('unknown','healthy','unhealthy')"));
            Identity(e);
            Tenant(e);
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.Host).IsRequired();
            e.Property(x => x.Status).IsRequired().HasDefaultValue("unknown");
            e.Property(x => x.CreatedAt).HasDefaultValueSql(Now);
        });

        b.Entity<SocialAccount>(e =>
        {
            e.ToTable("social_accounts", t =>
            {
                t.HasCheckConstraint("ck_social_accounts_platform", "platform IN ('facebook','instagram','tiktok','x','youtube','linkedin')");
                t.HasCheckConstraint("ck_social_accounts_status", "status IN ('active','expired','disabled','error')");
            });
            Identity(e);
            Tenant(e);
            e.Property(x => x.Platform).IsRequired();
            e.Property(x => x.DisplayName).IsRequired();
            e.Property(x => x.RemoteUserId).IsRequired();
            e.Property(x => x.EncAccessToken).HasColumnType("bytea");
            e.Property(x => x.EncRefreshToken).HasColumnType("bytea");
            e.Property(x => x.Status).IsRequired().HasDefaultValue("active");
            e.Property(x => x.CreatedAt).HasDefaultValueSql(Now);
            e.HasOne<Proxy>().WithMany().HasForeignKey(x => x.ProxyId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.WorkspaceId, x.Platform, x.RemoteUserId }).IsUnique();
        });

        b.Entity<Post>(e =>
        {
            e.ToTable("posts", t =>
            {
                t.HasCheckConstraint("ck_posts_status", "status IN ('draft','scheduled','publishing','published','failed')");
                t.HasCheckConstraint("ck_posts_repeat", "repeat_rule IN ('none','daily','weekly')");
            });
            Identity(e);
            Tenant(e);
            e.Property(x => x.Body).IsRequired();
            e.Property(x => x.AiGenerated).HasDefaultValue(false);
            e.Property(x => x.RepeatRule).IsRequired().HasDefaultValue("none");
            e.Property(x => x.Status).IsRequired().HasDefaultValue("draft");
            e.Property(x => x.CreatedAt).HasDefaultValueSql(Now);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.WorkspaceId, x.Status, x.ScheduledAt });
        });

        b.Entity<PostTarget>(e =>
        {
            e.ToTable("post_targets", t => t.HasCheckConstraint("ck_post_targets_status", "status IN ('pending','publishing','published','failed')"));
            Identity(e);
            Tenant(e);
            e.Property(x => x.Status).IsRequired().HasDefaultValue("pending");
            e.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<SocialAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.PostId);
            e.HasIndex(x => x.AccountId);
        });

        b.Entity<MediaAsset>(e =>
        {
            e.ToTable("media_assets", t => t.HasCheckConstraint("ck_media_assets_kind", "kind IN ('image','video')"));
            Identity(e);
            Tenant(e);
            e.Property(x => x.StorageKey).IsRequired();
            e.Property(x => x.Kind).IsRequired();
            e.Property(x => x.AiGenerated).HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasDefaultValueSql(Now);
        });

        b.Entity<Prompt>(e =>
        {
            e.ToTable("prompts", t => t.HasCheckConstraint("ck_prompts_preferred_ai", "preferred_ai IS NULL OR preferred_ai IN ('claude','openai','local')"));
            Identity(e);
            Tenant(e);
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.Task).IsRequired().HasDefaultValue("caption");
            e.Property(x => x.Body).IsRequired();
            e.Property(x => x.Variables).HasColumnType("jsonb");
            e.Property(x => x.Version).HasDefaultValue(1);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasDefaultValueSql(Now);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
        });

        b.Entity<PromptVersion>(e =>
        {
            e.ToTable("prompt_versions");
            Identity(e);
            Tenant(e);
            e.Property(x => x.Body).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql(Now);
            e.HasOne<Prompt>().WithMany().HasForeignKey(x => x.PromptId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.PromptId, x.Version }).IsUnique();
        });

        b.Entity<AnalyticsSnapshot>(e =>
        {
            e.ToTable("analytics_snapshots");
            Identity(e);
            Tenant(e);
            e.Property(x => x.Metrics).IsRequired().HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            e.Property(x => x.CapturedAt).HasDefaultValueSql(Now);
            e.HasOne<SocialAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PostTarget>().WithMany().HasForeignKey(x => x.PostTargetId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.AccountId, x.CapturedAt });
        });

        b.Entity<ResponderRule>(e =>
        {
            e.ToTable("responder_rules");
            Identity(e);
            Tenant(e);
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.IsEnabled).HasDefaultValue(false);
            e.Property(x => x.DelayMinSeconds).HasDefaultValue(30);
            e.Property(x => x.DelayMaxSeconds).HasDefaultValue(300);
            e.Property(x => x.CreatedAt).HasDefaultValueSql(Now);
            e.HasOne<SocialAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<CollectedItem>(e =>
        {
            e.ToTable("collected_data");
            Identity(e);
            Tenant(e);
            e.Property(x => x.Kind).IsRequired();
            e.Property(x => x.Data).IsRequired().HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            e.Property(x => x.CollectedAt).HasDefaultValueSql(Now);
            e.HasOne<SocialAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.WorkspaceId, x.Kind, x.CollectedAt });
        });

        b.Entity<AiUsage>(e =>
        {
            e.ToTable("ai_usage");
            Identity(e);
            Tenant(e);
            e.Property(x => x.Provider).IsRequired();
            e.Property(x => x.Model).IsRequired();
            e.Property(x => x.Task).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql(Now);
            e.HasIndex(x => new { x.WorkspaceId, x.CreatedAt });
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            Identity(e);
            e.Property(x => x.TokenHash).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql(Now);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        b.Entity<Invitation>(e =>
        {
            e.ToTable("invitations", t => t.HasCheckConstraint("ck_invitations_role", "role IN ('owner','editor','viewer')"));
            Identity(e);
            Tenant(e);
            e.Property(x => x.Email).IsRequired();
            e.Property(x => x.Role).IsRequired();
            e.Property(x => x.CodeHash).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql(Now);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.InvitedBy).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.CodeHash).IsUnique();
        });

        b.Entity<AppLog>(e =>
        {
            e.ToTable("app_logs");
            Identity(e);
            Tenant(e);
            e.Property(x => x.Level).IsRequired().HasDefaultValue("info");
            e.Property(x => x.Message).IsRequired();
            e.Property(x => x.Context).HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasDefaultValueSql(Now);
            e.HasIndex(x => new { x.WorkspaceId, x.CreatedAt });
        });

        b.Entity<AppSetting>(e =>
        {
            e.ToTable("settings");
            Identity(e);
            Tenant(e);
            e.Property(x => x.Key).IsRequired();
            e.Property(x => x.Value).HasColumnType("jsonb");
            e.Property(x => x.EncValue).HasColumnType("bytea");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql(Now);
            e.HasIndex(x => new { x.WorkspaceId, x.Key }).IsUnique();
        });
    }

    private static void Identity<T>(EntityTypeBuilder<T> e) where T : class, IHasId
    {
        e.HasKey("Id");
        e.Property<Guid>("Id").HasDefaultValueSql(NewId);
    }

    /// <summary>كل جدول تابع لمساحة: عمود workspace_id إلزامي + مفتاح أجنبي + فهرس.</summary>
    private static void Tenant<T>(EntityTypeBuilder<T> e) where T : class, ITenantOwned
    {
        e.Property<Guid>("WorkspaceId").IsRequired();
        e.HasOne<Workspace>().WithMany().HasForeignKey("WorkspaceId").OnDelete(DeleteBehavior.Cascade);
        e.HasIndex("WorkspaceId");
    }
}
