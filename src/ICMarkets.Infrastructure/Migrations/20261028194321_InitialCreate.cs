using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICMarkets.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Snapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Network = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Height = table.Column<long>(type: "bigint", nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LatestUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreviousHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreviousUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PeerCount = table.Column<int>(type: "int", nullable: false),
                    UnconfirmedCount = table.Column<int>(type: "int", nullable: false),
                    HighFeePerKb = table.Column<long>(type: "bigint", nullable: true),
                    MediumFeePerKb = table.Column<long>(type: "bigint", nullable: true),
                    LowFeePerKb = table.Column<long>(type: "bigint", nullable: true),
                    LastForkHeight = table.Column<long>(type: "bigint", nullable: true),
                    LastForkHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Snapshots", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Snapshots");
        }
    }
}
