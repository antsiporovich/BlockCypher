using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICMarkets.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNetworkCreatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Network",
                table: "Snapshots",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_Network_CreatedAt",
                table: "Snapshots",
                columns: new[] { "Network", "CreatedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Snapshots_Network_CreatedAt",
                table: "Snapshots");

            migrationBuilder.AlterColumn<string>(
                name: "Network",
                table: "Snapshots",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
