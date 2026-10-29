using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Escalated.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class PluginSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "escalated_plugins",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.CreateTable(
                name: "escalated_plugin_store",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Plugin = table.Column<string>(type: "nvarchar(191)", maxLength: 191, nullable: false),
                    Collection = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(191)", maxLength: 191, nullable: true),
                    Data = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalated_plugin_store", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_escalated_plugin_store_Plugin_Collection_Key",
                table: "escalated_plugin_store",
                columns: new[] { "Plugin", "Collection", "Key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "escalated_plugin_store");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "escalated_plugins",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255);
        }
    }
}
