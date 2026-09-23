using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceComparisonReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAtUtc",
                table: "FaceComparisonRuns",
                type: "timestamp with time zone",
                nullable: true);

            // Preserve photos that were fully labelled under the earlier per-detection review flow.
            migrationBuilder.Sql("""
                UPDATE "FaceComparisonRuns" AS run
                SET "ReviewedAtUtc" = (
                    SELECT MAX(result."CompletedAtUtc")
                    FROM "FaceComparisonResults" AS result
                    WHERE result."RunId" = run."Id"
                )
                WHERE NOT run."IsSkipped"
                  AND EXISTS (
                      SELECT 1 FROM "FaceComparisonResults" AS result
                      WHERE result."RunId" = run."Id"
                  )
                  AND NOT EXISTS (
                      SELECT 1 FROM "FaceComparisonResults" AS result
                      WHERE result."RunId" = run."Id"
                        AND (result."CompletedAtUtc" IS NULL OR result."Error" IS NOT NULL)
                  )
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "FaceComparisonDetections" AS detection
                      JOIN "FaceComparisonResults" AS result ON result."Id" = detection."ResultId"
                      WHERE result."RunId" = run."Id" AND detection."IsFace" IS NULL
                  );
                UPDATE "FaceComparisonDetections" SET "IsFace" = TRUE WHERE "IsFace" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewedAtUtc",
                table: "FaceComparisonRuns");
        }
    }
}
