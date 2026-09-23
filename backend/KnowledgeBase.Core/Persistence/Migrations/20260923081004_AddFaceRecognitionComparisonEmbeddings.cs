using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceRecognitionComparisonEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PhotoCount",
                table: "FaceRecognitionComparisonRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "FaceRecognitionComparisonEmbeddings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RunId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FaceOccurrenceId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModelId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Embedding = table.Column<float[]>(type: "real[]", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceRecognitionComparisonEmbeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceRecognitionComparisonEmbeddings_FaceOccurrences_FaceOcc~",
                        column: x => x.FaceOccurrenceId,
                        principalTable: "FaceOccurrences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FaceRecognitionComparisonEmbeddings_FaceRecognitionComparis~",
                        column: x => x.RunId,
                        principalTable: "FaceRecognitionComparisonRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FaceRecognitionComparisonEmbeddings_FaceOccurrenceId_ModelId",
                table: "FaceRecognitionComparisonEmbeddings",
                columns: new[] { "FaceOccurrenceId", "ModelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaceRecognitionComparisonEmbeddings_RunId",
                table: "FaceRecognitionComparisonEmbeddings",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FaceRecognitionComparisonEmbeddings");

            migrationBuilder.DropColumn(
                name: "PhotoCount",
                table: "FaceRecognitionComparisonRuns");
        }
    }
}
