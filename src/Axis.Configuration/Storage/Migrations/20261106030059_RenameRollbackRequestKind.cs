using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.Configuration.Storage.Migrations
{
    /// <inheritdoc />
    public partial class RenameRollbackRequestKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // D130 renames the request kind rollback to activate. Queued rows keep running under the new name.
            migrationBuilder.Sql("UPDATE axis.publish_requests SET kind = 'activate' WHERE kind = 'rollback';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE axis.publish_requests SET kind = 'rollback' WHERE kind = 'activate';");
        }
    }
}
