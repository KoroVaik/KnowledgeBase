using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoArchiveFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArchiveEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: true),
                    LocationId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchiveEvents_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ArchiveEventPeople",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PersonId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveEventPeople", x => new { x.EventId, x.PersonId });
                    table.ForeignKey(
                        name: "FK_ArchiveEventPeople_ArchiveEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "ArchiveEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArchiveEventPeople_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArchiveEventPhotos",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssetId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveEventPhotos", x => new { x.EventId, x.AssetId });
                    table.ForeignKey(
                        name: "FK_ArchiveEventPhotos_ArchiveEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "ArchiveEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArchiveEventPhotos_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveEventPeople_PersonId",
                table: "ArchiveEventPeople",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveEventPhotos_AssetId",
                table: "ArchiveEventPhotos",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveEvents_LocationId",
                table: "ArchiveEvents",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Name_Kind",
                table: "Locations",
                columns: new[] { "Name", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_Name",
                table: "People",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchiveEventPeople");

            migrationBuilder.DropTable(
                name: "ArchiveEventPhotos");

            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.DropTable(
                name: "ArchiveEvents");

            migrationBuilder.DropTable(
                name: "Locations");
        }
    }
}
