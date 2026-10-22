using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Escalated.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class ImportJobUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "escalated_import_jobs",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UserId",
                table: "escalated_import_jobs");
        }
    }
}
