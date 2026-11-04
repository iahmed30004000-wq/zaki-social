using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZakiSocial.Data.Migrations;

/// <summary>
/// عزل المساحات بـ Row-Level Security.
/// الفكرة: التطبيق بيشتغل بدور zaki_app (بدون صلاحية تجاوز) ويحدد المساحة الحالية بـ
/// SET LOCAL app.workspace_id = '...' ؛ أي استعلام بيرجّع فقط صفوف هالمساحة،
/// وأي كتابة لمساحة تانية بتنرفض. الاتصال بالمالك (للهجرات وتسجيل الدخول) هو الوحيد اللي بيتجاوز.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261104000002_EnableRowLevelSecurity")]
public partial class EnableRowLevelSecurity : Migration
{
    private const string TenantTables =
        "'memberships','social_accounts','proxies','posts','post_targets','media_assets','prompts'," +
        "'prompt_versions','analytics_snapshots','responder_rules','collected_data','app_logs','settings'";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION app_current_workspace() RETURNS uuid
            LANGUAGE sql STABLE
            AS $$ SELECT NULLIF(current_setting('app.workspace_id', true), '')::uuid $$;
            """);

        migrationBuilder.Sql("""
            DO $$
            BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'zaki_app') THEN
                CREATE ROLE zaki_app NOLOGIN NOSUPERUSER NOBYPASSRLS;
              END IF;
            END $$;
            """);

        migrationBuilder.Sql("GRANT USAGE ON SCHEMA public TO zaki_app;");
        migrationBuilder.Sql("GRANT EXECUTE ON FUNCTION app_current_workspace() TO zaki_app;");

        migrationBuilder.Sql($$"""
            DO $$
            DECLARE t text;
            BEGIN
              FOREACH t IN ARRAY ARRAY[{{TenantTables}}] LOOP
                EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', t);
                EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', t);
                EXECUTE format('CREATE POLICY tenant_isolation ON %I USING (workspace_id = app_current_workspace()) WITH CHECK (workspace_id = app_current_workspace())', t);
                EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON %I TO zaki_app', t);
              END LOOP;
            END $$;
            """);

        // المساحة نفسها: ترى مساحتها فقط. إنشاء مساحة جديدة بيتم بالاتصال الرئيسي (تسجيل المستخدم).
        migrationBuilder.Sql("""
            ALTER TABLE workspaces ENABLE ROW LEVEL SECURITY;
            ALTER TABLE workspaces FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON workspaces
              USING (id = app_current_workspace()) WITH CHECK (id = app_current_workspace());
            GRANT SELECT, UPDATE ON workspaces TO zaki_app;
            """);

        // المستخدمون عامون لكل المساحات: الدور بيشوف بس الأعمدة الآمنة، وما بيشوف password_hash أبداً.
        migrationBuilder.Sql("GRANT SELECT (id, email, created_at) ON users TO zaki_app;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            REVOKE ALL ON users FROM zaki_app;
            DROP POLICY IF EXISTS tenant_isolation ON workspaces;
            ALTER TABLE workspaces NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE workspaces DISABLE ROW LEVEL SECURITY;
            REVOKE ALL ON workspaces FROM zaki_app;
            """);

        migrationBuilder.Sql($$"""
            DO $$
            DECLARE t text;
            BEGIN
              FOREACH t IN ARRAY ARRAY[{{TenantTables}}] LOOP
                EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I', t);
                EXECUTE format('ALTER TABLE %I NO FORCE ROW LEVEL SECURITY', t);
                EXECUTE format('ALTER TABLE %I DISABLE ROW LEVEL SECURITY', t);
                EXECUTE format('REVOKE ALL ON %I FROM zaki_app', t);
              END LOOP;
            END $$;
            """);

        migrationBuilder.Sql("REVOKE EXECUTE ON FUNCTION app_current_workspace() FROM zaki_app;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS app_current_workspace();");
    }
}
