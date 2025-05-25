using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SearchEngine.Persistence.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Terms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTokens");

            migrationBuilder.CreateTable(
                name: "DocumentTerms",
                columns: table => new
                {
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Term = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PositionsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTerms", x => new { x.DocumentId, x.Term });
                    table.ForeignKey(
                        name: "FK_DocumentTerms_Documents_DocumentId",
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
                name: "DocumentTerms");

            migrationBuilder.CreateTable(
                name: "DocumentTokens",
                columns: table => new
                {
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    EndOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    StartOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    Term = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
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
    }
}
