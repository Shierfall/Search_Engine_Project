using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SearchEngine.Persistence.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class quicktest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTokens",
                columns: table => new
                {
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Term = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StartOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    EndOffset = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTokens", x => new { x.DocumentId, x.Position });
                    table.ForeignKey(
                        name: "FK_DocumentTokens_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTokens");

            migrationBuilder.DropTable(
                name: "Documents");
        }
    }
}
