using Ground43.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ground43.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260910093000_BatchCreations")]
public sealed class BatchCreations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateTable(
        name: "BatchCreations",
        columns: table => new { UserId = table.Column<string>(nullable: false), RequestId = table.Column<Guid>(nullable: false), PayloadHash = table.Column<string>(nullable: false), ResultJson = table.Column<string>(nullable: false) },
        constraints: table => table.PrimaryKey("PK_BatchCreations", x => new { x.UserId, x.RequestId }));
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("BatchCreations");
}
