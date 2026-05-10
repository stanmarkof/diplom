using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace diplom.Migrations
{
    /// <inheritdoc />
    public partial class AddSectionsToDiscipline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SectionId",
                table: "Tests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SectionId",
                table: "Materials",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Sections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    DisciplineId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sections_Disciplines_DisciplineId",
                        column: x => x.DisciplineId,
                        principalTable: "Disciplines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tests_SectionId",
                table: "Tests",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_SectionId",
                table: "Materials",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_DisciplineId",
                table: "Sections",
                column: "DisciplineId");

            migrationBuilder.AddForeignKey(
                name: "FK_Materials_Sections_SectionId",
                table: "Materials",
                column: "SectionId",
                principalTable: "Sections",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Tests_Sections_SectionId",
                table: "Tests",
                column: "SectionId",
                principalTable: "Sections",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Materials_Sections_SectionId",
                table: "Materials");

            migrationBuilder.DropForeignKey(
                name: "FK_Tests_Sections_SectionId",
                table: "Tests");

            migrationBuilder.DropTable(
                name: "Sections");

            migrationBuilder.DropIndex(
                name: "IX_Tests_SectionId",
                table: "Tests");

            migrationBuilder.DropIndex(
                name: "IX_Materials_SectionId",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "SectionId",
                table: "Tests");

            migrationBuilder.DropColumn(
                name: "SectionId",
                table: "Materials");
        }
    }
}
