using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aaas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventsAndAssociationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssociationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ContactPhone = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    ContactWhatsApp = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssociationSettings", x => x.Id);
                    table.CheckConstraint("CK_AssociationSettings_Singleton", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FirstPublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.CheckConstraint("CK_Events_Capacity", "[Capacity] >= 1");
                    table.CheckConstraint("CK_Events_Dates", "[EndsAt] > [StartsAt]");
                    table.CheckConstraint("CK_Events_Price", "[Price] IS NULL OR [Price] > 0");
                    table.CheckConstraint("CK_Events_Status", "[Status] IN (1, 2, 3)");
                });

            migrationBuilder.InsertData(
                table: "AssociationSettings",
                columns: new[] { "Id", "ContactPhone", "ContactWhatsApp", "UpdatedAt" },
                values: new object[] { 1, null, false, null });

            migrationBuilder.CreateIndex(
                name: "IX_Events_Status_StartsAt",
                table: "Events",
                columns: new[] { "Status", "StartsAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssociationSettings");

            migrationBuilder.DropTable(
                name: "Events");
        }
    }
}
