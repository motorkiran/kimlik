using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kimlik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SsoConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sso_connections",
                schema: "kimlik",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    issuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    client_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    encrypted_client_secret = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sso_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_sso_connections_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "kimlik",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sso_domains",
                schema: "kimlik",
                columns: table => new
                {
                    domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sso_domains", x => x.domain);
                    table.ForeignKey(
                        name: "fk_sso_domains_sso_connections_connection_id",
                        column: x => x.connection_id,
                        principalSchema: "kimlik",
                        principalTable: "sso_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sso_connections_organization_id",
                schema: "kimlik",
                table: "sso_connections",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_sso_domains_connection_id",
                schema: "kimlik",
                table: "sso_domains",
                column: "connection_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sso_domains",
                schema: "kimlik");

            migrationBuilder.DropTable(
                name: "sso_connections",
                schema: "kimlik");
        }
    }
}
