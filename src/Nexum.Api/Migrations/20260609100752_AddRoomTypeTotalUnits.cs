using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexum.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomTypeTotalUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TotalUnits",
                table: "RoomTypes",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalUnits",
                table: "RoomTypes");
        }
    }
}
