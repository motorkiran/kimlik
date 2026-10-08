using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kimlik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PicturesAndTimeZones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "picture_url",
                schema: "kimlik",
                table: "users",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "time_zone",
                schema: "kimlik",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "picture_url",
                schema: "kimlik",
                table: "organizations",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "picture_url",
                schema: "kimlik",
                table: "users");

            migrationBuilder.DropColumn(
                name: "time_zone",
                schema: "kimlik",
                table: "users");

            migrationBuilder.DropColumn(
                name: "picture_url",
                schema: "kimlik",
                table: "organizations");
        }
    }
}
