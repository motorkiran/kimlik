using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kimlik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LoginChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "login_challenges",
                schema: "kimlik",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    browser_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    client_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    return_url = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    scopes = table.Column<List<string>>(type: "text[]", nullable: false),
                    login_hint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    prompt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    requested_strength = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accepted_methods = table.Column<List<string>>(type: "text[]", nullable: false),
                    decided_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_login_challenges", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_login_challenges_challenge_hash",
                schema: "kimlik",
                table: "login_challenges",
                column: "challenge_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_login_challenges_expires_at",
                schema: "kimlik",
                table: "login_challenges",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "login_challenges",
                schema: "kimlik");
        }
    }
}
