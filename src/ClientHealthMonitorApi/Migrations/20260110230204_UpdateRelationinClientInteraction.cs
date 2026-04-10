using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientHealthMonitorApi.Migrations
{
    /// <inheritdoc />
    public partial class UpdateRelationinClientInteraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_clientinteractions_clientid_message_date",
                table: "clientinteractions");

            migrationBuilder.DropIndex(
                name: "IX_clientinteractions_userid",
                table: "clientinteractions");

            migrationBuilder.CreateIndex(
                name: "IX_clientinteractions_clientid",
                table: "clientinteractions",
                column: "clientid");

            migrationBuilder.CreateIndex(
                name: "IX_clientinteractions_userid_message_date",
                table: "clientinteractions",
                columns: new[] { "userid", "message_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_clientinteractions_clientid",
                table: "clientinteractions");

            migrationBuilder.DropIndex(
                name: "IX_clientinteractions_userid_message_date",
                table: "clientinteractions");

            migrationBuilder.CreateIndex(
                name: "IX_clientinteractions_clientid_message_date",
                table: "clientinteractions",
                columns: new[] { "clientid", "message_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_clientinteractions_userid",
                table: "clientinteractions",
                column: "userid");
        }
    }
}
