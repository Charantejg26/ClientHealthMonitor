using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientHealthMonitorApi.Migrations
{
    /// <inheritdoc />
    public partial class UniqueUserandGmailIdClientInteraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_clientinteractions_userid_message_date",
                table: "clientinteractions");

            migrationBuilder.CreateIndex(
                name: "IX_clientinteractions_userid_gmail_message_id",
                table: "clientinteractions",
                columns: new[] { "userid", "gmail_message_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_clientinteractions_userid_gmail_message_id",
                table: "clientinteractions");

            migrationBuilder.CreateIndex(
                name: "IX_clientinteractions_userid_message_date",
                table: "clientinteractions",
                columns: new[] { "userid", "message_date" },
                unique: true);
        }
    }
}
