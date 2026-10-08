using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kimlik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_keys",
                schema: "kimlik",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    secret_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.id);
                    table.CheckConstraint("ck_api_keys_one_owner", "(user_id IS NULL) <> (organization_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_api_keys_asp_net_users_created_by",
                        column: x => x.created_by,
                        principalSchema: "kimlik",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_api_keys_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "kimlik",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_api_keys_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "kimlik",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "api_key_permissions",
                schema: "kimlik",
                columns: table => new
                {
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_key_permissions", x => new { x.api_key_id, x.permission_id });
                    table.ForeignKey(
                        name: "fk_api_key_permissions_api_keys_api_key_id",
                        column: x => x.api_key_id,
                        principalSchema: "kimlik",
                        principalTable: "api_keys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_api_key_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalSchema: "kimlik",
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_key_permissions_permission_id",
                schema: "kimlik",
                table: "api_key_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_created_by",
                schema: "kimlik",
                table: "api_keys",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_organization_id",
                schema: "kimlik",
                table: "api_keys",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_secret_hash",
                schema: "kimlik",
                table: "api_keys",
                column: "secret_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_user_id",
                schema: "kimlik",
                table: "api_keys",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_key_permissions",
                schema: "kimlik");

            migrationBuilder.DropTable(
                name: "api_keys",
                schema: "kimlik");
        }
    }
}
