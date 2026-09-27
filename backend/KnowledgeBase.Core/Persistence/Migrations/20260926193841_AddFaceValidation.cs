using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FaceValidationReviewDecisions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    FaceIdentityId = table.Column<string>(type: "character varying(32)", nullable: false),
                    FaceOccurrenceId = table.Column<string>(type: "character varying(32)", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceValidationReviewDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceValidationReviewDecisions_FaceIdentities_FaceIdentityId",
                        column: x => x.FaceIdentityId,
                        principalTable: "FaceIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FaceValidationReviewDecisions_FaceOccurrences_FaceOccurrenc~",
                        column: x => x.FaceOccurrenceId,
                        principalTable: "FaceOccurrences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FaceValidations",
                columns: table => new
                {
                    FaceOccurrenceId = table.Column<string>(type: "character varying(32)", nullable: false),
                    PipelineVersion = table.Column<string>(type: "text", nullable: false),
                    ConfigurationHash = table.Column<string>(type: "text", nullable: false),
                    InputHash = table.Column<string>(type: "text", nullable: false),
                    ModelKey = table.Column<string>(type: "text", nullable: false),
                    MinSidePixels = table.Column<int>(type: "integer", nullable: true),
                    Sharpness112 = table.Column<double>(type: "double precision", nullable: true),
                    TouchesImageEdge = table.Column<bool>(type: "boolean", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: true),
                    Evidence = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceValidations", x => x.FaceOccurrenceId);
                    table.ForeignKey(
                        name: "FK_FaceValidations_FaceOccurrences_FaceOccurrenceId",
                        column: x => x.FaceOccurrenceId,
                        principalTable: "FaceOccurrences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FaceValidationReviewDecisions_FaceIdentityId_DecidedAtUtc",
                table: "FaceValidationReviewDecisions",
                columns: new[] { "FaceIdentityId", "DecidedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FaceValidationReviewDecisions_FaceOccurrenceId",
                table: "FaceValidationReviewDecisions",
                column: "FaceOccurrenceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FaceValidationReviewDecisions");

            migrationBuilder.DropTable(
                name: "FaceValidations");
        }
    }
}
