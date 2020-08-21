using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LiveLinq.EntityFramework.Tests.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Person",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    Name = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Person", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    Description = table.Column<string>(nullable: true),
                    AssignedToForeignKey = table.Column<Guid>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItem_Person_AssignedToForeignKey",
                        column: x => x.AssignedToForeignKey,
                        principalTable: "Person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItem_AssignedToForeignKey",
                table: "WorkItem",
                column: "AssignedToForeignKey");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkItem");

            migrationBuilder.DropTable(
                name: "Person");
        }
    }
}
