using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ground43.Api.Migrations
{
    /// <inheritdoc />
    public partial class WeComEmailIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WeComEmail",
                table: "Users",
                type: "TEXT",
                maxLength: 254,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_WeComUserId",
                table: "Users",
                column: "WeComUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_WeComUserId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "WeComEmail",
                table: "Users");
        }
    }
}
