using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICMarkets.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddValueObjectsAndDomainEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BaseFee",
                table: "Snapshots",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HighGasPrice",
                table: "Snapshots",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HighPriorityFee",
                table: "Snapshots",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LowGasPrice",
                table: "Snapshots",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LowPriorityFee",
                table: "Snapshots",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MediumGasPrice",
                table: "Snapshots",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MediumPriorityFee",
                table: "Snapshots",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaseFee",
                table: "Snapshots");

            migrationBuilder.DropColumn(
                name: "HighGasPrice",
                table: "Snapshots");

            migrationBuilder.DropColumn(
                name: "HighPriorityFee",
                table: "Snapshots");

            migrationBuilder.DropColumn(
                name: "LowGasPrice",
                table: "Snapshots");

            migrationBuilder.DropColumn(
                name: "LowPriorityFee",
                table: "Snapshots");

            migrationBuilder.DropColumn(
                name: "MediumGasPrice",
                table: "Snapshots");

            migrationBuilder.DropColumn(
                name: "MediumPriorityFee",
                table: "Snapshots");
        }
    }
}
