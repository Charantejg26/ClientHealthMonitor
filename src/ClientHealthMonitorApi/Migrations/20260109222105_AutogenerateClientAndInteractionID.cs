using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientHealthMonitorApi.Migrations
{
    /// <inheritdoc />
    public partial class AutogenerateClientAndInteractionID : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_clientinteractions_clientid_message_date",
                table: "clientinteractions");

            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                table: "clientinteractions",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_clientinteractions_clientid_message_date",
                table: "clientinteractions",
                columns: new[] { "clientid", "message_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_clientinteractions_clientid_message_date",
                table: "clientinteractions");

            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                table: "clientinteractions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldDefaultValueSql: "gen_random_uuid()");

            migrationBuilder.CreateIndex(
                name: "IX_clientinteractions_clientid_message_date",
                table: "clientinteractions",
                columns: new[] { "clientid", "message_date" });
        }
    }
}
