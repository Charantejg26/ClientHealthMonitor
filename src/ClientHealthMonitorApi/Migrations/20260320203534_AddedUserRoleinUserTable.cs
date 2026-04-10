using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientHealthMonitorApi.Migrations
{
    /// <inheritdoc />
    public partial class AddedUserRoleinUserTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "userrole",
                table: "users",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "userrole",
                table: "users");
        }
    }
}
