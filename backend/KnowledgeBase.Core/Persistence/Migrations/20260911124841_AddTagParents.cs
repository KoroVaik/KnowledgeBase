using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTagParents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TagParents",
                columns: table => new
                {
                    ChildId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParentId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagParents", x => new { x.ChildId, x.ParentId });
                    table.ForeignKey(
                        name: "FK_TagParents_Tags_ChildId",
                        column: x => x.ChildId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TagParents_Tags_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TagParents_ParentId",
                table: "TagParents",
                column: "ParentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TagParents");
        }
    }
}
