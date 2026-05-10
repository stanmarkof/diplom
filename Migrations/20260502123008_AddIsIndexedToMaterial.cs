using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace diplom.Migrations
{
    /// <inheritdoc />
    public partial class AddIsIndexedToMaterial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DisciplineId",
                table: "RagChunks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaterialId",
                table: "RagChunks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsIndexed",
                table: "Materials",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisciplineId",
                table: "RagChunks");

            migrationBuilder.DropColumn(
                name: "MaterialId",
                table: "RagChunks");

            migrationBuilder.DropColumn(
                name: "IsIndexed",
                table: "Materials");
        }
    }
}
