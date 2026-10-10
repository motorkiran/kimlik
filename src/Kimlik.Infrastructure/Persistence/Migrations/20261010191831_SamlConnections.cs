using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kimlik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SamlConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "encrypted_client_secret",
                schema: "kimlik",
                table: "sso_connections",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "client_id",
                schema: "kimlik",
                table: "sso_connections",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AddColumn<string>(
                name: "certificate",
                schema: "kimlik",
                table: "sso_connections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "protocol",
                schema: "kimlik",
                table: "sso_connections",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "OpenIdConnect");

            migrationBuilder.AddColumn<string>(
                name: "sign_on_url",
                schema: "kimlik",
                table: "sso_connections",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "certificate",
                schema: "kimlik",
                table: "sso_connections");

            migrationBuilder.DropColumn(
                name: "protocol",
                schema: "kimlik",
                table: "sso_connections");

            migrationBuilder.DropColumn(
                name: "sign_on_url",
                schema: "kimlik",
                table: "sso_connections");

            migrationBuilder.AlterColumn<string>(
                name: "encrypted_client_secret",
                schema: "kimlik",
                table: "sso_connections",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "client_id",
                schema: "kimlik",
                table: "sso_connections",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);
        }
    }
}
