using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZakiSocial.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptTaskAndPostApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "task",
                table: "prompts",
                type: "text",
                nullable: false,
                defaultValue: "caption");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                table: "posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by",
                table: "posts",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "task",
                table: "prompts");

            migrationBuilder.DropColumn(
                name: "approved_at",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "approved_by",
                table: "posts");
        }
    }
}
