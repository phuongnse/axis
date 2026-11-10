using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.ControlPlane.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddTermsAcceptances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "terms_acceptances",
                schema: "control_plane",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account = table.Column<string>(type: "text", nullable: false),
                    tenant_id = table.Column<string>(type: "text", nullable: false),
                    document = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<string>(type: "text", nullable: false),
                    privacy_version = table.Column<string>(type: "text", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    client_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terms_acceptances", x => x.id);
                    table.CheckConstraint("ck_terms_acceptances_document", "document IN ('terms', 'dpa')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_terms_acceptances_account",
                schema: "control_plane",
                table: "terms_acceptances",
                column: "account");

            // Terms acceptances are append-only: no statement may change or remove one, raw SQL included. The role that runs the
            // migrations owns the table, so it keeps only SELECT and INSERT. The triggers come first, as the revoke drops TRIGGER.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION control_plane.reject_terms_acceptance_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'control_plane.terms_acceptances is append-only' USING ERRCODE = 'restrict_violation';
                    RETURN NULL;
                END;
                $$;

                CREATE TRIGGER terms_acceptances_no_change
                    BEFORE UPDATE OR DELETE ON control_plane.terms_acceptances
                    FOR EACH ROW EXECUTE FUNCTION control_plane.reject_terms_acceptance_change();

                CREATE TRIGGER terms_acceptances_no_truncate
                    BEFORE TRUNCATE ON control_plane.terms_acceptances
                    FOR EACH STATEMENT EXECUTE FUNCTION control_plane.reject_terms_acceptance_change();

                REVOKE ALL ON control_plane.terms_acceptances FROM PUBLIC;
                REVOKE ALL ON control_plane.terms_acceptances FROM CURRENT_USER;
                GRANT SELECT, INSERT ON control_plane.terms_acceptances TO CURRENT_USER;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER terms_acceptances_no_truncate ON control_plane.terms_acceptances;
                DROP TRIGGER terms_acceptances_no_change ON control_plane.terms_acceptances;
                DROP FUNCTION control_plane.reject_terms_acceptance_change();
                """);

            migrationBuilder.DropTable(
                name: "terms_acceptances",
                schema: "control_plane");
        }
    }
}
