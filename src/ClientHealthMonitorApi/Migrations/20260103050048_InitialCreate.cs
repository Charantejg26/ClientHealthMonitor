using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientHealthMonitorApi.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    googleid = table.Column<string>(type: "text", nullable: false),
                    given_name = table.Column<string>(type: "text", nullable: false),
                    family_name = table.Column<string>(type: "text", nullable: false),
                    timezone = table.Column<string>(type: "text", nullable: false),
                    lastseenat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    createdat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "authtokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    accesstoken = table.Column<string>(type: "text", nullable: true),
                    refreshtoken = table.Column<string>(type: "text", nullable: true),
                    scope = table.Column<string>(type: "text", nullable: true),
                    last_initial_sync_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accesstoken_expiresat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    createdat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authtokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_authtokens_users_userid",
                        column: x => x.userid,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "clientinteractions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clientid = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    interactiontype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    isoutgoing = table.Column<bool>(type: "boolean", nullable: false),
                    interaction_notes = table.Column<string>(type: "text", nullable: true),
                    message_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    gmail_message_id = table.Column<string>(type: "text", nullable: true),
                    gmail_thread_id = table.Column<string>(type: "text", nullable: true),
                    snippet = table.Column<string>(type: "text", nullable: true),
                    subject = table.Column<string>(type: "text", nullable: true),
                    raw_headers = table.Column<string>(type: "text", nullable: true),
                    parsed_headers = table.Column<string>(type: "text", nullable: true),
                    createdat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clientinteractions", x => x.id);
                    table.ForeignKey(
                        name: "FK_clientinteractions_users_userid",
                        column: x => x.userid,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    client_email = table.Column<string>(type: "text", nullable: false),
                    client_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: false),
                    last_processed_gmail_id = table.Column<string>(type: "text", nullable: true),
                    last_contact_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    health_score = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    createdat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClientScoreId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clients", x => x.id);
                    table.ForeignKey(
                        name: "FK_clients_users_userid",
                        column: x => x.userid,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "clientscores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clientid = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    risklevel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true),
                    calculatedat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    createdat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clientscores", x => x.id);
                    table.ForeignKey(
                        name: "FK_clientscores_clients_clientid",
                        column: x => x.clientid,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    clientid = table.Column<Guid>(type: "uuid", nullable: false),
                    interactionid = table.Column<Guid>(type: "uuid", nullable: true),
                    feedbacktext = table.Column<string>(type: "text", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    createdat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedback", x => x.id);
                    table.ForeignKey(
                        name: "FK_feedback_clientinteractions_interactionid",
                        column: x => x.interactionid,
                        principalTable: "clientinteractions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_feedback_clients_clientid",
                        column: x => x.clientid,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_feedback_users_userid",
                        column: x => x.userid,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_authtokens_userid",
                table: "authtokens",
                column: "userid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_clientinteractions_clientid_message_date",
                table: "clientinteractions",
                columns: new[] { "clientid", "message_date" });

            migrationBuilder.CreateIndex(
                name: "IX_clientinteractions_userid",
                table: "clientinteractions",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "IX_clients_ClientScoreId",
                table: "clients",
                column: "ClientScoreId");

            migrationBuilder.CreateIndex(
                name: "IX_clients_userid_client_name",
                table: "clients",
                columns: new[] { "userid", "client_name" });

            migrationBuilder.CreateIndex(
                name: "IX_clientscores_clientid_calculatedat",
                table: "clientscores",
                columns: new[] { "clientid", "calculatedat" });

            migrationBuilder.CreateIndex(
                name: "IX_feedback_clientid",
                table: "feedback",
                column: "clientid");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_interactionid",
                table: "feedback",
                column: "interactionid");

            migrationBuilder.CreateIndex(
                name: "IX_feedback_userid",
                table: "feedback",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_googleid",
                table: "users",
                column: "googleid",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_clientinteractions_clients_clientid",
                table: "clientinteractions",
                column: "clientid",
                principalTable: "clients",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_clients_clientscores_ClientScoreId",
                table: "clients",
                column: "ClientScoreId",
                principalTable: "clientscores",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_clients_users_userid",
                table: "clients");

            migrationBuilder.DropForeignKey(
                name: "FK_clientscores_clients_clientid",
                table: "clientscores");

            migrationBuilder.DropTable(
                name: "authtokens");

            migrationBuilder.DropTable(
                name: "feedback");

            migrationBuilder.DropTable(
                name: "clientinteractions");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "clients");

            migrationBuilder.DropTable(
                name: "clientscores");
        }
    }
}
