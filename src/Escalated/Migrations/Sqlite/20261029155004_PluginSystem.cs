using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Escalated.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class PluginSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "escalated_plugin_store",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Plugin = table.Column<string>(type: "TEXT", maxLength: 191, nullable: false),
                    Collection = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 191, nullable: true),
                    Data = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
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
        }
    }
}
