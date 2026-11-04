using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZakiSocial.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectedExternalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_id",
                table: "collected_data",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_collected_data_workspace_id_kind_external_id",
                table: "collected_data",
                columns: new[] { "workspace_id", "kind", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_collected_data_workspace_id_kind_external_id",
                table: "collected_data");

            migrationBuilder.DropColumn(
                name: "external_id",
                table: "collected_data");
        }
    }
}
