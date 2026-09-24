using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NpApi.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPeople : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "camera_owner",
                table: "photos");

            migrationBuilder.AddColumn<Guid>(
                name: "camera_owner_id",
                table: "photos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    middle_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_people", x => x.id);
                    table.CheckConstraint("ck_people_first_name_not_blank", "btrim(first_name) <> ''");
                });

            migrationBuilder.CreateTable(
                name: "photo_people",
                columns: table => new
                {
                    photo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tagged_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    tagged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_photo_people", x => new { x.photo_id, x.person_id });
                    table.ForeignKey(
                        name: "fk_photo_people_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_photo_people_photos_photo_id",
                        column: x => x.photo_id,
                        principalTable: "photos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_photos_camera_owner_id",
                table: "photos",
                column: "camera_owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_people_first_name_last_name",
                table: "people",
                columns: new[] { "first_name", "last_name" });

            migrationBuilder.CreateIndex(
                name: "ix_photo_people_person_id",
                table: "photo_people",
                column: "person_id");

            migrationBuilder.AddForeignKey(
                name: "fk_photos_people_camera_owner_id",
                table: "photos",
                column: "camera_owner_id",
                principalTable: "people",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_photos_people_camera_owner_id",
                table: "photos");

            migrationBuilder.DropTable(
                name: "photo_people");

            migrationBuilder.DropTable(
                name: "people");

            migrationBuilder.DropIndex(
                name: "ix_photos_camera_owner_id",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "camera_owner_id",
                table: "photos");

            migrationBuilder.AddColumn<string>(
                name: "camera_owner",
                table: "photos",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }
    }
}
