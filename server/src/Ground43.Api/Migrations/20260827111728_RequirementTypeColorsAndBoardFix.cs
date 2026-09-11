using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ground43.Api.Migrations
{
    /// <inheritdoc />
    public partial class RequirementTypeColorsAndBoardFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "RequirementTypes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Color",
                table: "RequirementTypes");
        }
    }
}
