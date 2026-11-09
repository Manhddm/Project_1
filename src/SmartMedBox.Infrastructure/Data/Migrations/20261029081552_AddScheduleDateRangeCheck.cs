using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartMedBox.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleDateRangeCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_MedicationSchedules_DateRange",
                table: "MedicationSchedules",
                sql: "[EndDate] IS NULL OR [EndDate] >= [StartDate]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MedicationSchedules_DateRange",
                table: "MedicationSchedules");
        }
    }
}
