using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyhemsTrafikskola.Infrastructure.Migrations;

public partial class AddExpiresAtToCourseBookings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ExpiresAt",
            table: "CourseBookings",
            type: "datetimeoffset",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ExpiresAt",
            table: "CourseBookings");
    }
}
