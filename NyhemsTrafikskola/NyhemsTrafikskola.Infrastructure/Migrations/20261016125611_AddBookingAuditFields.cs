using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyhemsTrafikskola.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingAuditFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CancellationReason",
                table: "CourseBookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "CourseBookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledByUserId",
                table: "CourseBookings",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "CourseBookings",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TermsAcceptedAt",
                table: "CourseBookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsVersion",
                table: "CourseBookings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "CourseBookings");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "CourseBookings");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "CourseBookings");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "CourseBookings");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedAt",
                table: "CourseBookings");

            migrationBuilder.DropColumn(
                name: "TermsVersion",
                table: "CourseBookings");
        }
    }
}
