using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConstrainServiceIdentityClientIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $axis$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM service_identities
                        WHERE char_length(client_id) > 100
                    ) THEN
                        RAISE EXCEPTION 'Cannot constrain service identity client identifiers while values exceed 100 characters.';
                    END IF;
                END
                $axis$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "client_id",
                table: "service_identities",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "client_id",
                table: "service_identities",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }
    }
}
