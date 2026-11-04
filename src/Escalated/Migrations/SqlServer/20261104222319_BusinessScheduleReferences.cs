using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Escalated.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class BusinessScheduleReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BusinessScheduleId",
                table: "escalated_sla_policies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BusinessScheduleId",
                table: "escalated_departments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_sla_policies_BusinessScheduleId",
                table: "escalated_sla_policies",
                column: "BusinessScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_departments_BusinessScheduleId",
                table: "escalated_departments",
                column: "BusinessScheduleId");

            migrationBuilder.AddForeignKey(
                name: "FK_escalated_departments_escalated_business_schedules_BusinessScheduleId",
                table: "escalated_departments",
                column: "BusinessScheduleId",
                principalTable: "escalated_business_schedules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_escalated_sla_policies_escalated_business_schedules_BusinessScheduleId",
                table: "escalated_sla_policies",
                column: "BusinessScheduleId",
                principalTable: "escalated_business_schedules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_escalated_departments_escalated_business_schedules_BusinessScheduleId",
                table: "escalated_departments");

            migrationBuilder.DropForeignKey(
                name: "FK_escalated_sla_policies_escalated_business_schedules_BusinessScheduleId",
                table: "escalated_sla_policies");

            migrationBuilder.DropIndex(
                name: "IX_escalated_sla_policies_BusinessScheduleId",
                table: "escalated_sla_policies");

            migrationBuilder.DropIndex(
                name: "IX_escalated_departments_BusinessScheduleId",
                table: "escalated_departments");

            migrationBuilder.DropColumn(
                name: "BusinessScheduleId",
                table: "escalated_sla_policies");

            migrationBuilder.DropColumn(
                name: "BusinessScheduleId",
                table: "escalated_departments");
        }
    }
}
