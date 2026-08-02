using Escalated.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Escalated.Migrations;

/// <summary>
/// Mentions — records that a host user was @-mentioned inside an internal
/// note, so the mentioned agent can be notified and see the item in their
/// mention inbox. Mirrors the Laravel reference
/// <c>2026_04_07_100001_create_escalated_mentions_table</c>.
/// </summary>
[DbContext(typeof(EscalatedDbContext))]
[Migration("20260802000000_CreateMentions")]
public partial class CreateMentions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "escalated_mentions",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                    .Annotation("SqlServer:Identity", "1, 1")
                    .Annotation("Sqlite:Autoincrement", true),
                ReplyId = table.Column<int>(type: "int", nullable: false),
                UserId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_escalated_mentions", x => x.Id);
                table.ForeignKey(
                    name: "FK_escalated_mentions_escalated_replies_ReplyId",
                    column: x => x.ReplyId,
                    principalTable: "escalated_replies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_escalated_mentions_ReplyId_UserId",
            table: "escalated_mentions",
            columns: ["ReplyId", "UserId"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_escalated_mentions_UserId",
            table: "escalated_mentions",
            column: "UserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "escalated_mentions");
    }
}
