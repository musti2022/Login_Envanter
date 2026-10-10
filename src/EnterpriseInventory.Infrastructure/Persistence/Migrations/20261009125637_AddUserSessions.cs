using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseInventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AdminUserId = table.Column<int>(type: "int", nullable: false),
                    KeyHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastAccessCheckAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastFailedAccessCheckAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClientAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndReason = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                    table.CheckConstraint("CK_UserSessions_Ended", "([EndedAt] IS NULL AND [EndReason] IS NULL) OR ([EndedAt] IS NOT NULL AND [EndReason] IS NOT NULL)");
                    table.CheckConstraint("CK_UserSessions_EndReason", "[EndReason] IS NULL OR [EndReason] IN (1, 2, 3, 4, 5, 6)");
                    table.CheckConstraint("CK_UserSessions_Times", "[LastSeenAt] >= [StartedAt] AND [ExpiresAt] > [StartedAt]");
                    table.ForeignKey(
                        name: "FK_UserSessions_AdminUsers_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_AdminUserId",
                table: "UserSessions",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_KeyHash",
                table: "UserSessions",
                column: "KeyHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSessions");
        }
    }
}
