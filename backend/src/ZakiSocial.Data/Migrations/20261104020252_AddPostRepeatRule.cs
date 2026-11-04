using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZakiSocial.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPostRepeatRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "repeat_rule",
                table: "posts",
                type: "text",
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddCheckConstraint(
                name: "ck_posts_repeat",
                table: "posts",
                sql: "repeat_rule IN ('none','daily','weekly')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_posts_repeat",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "repeat_rule",
                table: "posts");
        }
    }
}
