using Escalated.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Escalated.Migrations;

/// <summary>
/// Newsletter system: lists, list members, templates, campaigns, and per-recipient
/// deliveries, plus the contacts marketing opt-out column. Hand-authored to match
/// <see cref="EscalatedModelConfiguration"/> (the EF model snapshot is intentionally
/// not maintained in this repo; migrations are authored directly, like
/// CreateTicketSubjectsTable).
/// </summary>
[DbContext(typeof(EscalatedDbContext))]
[Migration("20260603000000")]
public partial class CreateNewsletterTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "MarketingOptOutAt",
            table: "escalated_contacts",
            type: "datetime2",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "escalated_newsletter_lists",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                    .Annotation("SqlServer:Identity", "1, 1")
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                FilterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CreatedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_escalated_newsletter_lists", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "escalated_newsletter_templates",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                    .Annotation("SqlServer:Identity", "1, 1")
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                Theme = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                SubjectTemplate = table.Column<string>(type: "nvarchar(998)", maxLength: 998, nullable: true),
                BodyMarkdown = table.Column<string>(type: "nvarchar(max)", nullable: false),
                MergeFieldsSchema = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CreatedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_escalated_newsletter_templates", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "escalated_newsletters",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                    .Annotation("SqlServer:Identity", "1, 1")
                    .Annotation("Sqlite:Autoincrement", true),
                Subject = table.Column<string>(type: "nvarchar(998)", maxLength: 998, nullable: false),
                FromEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                FromName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                ReplyTo = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                TargetListId = table.Column<int>(type: "int", nullable: false),
                TemplateId = table.Column<int>(type: "int", nullable: true),
                Theme = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                BodyMarkdown = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                ScheduledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                SentBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                SummaryTotal = table.Column<int>(type: "int", nullable: false),
                SummarySent = table.Column<int>(type: "int", nullable: false),
                SummaryOpened = table.Column<int>(type: "int", nullable: false),
                SummaryClicked = table.Column<int>(type: "int", nullable: false),
                SummaryBounced = table.Column<int>(type: "int", nullable: false),
                SummaryComplained = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_escalated_newsletters", x => x.Id);
                table.ForeignKey(
                    name: "FK_escalated_newsletters_escalated_newsletter_lists_TargetListId",
                    column: x => x.TargetListId,
                    principalTable: "escalated_newsletter_lists",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_escalated_newsletters_escalated_newsletter_templates_TemplateId",
                    column: x => x.TemplateId,
                    principalTable: "escalated_newsletter_templates",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "escalated_newsletter_list_members",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                    .Annotation("SqlServer:Identity", "1, 1")
                    .Annotation("Sqlite:Autoincrement", true),
                ListId = table.Column<int>(type: "int", nullable: false),
                ContactId = table.Column<int>(type: "int", nullable: false),
                AddedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                AddedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_escalated_newsletter_list_members", x => x.Id);
                table.ForeignKey(
                    name: "FK_escalated_newsletter_list_members_escalated_newsletter_lists_ListId",
                    column: x => x.ListId,
                    principalTable: "escalated_newsletter_lists",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_escalated_newsletter_list_members_escalated_contacts_ContactId",
                    column: x => x.ContactId,
                    principalTable: "escalated_contacts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "escalated_newsletter_deliveries",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                    .Annotation("SqlServer:Identity", "1, 1")
                    .Annotation("Sqlite:Autoincrement", true),
                NewsletterId = table.Column<int>(type: "int", nullable: false),
                ContactId = table.Column<int>(type: "int", nullable: false),
                EmailAtSend = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                TrackingToken = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                OpenedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                LastClickedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ClicksCount = table.Column<int>(type: "int", nullable: false),
                BounceReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                FailureReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                AttemptCount = table.Column<short>(type: "smallint", nullable: false),
                ClaimedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsTest = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_escalated_newsletter_deliveries", x => x.Id);
                table.ForeignKey(
                    name: "FK_escalated_newsletter_deliveries_escalated_newsletters_NewsletterId",
                    column: x => x.NewsletterId,
                    principalTable: "escalated_newsletters",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_escalated_newsletter_deliveries_escalated_contacts_ContactId",
                    column: x => x.ContactId,
                    principalTable: "escalated_contacts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_escalated_contacts_MarketingOptOutAt",
            table: "escalated_contacts",
            column: "MarketingOptOutAt");

        migrationBuilder.CreateIndex(
            name: "IX_escalated_newsletter_lists_Kind",
            table: "escalated_newsletter_lists",
            column: "Kind");
        migrationBuilder.CreateIndex(
            name: "IX_escalated_newsletter_lists_CreatedBy",
            table: "escalated_newsletter_lists",
            column: "CreatedBy");

        migrationBuilder.CreateIndex(
            name: "IX_escalated_newsletter_list_members_ListId_ContactId",
            table: "escalated_newsletter_list_members",
            columns: ["ListId", "ContactId"],
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_escalated_newsletter_list_members_ContactId",
            table: "escalated_newsletter_list_members",
            column: "ContactId");

        migrationBuilder.CreateIndex(
            name: "IX_escalated_newsletters_Status",
            table: "escalated_newsletters",
            column: "Status");
        migrationBuilder.CreateIndex(
            name: "IX_escalated_newsletters_ScheduledAt",
            table: "escalated_newsletters",
            column: "ScheduledAt");
        migrationBuilder.CreateIndex(
            name: "IX_escalated_newsletters_Status_ScheduledAt",
            table: "escalated_newsletters",
            columns: ["Status", "ScheduledAt"]);
        migrationBuilder.CreateIndex(
            name: "IX_escalated_newsletters_CreatedBy",
            table: "escalated_newsletters",
            column: "CreatedBy");
        migrationBuilder.CreateIndex(
            name: "IX_escalated_newsletters_TargetListId",
            table: "escalated_newsletters",
            column: "TargetListId");
        migrationBuilder.CreateIndex(
            name: "IX_escalated_newsletters_TemplateId",
            table: "escalated_newsletters",
            column: "TemplateId");

        migrationBuilder.CreateIndex(
            name: "IX_escalated_newsletter_deliveries_NewsletterId_Status",
            table: "escalated_newsletter_deliveries",
            columns: ["NewsletterId", "Status"]);
        migrationBuilder.CreateIndex(
            name: "IX_escalated_newsletter_deliveries_ContactId",
            table: "escalated_newsletter_deliveries",
            column: "ContactId");
        migrationBuilder.CreateIndex(
            name: "IX_escalated_newsletter_deliveries_Status_ClaimedAt",
            table: "escalated_newsletter_deliveries",
            columns: ["Status", "ClaimedAt"]);
        migrationBuilder.CreateIndex(
            name: "IX_escalated_newsletter_deliveries_TrackingToken",
            table: "escalated_newsletter_deliveries",
            column: "TrackingToken",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "escalated_newsletter_deliveries");
        migrationBuilder.DropTable(name: "escalated_newsletter_list_members");
        migrationBuilder.DropTable(name: "escalated_newsletters");
        migrationBuilder.DropTable(name: "escalated_newsletter_templates");
        migrationBuilder.DropTable(name: "escalated_newsletter_lists");
        migrationBuilder.DropColumn(name: "MarketingOptOutAt", table: "escalated_contacts");
    }
}
