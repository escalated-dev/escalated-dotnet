using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Escalated.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class VerifiedGuestAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GuestAccessExpiresAt",
                table: "escalated_tickets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestAccessHash",
                table: "escalated_tickets",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "escalated_guest_verifications",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    email_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    client_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    code_digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_guest_verifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_tickets_GuestAccessHash",
                table: "escalated_tickets",
                column: "GuestAccessHash");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_guest_verifications_email_digest_CreatedAt",
                table: "escalated_guest_verifications",
                columns: new[] { "email_digest", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_guest_verifications_expires_at",
                table: "escalated_guest_verifications",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "escalated_guest_verifications");

            migrationBuilder.DropIndex(
                name: "IX_escalated_tickets_GuestAccessHash",
                table: "escalated_tickets");

            migrationBuilder.DropColumn(
                name: "GuestAccessExpiresAt",
                table: "escalated_tickets");

            migrationBuilder.DropColumn(
                name: "GuestAccessHash",
                table: "escalated_tickets");
        }
    }
}
