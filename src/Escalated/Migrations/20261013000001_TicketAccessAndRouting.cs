using Escalated.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Escalated.Migrations;

[DbContext(typeof(EscalatedDbContext))]
[Migration("20261013000001_TicketAccessAndRouting")]
public sealed class TicketAccessAndRouting : Migration
{
    private string AccountCollation => ActiveProvider?.Contains("Npgsql") == true ? "C" : ActiveProvider?.Contains("SqlServer") == true ? "Latin1_General_100_BIN2" : "BINARY";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("AccountType", "escalated_tickets", maxLength: 191, nullable: true, collation: AccountCollation);
        migrationBuilder.AddColumn<string>("AccountId", "escalated_tickets", maxLength: 191, nullable: true, collation: AccountCollation);
        migrationBuilder.CreateIndex("IX_escalated_tickets_AccountType_AccountId", "escalated_tickets", new[] { "AccountType", "AccountId" });
        migrationBuilder.AddColumn<int>("DepartmentId", "escalated_sla_policies", nullable: true);
        migrationBuilder.AddColumn<string>("TicketType", "escalated_sla_policies", maxLength: 50, nullable: true);
        migrationBuilder.CreateIndex("IX_escalated_sla_policies_DepartmentId_TicketType", "escalated_sla_policies", new[] { "DepartmentId", "TicketType" });
        migrationBuilder.AddForeignKey("FK_escalated_sla_policies_escalated_departments_DepartmentId", "escalated_sla_policies", "DepartmentId", "escalated_departments", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.CreateTable("escalated_department_agent", table => new
        {
            DepartmentId = table.Column<int>(nullable: false),
            AgentId = table.Column<string>(maxLength: 255, nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_escalated_department_agent", x => new { x.DepartmentId, x.AgentId });
            table.ForeignKey("FK_escalated_department_agent_escalated_departments_DepartmentId", x => x.DepartmentId, "escalated_departments", "Id", onDelete: ReferentialAction.Cascade);
        });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("escalated_department_agent");
        migrationBuilder.DropForeignKey("FK_escalated_sla_policies_escalated_departments_DepartmentId", "escalated_sla_policies");
        migrationBuilder.DropIndex("IX_escalated_sla_policies_DepartmentId_TicketType", "escalated_sla_policies");
        migrationBuilder.DropColumn("DepartmentId", "escalated_sla_policies");
        migrationBuilder.DropColumn("TicketType", "escalated_sla_policies");
        migrationBuilder.DropIndex("IX_escalated_tickets_AccountType_AccountId", "escalated_tickets");
        migrationBuilder.DropColumn("AccountType", "escalated_tickets");
        migrationBuilder.DropColumn("AccountId", "escalated_tickets");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        EscalatedModelConfiguration.Configure(modelBuilder);
        modelBuilder.Entity<Escalated.Models.Ticket>().Property(t => t.AccountType).UseCollation(AccountCollation);
        modelBuilder.Entity<Escalated.Models.Ticket>().Property(t => t.AccountId).UseCollation(AccountCollation);
    }
}
