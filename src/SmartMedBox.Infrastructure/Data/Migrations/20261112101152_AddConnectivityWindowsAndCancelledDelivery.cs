using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartMedBox.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectivityWindowsAndCancelledDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EmailDeliveries_Status",
                table: "EmailDeliveries");

            migrationBuilder.CreateTable(
                name: "DeviceConnectivityWindows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OnlineFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceConnectivityWindows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceConnectivityWindows_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmailDeliveries_Status",
                table: "EmailDeliveries",
                sql: "[Status] IN (N'Pending', N'Sent', N'Failed', N'Exhausted', N'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceConnectivityWindows_DeviceId_LastSeenAt",
                table: "DeviceConnectivityWindows",
                columns: new[] { "DeviceId", "LastSeenAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceConnectivityWindows");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EmailDeliveries_Status",
                table: "EmailDeliveries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmailDeliveries_Status",
                table: "EmailDeliveries",
                sql: "[Status] IN (N'Pending', N'Sent', N'Failed', N'Exhausted')");
        }
    }
}
