using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZakiSocial.Data.Migrations;

/// <summary>
/// يعرّف دالة enable_tenant_rls(table) القابلة لإعادة الاستعمال بكل هجرة بعدين (أي جدول جديد فيه workspace_id)،
/// ويطبّقها على جدول الدعوات. جدول refresh_tokens مش تابع لمساحة وما بنعطي الدور zaki_app أي صلاحية عليه.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261104010615_AuthTablesRls")]
public partial class AuthTablesRls : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION enable_tenant_rls(tbl text) RETURNS void
            LANGUAGE plpgsql
            AS $$
            BEGIN
              EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', tbl);
              EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', tbl);
              EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I', tbl);
              EXECUTE format('CREATE POLICY tenant_isolation ON %I USING (workspace_id = app_current_workspace()) WITH CHECK (workspace_id = app_current_workspace())', tbl);
              EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON %I TO zaki_app', tbl);
            END $$;
            """);

        migrationBuilder.Sql("SELECT enable_tenant_rls('invitations');");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP POLICY IF EXISTS tenant_isolation ON invitations;
            ALTER TABLE invitations NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE invitations DISABLE ROW LEVEL SECURITY;
            REVOKE ALL ON invitations FROM zaki_app;
            DROP FUNCTION IF EXISTS enable_tenant_rls(text);
            """);
    }
}
