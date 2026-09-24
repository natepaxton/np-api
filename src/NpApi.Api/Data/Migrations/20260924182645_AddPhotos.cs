using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NpApi.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cloudinary_public_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    filename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    camera_owner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    location_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    date_taken = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    date_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    uploaded_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_photos", x => x.id);
                    table.CheckConstraint("ck_photos_latitude_range", "latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_photos_location_complete", "(latitude IS NULL) = (longitude IS NULL) AND (latitude IS NULL) = (location_source IS NULL)");
                    table.CheckConstraint("ck_photos_longitude_range", "longitude BETWEEN -180 AND 180");
                });

            migrationBuilder.CreateIndex(
                name: "ix_photos_cloudinary_public_id",
                table: "photos",
                column: "cloudinary_public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_photos_date_taken",
                table: "photos",
                column: "date_taken");

            migrationBuilder.CreateIndex(
                name: "ix_photos_tags",
                table: "photos",
                column: "tags")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "photos");
        }
    }
}
