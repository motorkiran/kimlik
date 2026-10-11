using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kimlik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UsageRecordFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotency keys are kept for a day and did not name their feature; those taken before now are let go.
            migrationBuilder.Sql("DELETE FROM kimlik.usage_records;");

            migrationBuilder.DropIndex(
                name: "ix_usage_records_organization_id_key",
                schema: "kimlik",
                table: "usage_records");

            migrationBuilder.DropIndex(
                name: "ix_usage_records_user_id_key",
                schema: "kimlik",
                table: "usage_records");

            migrationBuilder.AddColumn<Guid>(
                name: "feature_id",
                schema: "kimlik",
                table: "usage_records",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_usage_records_feature_id",
                schema: "kimlik",
                table: "usage_records",
                column: "feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_usage_records_organization_id_feature_id_key",
                schema: "kimlik",
                table: "usage_records",
                columns: new[] { "organization_id", "feature_id", "key" },
                unique: true,
                filter: "organization_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_usage_records_user_id_feature_id_key",
                schema: "kimlik",
                table: "usage_records",
                columns: new[] { "user_id", "feature_id", "key" },
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_usage_records_features_feature_id",
                schema: "kimlik",
                table: "usage_records",
                column: "feature_id",
                principalSchema: "kimlik",
                principalTable: "features",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_usage_records_features_feature_id",
                schema: "kimlik",
                table: "usage_records");

            migrationBuilder.DropIndex(
                name: "ix_usage_records_feature_id",
                schema: "kimlik",
                table: "usage_records");

            migrationBuilder.DropIndex(
                name: "ix_usage_records_organization_id_feature_id_key",
                schema: "kimlik",
                table: "usage_records");

            migrationBuilder.DropIndex(
                name: "ix_usage_records_user_id_feature_id_key",
                schema: "kimlik",
                table: "usage_records");

            migrationBuilder.DropColumn(
                name: "feature_id",
                schema: "kimlik",
                table: "usage_records");

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
    }
}
