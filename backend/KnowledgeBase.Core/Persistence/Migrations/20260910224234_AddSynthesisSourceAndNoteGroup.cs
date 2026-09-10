using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSynthesisSourceAndNoteGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SynthesisGroup",
                table: "Notes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SynthesisSources",
                columns: table => new
                {
                    SynthesisNoteId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InputNoteId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SynthesisSources", x => new { x.SynthesisNoteId, x.InputNoteId });
                    table.ForeignKey(
                        name: "FK_SynthesisSources_Notes_InputNoteId",
                        column: x => x.InputNoteId,
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SynthesisSources_Notes_SynthesisNoteId",
                        column: x => x.SynthesisNoteId,
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_Kind_SynthesisGroup",
                table: "Notes",
                columns: new[] { "Kind", "SynthesisGroup" },
                unique: true,
                filter: "\"SynthesisGroup\" IS NOT NULL AND \"DeletedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SynthesisSources_InputNoteId",
                table: "SynthesisSources",
                column: "InputNoteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SynthesisSources");

            migrationBuilder.DropIndex(
                name: "IX_Notes_Kind_SynthesisGroup",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "SynthesisGroup",
                table: "Notes");
        }
    }
}
