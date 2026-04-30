using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace diplom.Migrations
{
    /// <inheritdoc />
    public partial class lecturersdisc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Disciplines_AspNetUsers_LecturerId",
                table: "Disciplines");

            migrationBuilder.DropIndex(
                name: "IX_Disciplines_LecturerId",
                table: "Disciplines");

            migrationBuilder.RenameColumn(
                name: "LecturerId",
                table: "Disciplines",
                newName: "CourseNumber");

            migrationBuilder.CreateTable(
                name: "DisciplineLecturers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisciplineId = table.Column<int>(type: "int", nullable: false),
                    LecturerId = table.Column<int>(type: "int", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisciplineLecturers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DisciplineLecturers_AspNetUsers_LecturerId",
                        column: x => x.LecturerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DisciplineLecturers_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DisciplineLecturers_DisciplineId_LecturerId",
                table: "DisciplineLecturers",
                columns: new[] { "DisciplineId", "LecturerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisciplineLecturers_LecturerId",
                table: "DisciplineLecturers",
                column: "LecturerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DisciplineLecturers");

            migrationBuilder.RenameColumn(
                name: "CourseNumber",
                table: "Disciplines",
                newName: "LecturerId");

            migrationBuilder.CreateIndex(
                name: "IX_Disciplines_LecturerId",
                table: "Disciplines",
                column: "LecturerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Disciplines_AspNetUsers_LecturerId",
                table: "Disciplines",
                column: "LecturerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
