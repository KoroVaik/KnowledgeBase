using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineAndNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    SourceAssetId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notes_Assets_SourceAssetId",
                        column: x => x.SourceAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingJobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssetId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessingJobs_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NoteLinks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceNoteId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TargetNoteId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoteLinks_Notes_SourceNoteId",
                        column: x => x.SourceNoteId,
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NoteLinks_Notes_TargetNoteId",
                        column: x => x.TargetNoteId,
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NoteLinks_SourceNoteId_TargetTitle",
                table: "NoteLinks",
                columns: new[] { "SourceNoteId", "TargetTitle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NoteLinks_TargetNoteId",
                table: "NoteLinks",
                column: "TargetNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_NoteLinks_TargetTitle",
                table: "NoteLinks",
                column: "TargetTitle");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_Category",
                table: "Notes",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_SourceAssetId",
                table: "Notes",
                column: "SourceAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_AssetId",
                table: "ProcessingJobs",
                column: "AssetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_Status_CreatedAtUtc",
                table: "ProcessingJobs",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NoteLinks");

            migrationBuilder.DropTable(
                name: "ProcessingJobs");

            migrationBuilder.DropTable(
                name: "Notes");
        }
    }
}
