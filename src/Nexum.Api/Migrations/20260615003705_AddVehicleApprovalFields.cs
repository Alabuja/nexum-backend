using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexum.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleApprovalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ShuttleVehicles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "ShuttleVehicles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "ShuttleVehicles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "VehicleType",
                table: "ShuttleVehicles",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ShuttleVehicles");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "ShuttleVehicles");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ShuttleVehicles");

            migrationBuilder.DropColumn(
                name: "VehicleType",
                table: "ShuttleVehicles");
        }
    }
}
