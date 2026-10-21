using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Escalated.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AdminPageColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "escalated_ticket_statuses",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "open");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "escalated_ticket_statuses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "escalated_escalation_rules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TriggerType",
                table: "escalated_escalation_rules",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "time_based");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "escalated_ticket_statuses");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "escalated_ticket_statuses");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "escalated_escalation_rules");

            migrationBuilder.DropColumn(
                name: "TriggerType",
                table: "escalated_escalation_rules");
        }
    }
}
