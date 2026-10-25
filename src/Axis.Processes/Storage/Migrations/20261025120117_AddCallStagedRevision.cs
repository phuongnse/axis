using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Processes.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddCallStagedRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "staged_revision",
                schema: "axis",
                table: "connector_calls",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "staged_revision",
                schema: "axis",
                table: "package_calls",
                type: "bigint",
                nullable: true);

            // A call was staged by the run whose history row names it, so it takes that row's revision. A
            // call with no such row takes the instance's revision. Existing rows are unique per instance
            // and step, so either way the new index holds.
            migrationBuilder.Sql(
                """
                UPDATE axis.connector_calls c SET staged_revision = COALESCE(
                    (SELECT max(h.revision) FROM axis.process_step_history h
                     WHERE h.process_instance_id = c.process_instance_id AND h.step = c.step
                       AND h.output->>'operationId' = c.id::text AND h.output->>'state' = 'waiting'),
                    (SELECT i.revision FROM axis.process_instances i WHERE i.id = c.process_instance_id));
                """);

            // A call was staged by the run whose history row names it, so it takes that row's revision. A
            // call with no such row takes the instance's revision. Existing rows are unique per instance
            // and step, so either way the new index holds.
            migrationBuilder.Sql(
                """
                UPDATE axis.package_calls c SET staged_revision = COALESCE(
                    (SELECT max(h.revision) FROM axis.process_step_history h
                     WHERE h.process_instance_id = c.process_instance_id AND h.step = c.step
                       AND h.output->>'operationId' = c.id::text AND h.output->>'state' = 'waiting'),
                    (SELECT i.revision FROM axis.process_instances i WHERE i.id = c.process_instance_id));
                """);

            migrationBuilder.AlterColumn<long>(
                name: "staged_revision",
                schema: "axis",
                table: "connector_calls",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "staged_revision",
                schema: "axis",
                table: "package_calls",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "ux_package_calls_process_instance_id_step",
                schema: "axis",
                table: "package_calls");

            migrationBuilder.DropIndex(
                name: "ux_connector_calls_process_instance_id_step",
                schema: "axis",
                table: "connector_calls");

            migrationBuilder.CreateIndex(
                name: "ux_package_calls_process_instance_id_step_staged_revision",
                schema: "axis",
                table: "package_calls",
                columns: new[] { "process_instance_id", "step", "staged_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_connector_calls_process_instance_id_step_staged_revision",
                schema: "axis",
                table: "connector_calls",
                columns: new[] { "process_instance_id", "step", "staged_revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_package_calls_process_instance_id_step_staged_revision",
                schema: "axis",
                table: "package_calls");

            migrationBuilder.DropIndex(
                name: "ux_connector_calls_process_instance_id_step_staged_revision",
                schema: "axis",
                table: "connector_calls");

            migrationBuilder.DropColumn(
                name: "staged_revision",
                schema: "axis",
                table: "package_calls");

            migrationBuilder.DropColumn(
                name: "staged_revision",
                schema: "axis",
                table: "connector_calls");

            migrationBuilder.CreateIndex(
                name: "ux_package_calls_process_instance_id_step",
                schema: "axis",
                table: "package_calls",
                columns: new[] { "process_instance_id", "step" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_connector_calls_process_instance_id_step",
                schema: "axis",
                table: "connector_calls",
                columns: new[] { "process_instance_id", "step" },
                unique: true);
        }
    }
}
