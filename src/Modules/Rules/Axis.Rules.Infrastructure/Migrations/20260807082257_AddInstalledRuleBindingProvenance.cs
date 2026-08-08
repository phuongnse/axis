using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Rules.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInstalledRuleBindingProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "installed_component_hash",
                table: "rule_bindings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "installed_component_key",
                table: "rule_bindings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "installed_lease_epoch",
                table: "rule_bindings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "installed_operation_id",
                table: "rule_bindings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "installed_solution_version_id",
                table: "rule_bindings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "installed_step_id",
                table: "rule_bindings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_rule_bindings_workspace_id_installed_component_key",
                table: "rule_bindings",
                columns: new[] { "workspace_id", "installed_component_key" },
                unique: true,
                filter: "installed_component_key IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_rule_bindings_installed_provenance",
                table: "rule_bindings",
                sql: "(installed_solution_version_id IS NULL AND installed_component_key IS NULL AND\n installed_component_hash IS NULL AND installed_operation_id IS NULL AND\n installed_step_id IS NULL AND installed_lease_epoch IS NULL)\nOR\n(installed_solution_version_id IS NOT NULL AND installed_component_key IS NOT NULL AND\n installed_component_hash IS NOT NULL AND installed_operation_id IS NOT NULL AND\n installed_step_id IS NOT NULL AND installed_lease_epoch > 0 AND\n installed_component_key ~ '^[a-z][a-z0-9_.:@-]{0,199}$' AND\n installed_component_hash ~ '^[0-9a-f]{64}$' AND\n installed_solution_version_id <> '00000000-0000-0000-0000-000000000000'::uuid AND\n installed_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND\n installed_step_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_rule_bindings_workspace_id_installed_component_key",
                table: "rule_bindings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_rule_bindings_installed_provenance",
                table: "rule_bindings");

            migrationBuilder.DropColumn(
                name: "installed_component_hash",
                table: "rule_bindings");

            migrationBuilder.DropColumn(
                name: "installed_component_key",
                table: "rule_bindings");

            migrationBuilder.DropColumn(
                name: "installed_lease_epoch",
                table: "rule_bindings");

            migrationBuilder.DropColumn(
                name: "installed_operation_id",
                table: "rule_bindings");

            migrationBuilder.DropColumn(
                name: "installed_solution_version_id",
                table: "rule_bindings");

            migrationBuilder.DropColumn(
                name: "installed_step_id",
                table: "rule_bindings");
        }
    }
}
