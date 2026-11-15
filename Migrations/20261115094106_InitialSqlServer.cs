using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace voting.Migrations;
public partial class InitialSqlServer : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "user_accounts", columns: table => new
        {
            id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            full_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
            email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
            normalized_email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
            password_hash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
            role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
            year_level = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
            created_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_user_accounts", x => x.id));
        migrationBuilder.CreateIndex(name: "IX_user_accounts_normalized_email", table: "user_accounts", column: "normalized_email", unique: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "user_accounts");
}
