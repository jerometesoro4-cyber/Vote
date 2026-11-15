using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using voting.Data;

namespace voting.Migrations;

[DbContext(typeof(VotingDbContext))]
[Migration("20261115090000_CreateUserAccounts")]
public partial class CreateUserAccounts : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        VotingDbContextModelSnapshotBuilder.Build(modelBuilder);
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_accounts",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                year_level = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_user_accounts", user => user.id));

        migrationBuilder.CreateIndex(
            name: "IX_user_accounts_normalized_email",
            table: "user_accounts",
            column: "normalized_email",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "user_accounts");
    }
}
