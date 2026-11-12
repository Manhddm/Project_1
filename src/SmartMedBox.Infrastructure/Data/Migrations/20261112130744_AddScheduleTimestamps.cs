using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartMedBox.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill hàng cũ: CreatedAt/UpdatedAt = giờ UTC lúc migrate (không biết giờ thật), DeletedAt = cùng giá trị cho
            // hàng đã xóa mềm, NULL cho hàng còn sống; default chỉ dùng cho backfill rồi bỏ để app luôn tự set qua IClock.
            const string backfill = "TODATETIMEOFFSET(SYSUTCDATETIME(), 0)";

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "MedicationSchedules",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: backfill);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "MedicationSchedules",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: backfill);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "MedicationSchedules",
                type: "datetimeoffset",
                nullable: true);

            // EXEC: cột mới chưa tồn tại lúc biên dịch batch khi chạy script (schema.sql) một lô.
            migrationBuilder.Sql("EXEC(N'UPDATE [MedicationSchedules] SET [DeletedAt] = [UpdatedAt] WHERE [IsDeleted] = 1;');");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "MedicationSchedules",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldDefaultValueSql: backfill);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "MedicationSchedules",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldDefaultValueSql: backfill);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MedicationSchedules_DeletedAt",
                table: "MedicationSchedules",
                sql: "([IsDeleted] = 1 AND [DeletedAt] IS NOT NULL) OR ([IsDeleted] = 0 AND [DeletedAt] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MedicationSchedules_DeletedAt",
                table: "MedicationSchedules");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "MedicationSchedules");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "MedicationSchedules");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "MedicationSchedules");
        }
    }
}
