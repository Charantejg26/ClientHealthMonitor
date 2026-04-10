using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientHealthMonitorApi.Migrations
{
    /// <inheritdoc />
    public partial class ChangedClientScoreTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "risklevel",
                table: "clientscores");

            migrationBuilder.RenameColumn(
                name: "confidence",
                table: "clientscores",
                newName: "datacoverage");

            migrationBuilder.AlterColumn<int>(
                name: "score",
                table: "clientscores",
                type: "int",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,2)");

            migrationBuilder.AddColumn<int>(
                name: "executionhealth",
                table: "clientscores",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "opportunityboost",
                table: "clientscores",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "relationshiphealth",
                table: "clientscores",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "tier",
                table: "clientscores",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "executionhealth",
                table: "clientscores");

            migrationBuilder.DropColumn(
                name: "opportunityboost",
                table: "clientscores");

            migrationBuilder.DropColumn(
                name: "relationshiphealth",
                table: "clientscores");

            migrationBuilder.DropColumn(
                name: "tier",
                table: "clientscores");

            migrationBuilder.RenameColumn(
                name: "datacoverage",
                table: "clientscores",
                newName: "confidence");

            migrationBuilder.AlterColumn<decimal>(
                name: "score",
                table: "clientscores",
                type: "numeric(5,2)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "risklevel",
                table: "clientscores",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }
    }
}
