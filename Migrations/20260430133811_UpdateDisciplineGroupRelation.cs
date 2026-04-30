using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace diplom.Migrations
{
    /// <inheritdoc />
    public partial class UpdateDisciplineGroupRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DisciplineGroups_Disciplines_DisciplinesId",
                table: "DisciplineGroups");

            migrationBuilder.DropForeignKey(
                name: "FK_DisciplineGroups_StudentGroups_StudentGroupsId",
                table: "DisciplineGroups");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DisciplineGroups",
                table: "DisciplineGroups");

            migrationBuilder.DropColumn(
                name: "CourseNumber",
                table: "Disciplines");

            migrationBuilder.RenameTable(
                name: "DisciplineGroups",
                newName: "DisciplineGroupAccess");

            migrationBuilder.RenameColumn(
                name: "StudentGroupsId",
                table: "DisciplineGroupAccess",
                newName: "OpenGroupsId");

            migrationBuilder.RenameIndex(
                name: "IX_DisciplineGroups_StudentGroupsId",
                table: "DisciplineGroupAccess",
                newName: "IX_DisciplineGroupAccess_OpenGroupsId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DisciplineGroupAccess",
                table: "DisciplineGroupAccess",
                columns: new[] { "DisciplinesId", "OpenGroupsId" });

            migrationBuilder.AddForeignKey(
                name: "FK_DisciplineGroupAccess_Disciplines_DisciplinesId",
                table: "DisciplineGroupAccess",
                column: "DisciplinesId",
                principalTable: "Disciplines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DisciplineGroupAccess_StudentGroups_OpenGroupsId",
                table: "DisciplineGroupAccess",
                column: "OpenGroupsId",
                principalTable: "StudentGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DisciplineGroupAccess_Disciplines_DisciplinesId",
                table: "DisciplineGroupAccess");

            migrationBuilder.DropForeignKey(
                name: "FK_DisciplineGroupAccess_StudentGroups_OpenGroupsId",
                table: "DisciplineGroupAccess");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DisciplineGroupAccess",
                table: "DisciplineGroupAccess");

            migrationBuilder.RenameTable(
                name: "DisciplineGroupAccess",
                newName: "DisciplineGroups");

            migrationBuilder.RenameColumn(
                name: "OpenGroupsId",
                table: "DisciplineGroups",
                newName: "StudentGroupsId");

            migrationBuilder.RenameIndex(
                name: "IX_DisciplineGroupAccess_OpenGroupsId",
                table: "DisciplineGroups",
                newName: "IX_DisciplineGroups_StudentGroupsId");

            migrationBuilder.AddColumn<int>(
                name: "CourseNumber",
                table: "Disciplines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_DisciplineGroups",
                table: "DisciplineGroups",
                columns: new[] { "DisciplinesId", "StudentGroupsId" });

            migrationBuilder.AddForeignKey(
                name: "FK_DisciplineGroups_Disciplines_DisciplinesId",
                table: "DisciplineGroups",
                column: "DisciplinesId",
                principalTable: "Disciplines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DisciplineGroups_StudentGroups_StudentGroupsId",
                table: "DisciplineGroups",
                column: "StudentGroupsId",
                principalTable: "StudentGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
