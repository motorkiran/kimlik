using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kimlik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationMfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "require_mfa",
                schema: "kimlik",
                table: "organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "require_mfa",
                schema: "kimlik",
                table: "organizations");
        }
    }
}
