using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Escalated.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class SsoMessageIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "escalated_sso_message_ids",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(191)", maxLength: 191, nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_sso_message_ids", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_sso_message_ids_ExpiresAt",
                table: "escalated_sso_message_ids",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_escalated_sso_message_ids_Kind_MessageId",
                table: "escalated_sso_message_ids",
                columns: new[] { "Kind", "MessageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "escalated_sso_message_ids");
        }
    }
}
