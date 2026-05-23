using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axis.DataModeling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDataRecordModelForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_data_records_data_models_model_id",
                table: "data_records",
                column: "model_id",
                principalTable: "data_models",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_data_records_data_models_model_id",
                table: "data_records");
        }
    }
}
