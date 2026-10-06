using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Data.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreateProvisionedEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "axis");

            migrationBuilder.CreateTable(
                name: "provisioned_entities",
                schema: "axis",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provisioned_entities", x => x.entity_id);
                });

            migrationBuilder.CreateTable(
                name: "provisioned_enum_values",
                schema: "axis",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provisioned_enum_values", x => new { x.entity_id, x.field_name, x.value });
                    table.ForeignKey(
                        name: "fk_provisioned_enum_values_provisioned_entities_entity_id",
                        column: x => x.entity_id,
                        principalSchema: "axis",
                        principalTable: "provisioned_entities",
                        principalColumn: "entity_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_provisioned_entities_application_id",
                schema: "axis",
                table: "provisioned_entities",
                column: "application_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provisioned_enum_values",
                schema: "axis");

            migrationBuilder.DropTable(
                name: "provisioned_entities",
                schema: "axis");
        }
    }
}
