using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kimlik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnsAndOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "kimlik",
                table: "plans",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Base");

            migrationBuilder.CreateTable(
                name: "subscription_add_on",
                schema: "kimlik",
                columns: table => new
                {
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_add_on", x => new { x.subscription_id, x.plan_id });
                    table.ForeignKey(
                        name: "fk_subscription_add_on_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "kimlik",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscription_add_on_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "kimlik",
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscription_feature_override",
                schema: "kimlik",
                columns: table => new
                {
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    limit = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_feature_override", x => new { x.subscription_id, x.feature_id });
                    table.ForeignKey(
                        name: "fk_subscription_feature_override_features_feature_id",
                        column: x => x.feature_id,
                        principalSchema: "kimlik",
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subscription_feature_override_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "kimlik",
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_add_on_plan_id",
                schema: "kimlik",
                table: "subscription_add_on",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_feature_override_feature_id",
                schema: "kimlik",
                table: "subscription_feature_override",
                column: "feature_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_add_on",
                schema: "kimlik");

            migrationBuilder.DropTable(
                name: "subscription_feature_override",
                schema: "kimlik");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "kimlik",
                table: "plans");
        }
    }
}
