using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientHealthMonitorApi.Migrations
{
    /// <inheritdoc />
    public partial class RemovedColumnsInClientAndClientInteraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "engagementstage",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "emailbody",
                table: "clientinteractions");

            migrationBuilder.DropColumn(
                name: "emailtype",
                table: "clientinteractions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "engagementstage",
                table: "clients",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "emailbody",
                table: "clientinteractions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "emailtype",
                table: "clientinteractions",
                type: "text",
                nullable: true);
        }
    }
}
