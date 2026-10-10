using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kimlik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SessionClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "session_clients",
                schema: "kimlik",
                columns: table => new
                {
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signed_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_clients", x => new { x.session_id, x.client_id });
                    table.ForeignKey(
                        name: "fk_session_clients_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "kimlik",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_session_clients_user_id",
                schema: "kimlik",
                table: "session_clients",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session_clients",
                schema: "kimlik");
        }
    }
}
