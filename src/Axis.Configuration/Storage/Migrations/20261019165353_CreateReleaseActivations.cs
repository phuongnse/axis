using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Configuration.Storage.Migrations
{
    /// <inheritdoc />
    public partial class CreateReleaseActivations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "release_activations",
                schema: "axis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "text", nullable: false),
                    actor_name = table.Column<string>(type: "text", nullable: true),
                    label = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_release_activations", x => x.id);
                    table.ForeignKey(
                        name: "fk_release_activations_releases_release_id",
                        column: x => x.release_id,
                        principalSchema: "axis",
                        principalTable: "releases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_release_activations_application_id_activated_at",
                schema: "axis",
                table: "release_activations",
                columns: new[] { "application_id", "activated_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_release_activations_application_id_release_id",
                schema: "axis",
                table: "release_activations",
                columns: new[] { "application_id", "release_id" });

            migrationBuilder.CreateIndex(
                name: "ix_release_activations_release_id",
                schema: "axis",
                table: "release_activations",
                column: "release_id");

            // The history is append only: no statement may change or remove a row, raw SQL included.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER release_activations_no_change
                    BEFORE UPDATE OR DELETE ON axis.release_activations
                    FOR EACH ROW EXECUTE FUNCTION axis.reject_release_change();
                CREATE TRIGGER release_activations_no_truncate
                    BEFORE TRUNCATE ON axis.release_activations
                    FOR EACH STATEMENT EXECUTE FUNCTION axis.reject_release_change();
                """);

            // The release that is live today gets number 1, so it has a number and a row to roll back to.
            migrationBuilder.Sql(
                """
                INSERT INTO axis.release_activations (id, release_id, application_id, number, content_hash, activated_at, actor)
                SELECT uuidv7(), a.release_id, a.application_id, 1, r.content_hash, a.activated_at, 'unknown'
                FROM axis.active_releases a
                JOIN axis.releases r ON r.id = a.release_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER release_activations_no_truncate ON axis.release_activations;
                DROP TRIGGER release_activations_no_change ON axis.release_activations;
                """);

            migrationBuilder.DropTable(
                name: "release_activations",
                schema: "axis");
        }
    }
}
