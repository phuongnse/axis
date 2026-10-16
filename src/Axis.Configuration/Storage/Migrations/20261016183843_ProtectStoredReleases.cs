using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Configuration.Storage.Migrations
{
    /// <inheritdoc />
    public partial class ProtectStoredReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A stored release never changes and is never removed: no statement may do it, raw SQL included.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION axis.reject_release_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION '% is immutable', TG_TABLE_NAME USING ERRCODE = 'restrict_violation';
                    RETURN NULL;
                END;
                $$;

                CREATE TRIGGER releases_no_change
                    BEFORE UPDATE OR DELETE ON axis.releases
                    FOR EACH ROW EXECUTE FUNCTION axis.reject_release_change();
                CREATE TRIGGER releases_no_truncate
                    BEFORE TRUNCATE ON axis.releases
                    FOR EACH STATEMENT EXECUTE FUNCTION axis.reject_release_change();

                CREATE TRIGGER release_resources_no_change
                    BEFORE UPDATE OR DELETE ON axis.release_resources
                    FOR EACH ROW EXECUTE FUNCTION axis.reject_release_change();
                CREATE TRIGGER release_resources_no_truncate
                    BEFORE TRUNCATE ON axis.release_resources
                    FOR EACH STATEMENT EXECUTE FUNCTION axis.reject_release_change();

                CREATE TRIGGER release_packages_no_change
                    BEFORE UPDATE OR DELETE ON axis.release_packages
                    FOR EACH ROW EXECUTE FUNCTION axis.reject_release_change();
                CREATE TRIGGER release_packages_no_truncate
                    BEFORE TRUNCATE ON axis.release_packages
                    FOR EACH STATEMENT EXECUTE FUNCTION axis.reject_release_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER release_packages_no_truncate ON axis.release_packages;
                DROP TRIGGER release_packages_no_change ON axis.release_packages;
                DROP TRIGGER release_resources_no_truncate ON axis.release_resources;
                DROP TRIGGER release_resources_no_change ON axis.release_resources;
                DROP TRIGGER releases_no_truncate ON axis.releases;
                DROP TRIGGER releases_no_change ON axis.releases;
                DROP FUNCTION axis.reject_release_change();
                """);
        }
    }
}
