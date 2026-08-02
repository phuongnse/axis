using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.BusinessObjects.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessObjectRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "binding_revision",
                table: "business_object_definition_version_field_rules",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "binding_revision",
                table: "business_object_definition_field_rules",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "business_object_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_version_number = table.Column<int>(type: "integer", nullable: false),
                    object_key = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rule_evaluations = table.Column<string>(type: "jsonb", nullable: false),
                    values = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_object_records", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_object_records_workspace_id_definition_version_id",
                table: "business_object_records",
                columns: new[] { "workspace_id", "definition_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_business_object_records_workspace_id_object_key_idempotency~",
                table: "business_object_records",
                columns: new[] { "workspace_id", "object_key", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_object_records_workspace_id_object_key_updated_at",
                table: "business_object_records",
                columns: new[] { "workspace_id", "object_key", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_object_records");

            migrationBuilder.DropColumn(
                name: "binding_revision",
                table: "business_object_definition_version_field_rules");

            migrationBuilder.DropColumn(
                name: "binding_revision",
                table: "business_object_definition_field_rules");
        }
    }
}
