using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Escalated.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class TwoFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "escalated_two_factors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Secret = table.Column<string>(type: "TEXT", nullable: false),
                    RecoveryCodes = table.Column<string>(type: "TEXT", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastUsedStep = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_two_factors", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_two_factors_UserId",
                table: "escalated_two_factors",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "escalated_two_factors");
        }
    }
}
