using Ground43.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ground43.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260914120000_CommentCollaboration")]
public sealed class CommentCollaboration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "MentionsJson", table: "Comments", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "CommentId", table: "Attachments", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "ForComment", table: "Attachments", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>(name: "ForComment", table: "UploadSessions", nullable: false, defaultValue: false);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "MentionsJson", table: "Comments");
        migrationBuilder.DropColumn(name: "CommentId", table: "Attachments");
        migrationBuilder.DropColumn(name: "ForComment", table: "Attachments");
        migrationBuilder.DropColumn(name: "ForComment", table: "UploadSessions");
    }
}
