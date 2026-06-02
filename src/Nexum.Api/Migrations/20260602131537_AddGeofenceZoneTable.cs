using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Nexum.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGeofenceZoneTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Geometry>(
                name: "Boundary",
                table: "GeofenceZones",
                type: "geometry(Polygon, 4326)",
                nullable: false,
                oldClrType: typeof(Geometry),
                oldType: "geometry");

            migrationBuilder.CreateIndex(
                name: "IX_GeofenceZones_IsActive",
                table: "GeofenceZones",
                column: "IsActive",
                filter: "\"IsActive\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GeofenceZones_IsActive",
                table: "GeofenceZones");

            migrationBuilder.AlterColumn<Geometry>(
                name: "Boundary",
                table: "GeofenceZones",
                type: "geometry",
                nullable: false,
                oldClrType: typeof(Geometry),
                oldType: "geometry(Polygon, 4326)");
        }
    }
}
