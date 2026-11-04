using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZakiSocial.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "managed_by_workspace_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "must_change_password",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // دور التطبيق بيقرا من users أعمدة محددة بس (الهوية)، فنفتح عمود «المساحة المنشئة» لقائمة الأعضاء.
            // كلمة السر وعلم «لازم يغيّر» بيضلوا مغلقين عنه.
            migrationBuilder.Sql("GRANT SELECT (managed_by_workspace_id) ON users TO zaki_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "managed_by_workspace_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "must_change_password",
                table: "users");
        }
    }
}
