using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindAttic.Legion.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssessmentRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Tier = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    InstrumentSetVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PersonaCount = table.Column<int>(type: "int", nullable: false),
                    CompletedCount = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Personas",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PersonalityMarkdown = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Archetype = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Worldview = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Background = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Age = table.Column<int>(type: "int", nullable: true),
                    Pronouns = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Quirk = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    ProviderId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Personas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemResponses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssessmentRunId = table.Column<int>(type: "int", nullable: false),
                    PersonaId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Instrument = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemResponses_AssessmentRuns_AssessmentRunId",
                        column: x => x.AssessmentRunId,
                        principalTable: "AssessmentRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PsychometricProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonaId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AssessmentRunId = table.Column<int>(type: "int", nullable: false),
                    AdministeredByProvider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AdministeredByModel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    InstrumentSetVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ScoredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Ocean_Openness = table.Column<double>(type: "float", nullable: false),
                    Ocean_Conscientiousness = table.Column<double>(type: "float", nullable: false),
                    Ocean_Extraversion = table.Column<double>(type: "float", nullable: false),
                    Ocean_Agreeableness = table.Column<double>(type: "float", nullable: false),
                    Ocean_Neuroticism = table.Column<double>(type: "float", nullable: false),
                    Hexaco_HonestyHumility = table.Column<double>(type: "float", nullable: false),
                    Hexaco_Emotionality = table.Column<double>(type: "float", nullable: false),
                    Hexaco_Extraversion = table.Column<double>(type: "float", nullable: false),
                    Hexaco_Agreeableness = table.Column<double>(type: "float", nullable: false),
                    Hexaco_Conscientiousness = table.Column<double>(type: "float", nullable: false),
                    Hexaco_Openness = table.Column<double>(type: "float", nullable: false),
                    Mbti_Type = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Mbti_ExtraversionPct = table.Column<double>(type: "float", nullable: false),
                    Mbti_SensingPct = table.Column<double>(type: "float", nullable: false),
                    Mbti_ThinkingPct = table.Column<double>(type: "float", nullable: false),
                    Mbti_JudgingPct = table.Column<double>(type: "float", nullable: false),
                    Enneagram_Type = table.Column<int>(type: "int", nullable: false),
                    Enneagram_Wing = table.Column<int>(type: "int", nullable: true),
                    Enneagram_Triad = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Disc_Dominance = table.Column<double>(type: "float", nullable: false),
                    Disc_Influence = table.Column<double>(type: "float", nullable: false),
                    Disc_Steadiness = table.Column<double>(type: "float", nullable: false),
                    Disc_Conscientiousness = table.Column<double>(type: "float", nullable: false),
                    Disc_PrimaryStyle = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PsychometricProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PsychometricProfiles_AssessmentRuns_AssessmentRunId",
                        column: x => x.AssessmentRunId,
                        principalTable: "AssessmentRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PsychometricProfiles_Personas_PersonaId",
                        column: x => x.PersonaId,
                        principalTable: "Personas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemResponses_AssessmentRunId_PersonaId",
                table: "ItemResponses",
                columns: new[] { "AssessmentRunId", "PersonaId" });

            migrationBuilder.CreateIndex(
                name: "IX_Personas_Archetype",
                table: "Personas",
                column: "Archetype");

            migrationBuilder.CreateIndex(
                name: "IX_Personas_IsDefault",
                table: "Personas",
                column: "IsDefault");

            migrationBuilder.CreateIndex(
                name: "IX_PsychometricProfiles_AssessmentRunId",
                table: "PsychometricProfiles",
                column: "AssessmentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PsychometricProfiles_PersonaId_AssessmentRunId",
                table: "PsychometricProfiles",
                columns: new[] { "PersonaId", "AssessmentRunId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemResponses");

            migrationBuilder.DropTable(
                name: "PsychometricProfiles");

            migrationBuilder.DropTable(
                name: "AssessmentRuns");

            migrationBuilder.DropTable(
                name: "Personas");
        }
    }
}
