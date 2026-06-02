using Escalated.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Escalated.Migrations;

/// <summary>
/// Ticket subjects — host-app entities a ticket is <em>about</em> (Project, Customer, …),
/// distinct from the requester and the subject line.
/// </summary>
[DbContext(typeof(EscalatedDbContext))]
[Migration("20260529120000")]
public partial class CreateTicketSubjectsTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "escalated_ticket_subjects",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                    .Annotation("SqlServer:Identity", "1, 1")
                    .Annotation("Sqlite:Autoincrement", true),
                TicketId = table.Column<int>(type: "int", nullable: false),
                SubjectType = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                SubjectId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                Role = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                Position = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_escalated_ticket_subjects", x => x.Id);
                table.ForeignKey(
                    name: "FK_escalated_ticket_subjects_escalated_tickets_TicketId",
                    column: x => x.TicketId,
                    principalTable: "escalated_tickets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_escalated_ticket_subjects_SubjectType_SubjectId",
            table: "escalated_ticket_subjects",
            columns: ["SubjectType", "SubjectId"]);

        migrationBuilder.CreateIndex(
            name: "IX_escalated_ticket_subjects_TicketId_SubjectType_SubjectId",
            table: "escalated_ticket_subjects",
            columns: ["TicketId", "SubjectType", "SubjectId"],
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "escalated_ticket_subjects");
    }
}
