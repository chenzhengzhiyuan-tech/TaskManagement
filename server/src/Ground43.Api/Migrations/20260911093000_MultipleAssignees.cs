using Ground43.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ground43.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260911093000_MultipleAssignees")]
public sealed class MultipleAssignees : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(name: "AssigneeIdsJson", table: "Requirements", nullable: true);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(name: "AssigneeIdsJson", table: "Requirements");
}
