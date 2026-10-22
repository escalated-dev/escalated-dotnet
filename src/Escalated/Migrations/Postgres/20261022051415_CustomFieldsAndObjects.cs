using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Escalated.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class CustomFieldsAndObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One value per field and entity: keep the newest of any duplicates before the unique index.
            migrationBuilder.Sql(
                "DELETE FROM \"escalated_custom_field_values\" WHERE \"Id\" NOT IN (SELECT MAX(\"Id\") FROM \"escalated_custom_field_values\" GROUP BY \"CustomFieldId\", \"EntityType\", \"EntityId\")");

            migrationBuilder.DropIndex(
                name: "IX_escalated_custom_field_values_CustomFieldId",
                table: "escalated_custom_field_values");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "escalated_custom_objects",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "escalated_custom_fields",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<string>(
                name: "Context",
                table: "escalated_custom_fields",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "ticket");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "escalated_custom_fields",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Placeholder",
                table: "escalated_custom_fields",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_custom_field_values_CustomFieldId_EntityType_Enti~",
                table: "escalated_custom_field_values",
                columns: new[] { "CustomFieldId", "EntityType", "EntityId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_escalated_custom_field_values_CustomFieldId_EntityType_Enti~",
                table: "escalated_custom_field_values");

            migrationBuilder.DropColumn(
                name: "Context",
                table: "escalated_custom_fields");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "escalated_custom_fields");

            migrationBuilder.DropColumn(
                name: "Placeholder",
                table: "escalated_custom_fields");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "escalated_custom_objects",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "escalated_custom_fields",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.CreateIndex(
                name: "IX_escalated_custom_field_values_CustomFieldId",
                table: "escalated_custom_field_values",
                column: "CustomFieldId");
        }
    }
}
