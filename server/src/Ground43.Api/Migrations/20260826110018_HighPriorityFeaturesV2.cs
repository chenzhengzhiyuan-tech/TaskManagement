using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ground43.Api.Migrations
{
    /// <inheritdoc />
    public partial class HighPriorityFeaturesV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RequirementTypeId",
                table: "Requirements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewerId",
                table: "Requirements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobLocks",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Owner = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobLocks", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "RequirementDefaults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Module = table.Column<string>(type: "TEXT", nullable: true),
                    Priority = table.Column<string>(type: "TEXT", nullable: false),
                    StatusId = table.Column<string>(type: "TEXT", nullable: true),
                    AssigneeId = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewerId = table.Column<string>(type: "TEXT", nullable: true),
                    RequirementTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IterationMode = table.Column<string>(type: "TEXT", nullable: false),
                    IterationId = table.Column<string>(type: "TEXT", nullable: true),
                    DueDateOffsetDays = table.Column<int>(type: "INTEGER", nullable: true),
                    DescriptionTemplate = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequirementDefaults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequirementTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequirementTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemBranding",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectName = table.Column<string>(type: "TEXT", nullable: false),
                    LoginBackgroundUrl = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemBranding", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Requirements_RequirementTypeId",
                table: "Requirements",
                column: "RequirementTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Requirements_ReviewerId",
                table: "Requirements",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_RequirementTypes_Name",
                table: "RequirementTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequirementTypes_SortOrder",
                table: "RequirementTypes",
                column: "SortOrder");

            migrationBuilder.AddForeignKey(
                name: "FK_Requirements_RequirementTypes_RequirementTypeId",
                table: "Requirements",
                column: "RequirementTypeId",
                principalTable: "RequirementTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Requirements_Users_ReviewerId",
                table: "Requirements",
                column: "ReviewerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Requirements_RequirementTypes_RequirementTypeId",
                table: "Requirements");

            migrationBuilder.DropForeignKey(
                name: "FK_Requirements_Users_ReviewerId",
                table: "Requirements");

            migrationBuilder.DropTable(
                name: "JobLocks");

            migrationBuilder.DropTable(
                name: "RequirementDefaults");

            migrationBuilder.DropTable(
                name: "RequirementTypes");

            migrationBuilder.DropTable(
                name: "SystemBranding");

            migrationBuilder.DropIndex(
                name: "IX_Requirements_RequirementTypeId",
                table: "Requirements");

            migrationBuilder.DropIndex(
                name: "IX_Requirements_ReviewerId",
                table: "Requirements");

            migrationBuilder.DropColumn(
                name: "RequirementTypeId",
                table: "Requirements");

            migrationBuilder.DropColumn(
                name: "ReviewerId",
                table: "Requirements");
        }
    }
}
