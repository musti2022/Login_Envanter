using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseInventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignInAuditActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditLogs_Action",
                table: "AuditLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditLogs_Action",
                table: "AuditLogs",
                sql: "[Action] IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditLogs_Action",
                table: "AuditLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditLogs_Action",
                table: "AuditLogs",
                sql: "[Action] IN (1, 2, 3, 4, 5, 6, 7)");
        }
    }
}
