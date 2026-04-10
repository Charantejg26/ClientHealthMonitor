using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientHealthMonitorApi.Migrations
{
    /// <inheritdoc />
    public partial class UpdatedColumnsInClientAndClientInteractionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_clients_clientscores_ClientScoreId",
                table: "clients");

            migrationBuilder.DropIndex(
                name: "IX_clients_ClientScoreId",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "ClientScoreId",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "health_score",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "last_processed_gmail_id",
                table: "clients");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "clients",
                newName: "engagementstage");

            migrationBuilder.RenameColumn(
                name: "interaction_notes",
                table: "clientinteractions",
                newName: "emailtype");

            migrationBuilder.AddColumn<double>(
                name: "confidence",
                table: "clientscores",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "emailbody",
                table: "clientinteractions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_clientscores_clientid",
                table: "clientscores",
                column: "clientid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_clientscores_clientid",
                table: "clientscores");

            migrationBuilder.DropColumn(
                name: "confidence",
                table: "clientscores");

            migrationBuilder.DropColumn(
                name: "emailbody",
                table: "clientinteractions");

            migrationBuilder.RenameColumn(
                name: "engagementstage",
                table: "clients",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "emailtype",
                table: "clientinteractions",
                newName: "interaction_notes");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientScoreId",
                table: "clients",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "health_score",
                table: "clients",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "last_processed_gmail_id",
                table: "clients",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_clients_ClientScoreId",
                table: "clients",
                column: "ClientScoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_clients_clientscores_ClientScoreId",
                table: "clients",
                column: "ClientScoreId",
                principalTable: "clientscores",
                principalColumn: "id");
        }
    }
}
