using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyhemsTrafikskola.Infrastructure.Migrations;

public partial class AddIsArchivedToCourseSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsArchived",
            table: "CourseSessions",
            type: "bit",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsArchived",
            table: "CourseSessions");
    }
}
