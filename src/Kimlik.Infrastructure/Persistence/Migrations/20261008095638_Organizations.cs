using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kimlik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Organizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "organization_id",
                schema: "kimlik",
                table: "audit_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "organizations",
                schema: "kimlik",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organizations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                schema: "kimlik",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memberships", x => x.id);
                    table.ForeignKey(
                        name: "fk_memberships_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "kimlik",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_memberships_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "kimlik",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "membership_roles",
                schema: "kimlik",
                columns: table => new
                {
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_membership_roles", x => new { x.membership_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_membership_roles_memberships_membership_id",
                        column: x => x.membership_id,
                        principalSchema: "kimlik",
                        principalTable: "memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_membership_roles_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "kimlik",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_organization_id",
                schema: "kimlik",
                table: "audit_events",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_membership_roles_role_id",
                schema: "kimlik",
                table: "membership_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_memberships_organization_id_user_id",
                schema: "kimlik",
                table: "memberships",
                columns: new[] { "organization_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_memberships_user_id",
                schema: "kimlik",
                table: "memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_organizations_slug",
                schema: "kimlik",
                table: "organizations",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "membership_roles",
                schema: "kimlik");

            migrationBuilder.DropTable(
                name: "memberships",
                schema: "kimlik");

            migrationBuilder.DropTable(
                name: "organizations",
                schema: "kimlik");

            migrationBuilder.DropIndex(
                name: "ix_audit_events_organization_id",
                schema: "kimlik",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "organization_id",
                schema: "kimlik",
                table: "audit_events");
        }
    }
}
