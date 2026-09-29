using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyhemsTrafikskola.Infrastructure.Migrations;

public partial class AddIsCancelledToCourseSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsCancelled",
            table: "CourseSessions",
            type: "bit",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsCancelled",
            table: "CourseSessions");
    }
}
