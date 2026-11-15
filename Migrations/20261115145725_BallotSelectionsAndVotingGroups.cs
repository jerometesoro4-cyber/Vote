using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace voting.Migrations
{
    /// <inheritdoc />
    public partial class BallotSelectionsAndVotingGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BallotSelections_BallotId_PositionId",
                table: "BallotSelections");

            migrationBuilder.AddColumn<string>(
                name: "voting_group",
                table: "user_accounts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxSelections",
                table: "Positions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "MinSelections",
                table: "Positions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "RequiredGroup",
                table: "Elections",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Positions_SelectionLimits",
                table: "Positions",
                sql: "[MinSelections] >= 1 AND [MaxSelections] >= [MinSelections]");

            migrationBuilder.CreateIndex(
                name: "IX_BallotSelections_BallotId_CandidateId",
                table: "BallotSelections",
                columns: new[] { "BallotId", "CandidateId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM [BallotSelections]
                    GROUP BY [BallotId], [PositionId]
                    HAVING COUNT(*) > 1
                )
                    THROW 50000, 'Reverting this migration would invalidate ballots with multiple selections.', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Positions_SelectionLimits",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_BallotSelections_BallotId_CandidateId",
                table: "BallotSelections");

            migrationBuilder.DropColumn(
                name: "voting_group",
                table: "user_accounts");

            migrationBuilder.DropColumn(
                name: "MaxSelections",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "MinSelections",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "RequiredGroup",
                table: "Elections");

            migrationBuilder.CreateIndex(
                name: "IX_BallotSelections_BallotId_PositionId",
                table: "BallotSelections",
                columns: new[] { "BallotId", "PositionId" },
                unique: true);
        }
    }
}
