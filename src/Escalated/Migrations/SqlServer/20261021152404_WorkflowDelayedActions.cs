using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Escalated.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class WorkflowDelayedActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "escalated_delayed_actions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    workflow_id = table.Column<int>(type: "int", nullable: false),
                    ticket_id = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    remaining_actions = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    execute_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Executed = table.Column<bool>(type: "bit", nullable: false),
                    Cancelled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_delayed_actions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_escalated_delayed_actions_escalated_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "escalated_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_escalated_delayed_actions_escalated_workflows_workflow_id",
                        column: x => x.workflow_id,
                        principalTable: "escalated_workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_delayed_actions_execute_at_Executed_Cancelled",
                table: "escalated_delayed_actions",
                columns: new[] { "execute_at", "Executed", "Cancelled" });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_delayed_actions_ticket_id",
                table: "escalated_delayed_actions",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_delayed_actions_workflow_id",
                table: "escalated_delayed_actions",
                column: "workflow_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "escalated_delayed_actions");
        }
    }
}
