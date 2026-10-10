using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseInventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Assets_IsDeleted_AssetCode",
                table: "Assets",
                columns: new[] { "IsDeleted", "AssetCode" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_AssignedAt",
                table: "AssetAssignments",
                column: "AssignedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_ReturnedAt",
                table: "AssetAssignments",
                column: "ReturnedAt",
                filter: "[ReturnedAt] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assets_IsDeleted_AssetCode",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_AssetAssignments_AssignedAt",
                table: "AssetAssignments");

            migrationBuilder.DropIndex(
                name: "IX_AssetAssignments_ReturnedAt",
                table: "AssetAssignments");
        }
    }
}
