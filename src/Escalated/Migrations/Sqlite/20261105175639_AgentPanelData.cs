using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Escalated.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AgentPanelData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastCustomerActivityAt",
                table: "escalated_tickets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChatStatus",
                table: "escalated_agent_profiles",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "offline");

            migrationBuilder.CreateTable(
                name: "escalated_customer_presence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubjectType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SubjectId = table.Column<string>(type: "TEXT", maxLength: 191, nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_customer_presence", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_notifications",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "escalated_ticket_reads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TicketId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    LastReadAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_ticket_reads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_ticket_reads_escalated_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "escalated_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_tickets_LastCustomerActivityAt",
                table: "escalated_tickets",
                column: "LastCustomerActivityAt");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_customer_presence_SubjectType_SubjectId",
                table: "escalated_customer_presence",
                columns: new[] { "SubjectType", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_notifications_UserId_ReadAt",
                table: "escalated_notifications",
                columns: new[] { "UserId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_ticket_reads_TicketId",
                table: "escalated_ticket_reads",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_ticket_reads_UserId_TicketId",
                table: "escalated_ticket_reads",
                columns: new[] { "UserId", "TicketId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "escalated_customer_presence");

            migrationBuilder.DropTable(
                name: "escalated_notifications");

            migrationBuilder.DropTable(
                name: "escalated_ticket_reads");

            migrationBuilder.DropIndex(
                name: "IX_escalated_tickets_LastCustomerActivityAt",
                table: "escalated_tickets");

            migrationBuilder.DropColumn(
                name: "LastCustomerActivityAt",
                table: "escalated_tickets");

            migrationBuilder.DropColumn(
                name: "ChatStatus",
                table: "escalated_agent_profiles");
        }
    }
}
