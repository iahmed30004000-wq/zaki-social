using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZakiSocial.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                table: "media_assets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "content_type",
                table: "media_assets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "duration_seconds",
                table: "media_assets",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "error",
                table: "media_assets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "height",
                table: "media_assets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "original_name",
                table: "media_assets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "processed_key",
                table: "media_assets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "size_bytes",
                table: "media_assets",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "media_assets",
                type: "text",
                nullable: false,
                defaultValue: "processing");

            migrationBuilder.AddColumn<string>(
                name: "thumbnail_key",
                table: "media_assets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "width",
                table: "media_assets",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "post_media",
                columns: table => new
                {
                    post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_post_media", x => new { x.post_id, x.media_id });
                    table.ForeignKey(
                        name: "fk_post_media_media_assets_media_id",
                        column: x => x.media_id,
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_post_media_posts_post_id",
                        column: x => x.post_id,
                        principalTable: "posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_post_media_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_media_assets_status",
                table: "media_assets",
                sql: "status IN ('processing','ready','failed')");

            migrationBuilder.CreateIndex(
                name: "ix_post_media_media_id",
                table: "post_media",
                column: "media_id");

            migrationBuilder.CreateIndex(
                name: "ix_post_media_workspace_id",
                table: "post_media",
                column: "workspace_id");

            // عزل المساحات: كل جدول فيه workspace_id لازم يكون عليه سياسة (دالة enable_tenant_rls من هجرة AuthTablesRls)
            migrationBuilder.Sql("SELECT enable_tenant_rls('post_media');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "post_media");

            migrationBuilder.DropCheckConstraint(
                name: "ck_media_assets_status",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "approved_at",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "content_type",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "duration_seconds",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "error",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "height",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "original_name",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "processed_key",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "size_bytes",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "status",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "thumbnail_key",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "width",
                table: "media_assets");
        }
    }
}
