using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdentityId",
                table: "FaceOccurrences",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FaceIdentities",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssetId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceIdentities_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FaceOccurrences_IdentityId",
                table: "FaceOccurrences",
                column: "IdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_FaceIdentities_AssetId",
                table: "FaceIdentities",
                column: "AssetId");

            migrationBuilder.AddForeignKey(
                name: "FK_FaceOccurrences_FaceIdentities_IdentityId",
                table: "FaceOccurrences",
                column: "IdentityId",
                principalTable: "FaceIdentities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FaceOccurrences_FaceIdentities_IdentityId",
                table: "FaceOccurrences");

            migrationBuilder.DropTable(
                name: "FaceIdentities");

            migrationBuilder.DropIndex(
                name: "IX_FaceOccurrences_IdentityId",
                table: "FaceOccurrences");

            migrationBuilder.DropColumn(
                name: "IdentityId",
                table: "FaceOccurrences");
        }
    }
}
