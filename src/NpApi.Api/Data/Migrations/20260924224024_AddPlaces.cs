using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NpApi.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "place_id",
                table: "photos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "countries",
                columns: table => new
                {
                    code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_countries", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "places",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    state_province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_places", x => x.id);
                    table.CheckConstraint("ck_places_latitude_range", "latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_places_longitude_range", "longitude BETWEEN -180 AND 180");
                    table.CheckConstraint("ck_places_not_empty", "city IS NOT NULL OR state_province IS NOT NULL OR country_code IS NOT NULL OR latitude IS NOT NULL");
                    table.CheckConstraint("ck_places_point_complete", "(latitude IS NULL) = (longitude IS NULL)");
                    table.ForeignKey(
                        name: "fk_places_countries_country_code",
                        column: x => x.country_code,
                        principalTable: "countries",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "countries",
                columns: new[] { "code", "name" },
                values: new object[,]
                {
                    { "CA", "Canada" },
                    { "MX", "Mexico" },
                    { "US", "United States" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_photos_place_id",
                table: "photos",
                column: "place_id");

            migrationBuilder.CreateIndex(
                name: "ix_places_country_code_state_province_city",
                table: "places",
                columns: new[] { "country_code", "state_province", "city" });

            migrationBuilder.AddForeignKey(
                name: "fk_photos_places_place_id",
                table: "photos",
                column: "place_id",
                principalTable: "places",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_photos_places_place_id",
                table: "photos");

            migrationBuilder.DropTable(
                name: "places");

            migrationBuilder.DropTable(
                name: "countries");

            migrationBuilder.DropIndex(
                name: "ix_photos_place_id",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "place_id",
                table: "photos");
        }
    }
}
