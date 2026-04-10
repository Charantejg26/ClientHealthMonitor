using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientHealthMonitorApi.Migrations
{
    /// <inheritdoc />
    public partial class AddedLastGmailHistoryIDAuthTokensTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "gmail_last_history_id",
                table: "authtokens",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "gmail_last_history_id",
                table: "authtokens");
        }
    }
}
