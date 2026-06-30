using Escalated.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Escalated.Migrations;

/// <summary>
/// Ticket followers — host users who follow a ticket and are a notification
/// target alongside the assignee and requester. See issue #92.
/// </summary>
[DbContext(typeof(EscalatedDbContext))]
[Migration("20260630000001_CreateTicketFollowers")]
public partial class CreateTicketFollowers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "escalated_ticket_followers",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                    .Annotation("SqlServer:Identity", "1, 1")
                    .Annotation("Sqlite:Autoincrement", true),
                TicketId = table.Column<int>(type: "int", nullable: false),
                UserId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_escalated_ticket_followers", x => x.Id);
                table.ForeignKey(
                    name: "FK_escalated_ticket_followers_escalated_tickets_TicketId",
                    column: x => x.TicketId,
                    principalTable: "escalated_tickets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_escalated_ticket_followers_TicketId_UserId",
            table: "escalated_ticket_followers",
            columns: ["TicketId", "UserId"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_escalated_ticket_followers_UserId",
            table: "escalated_ticket_followers",
            column: "UserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "escalated_ticket_followers");
    }
}
