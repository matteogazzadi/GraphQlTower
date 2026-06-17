using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GraphQlTower.Api.Data.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UpstreamServices",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                LastStatus = table.Column<int>(type: "INTEGER", nullable: false),
                LastChecked = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_UpstreamServices", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ServiceHeaders",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                UpstreamServiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Value = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ServiceHeaders", x => x.Id);
                table.ForeignKey(
                    name: "FK_ServiceHeaders_UpstreamServices_UpstreamServiceId",
                    column: x => x.UpstreamServiceId,
                    principalTable: "UpstreamServices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_UpstreamServices_Name",
            table: "UpstreamServices",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ServiceHeaders_UpstreamServiceId",
            table: "ServiceHeaders",
            column: "UpstreamServiceId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ServiceHeaders");
        migrationBuilder.DropTable(name: "UpstreamServices");
    }
}
