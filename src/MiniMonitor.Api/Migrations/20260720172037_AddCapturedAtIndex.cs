using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniMonitor.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCapturedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_activity_samples_captured_at_utc",
                table: "activity_samples",
                column: "captured_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_activity_samples_captured_at_utc",
                table: "activity_samples");
        }
    }
}
