using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kimlik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UsageMetering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_metered",
                schema: "kimlik",
                table: "features",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "usage_counters",
                schema: "kimlik",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    used = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_counters", x => x.id);
                    table.CheckConstraint("ck_usage_counters_one_subscriber", "(user_id IS NULL) <> (organization_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_usage_counters_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "kimlik",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_usage_counters_features_feature_id",
                        column: x => x.feature_id,
                        principalSchema: "kimlik",
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_usage_counters_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "kimlik",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usage_records",
                schema: "kimlik",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_records", x => x.id);
                    table.CheckConstraint("ck_usage_records_one_subscriber", "(user_id IS NULL) <> (organization_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_usage_records_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "kimlik",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_usage_records_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "kimlik",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_usage_counters_feature_id",
                schema: "kimlik",
                table: "usage_counters",
                column: "feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_usage_counters_organization_id_feature_id_period",
                schema: "kimlik",
                table: "usage_counters",
                columns: new[] { "organization_id", "feature_id", "period" },
                unique: true,
                filter: "organization_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_usage_counters_period",
                schema: "kimlik",
                table: "usage_counters",
                column: "period");

            migrationBuilder.CreateIndex(
                name: "ix_usage_counters_user_id_feature_id_period",
                schema: "kimlik",
                table: "usage_counters",
                columns: new[] { "user_id", "feature_id", "period" },
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_usage_records_created_at",
                schema: "kimlik",
                table: "usage_records",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_usage_records_organization_id_key",
                schema: "kimlik",
                table: "usage_records",
                columns: new[] { "organization_id", "key" },
                unique: true,
                filter: "organization_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_usage_records_user_id_key",
                schema: "kimlik",
                table: "usage_records",
                columns: new[] { "user_id", "key" },
                unique: true,
                filter: "user_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "usage_counters",
                schema: "kimlik");

            migrationBuilder.DropTable(
                name: "usage_records",
                schema: "kimlik");

            migrationBuilder.DropColumn(
                name: "is_metered",
                schema: "kimlik",
                table: "features");
        }
    }
}
