using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using voting.Data;
using voting.Models;

namespace voting.Migrations;

[DbContext(typeof(VotingDbContext))]
public partial class VotingDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        VotingDbContextModelSnapshotBuilder.Build(modelBuilder);
    }
}

internal static class VotingDbContextModelSnapshotBuilder
{
    public static void Build(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.3")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        modelBuilder.Entity<UserAccount>(account =>
        {
            account.ToTable("user_accounts");
            account.HasKey(user => user.Id);
            account.Property(user => user.Id)
                .ValueGeneratedOnAdd()
                .HasColumnName("id")
                .HasColumnType("integer")
                .HasAnnotation(
                    "Npgsql:ValueGenerationStrategy",
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            account.Property(user => user.FullName)
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnName("full_name")
                .HasColumnType("character varying(100)");
            account.Property(user => user.Email)
                .IsRequired()
                .HasMaxLength(254)
                .HasColumnName("email")
                .HasColumnType("character varying(254)");
            account.Property(user => user.NormalizedEmail)
                .IsRequired()
                .HasMaxLength(254)
                .HasColumnName("normalized_email")
                .HasColumnType("character varying(254)");
            account.Property(user => user.PasswordHash)
                .IsRequired()
                .HasMaxLength(500)
                .HasColumnName("password_hash")
                .HasColumnType("character varying(500)");
            account.Property(user => user.Role)
                .IsRequired()
                .HasMaxLength(20)
                .HasColumnName("role")
                .HasColumnType("character varying(20)");
            account.Property(user => user.YearLevel)
                .HasMaxLength(100)
                .HasColumnName("year_level")
                .HasColumnType("character varying(100)");
            account.Property(user => user.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .HasColumnType("timestamp with time zone");
            account.HasIndex(user => user.NormalizedEmail)
                .IsUnique()
                .HasDatabaseName("IX_user_accounts_normalized_email");
        });

        modelBuilder.Entity<Voter>(student =>
        {
            student.ToTable("students");
            student.HasKey(voter => voter.student_id);
            student.Property(voter => voter.student_id)
                .ValueGeneratedOnAdd()
                .HasColumnName("student_id")
                .HasColumnType("integer")
                .HasAnnotation(
                    "Npgsql:ValueGenerationStrategy",
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            student.Property(voter => voter.student_name)
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnName("student_name")
                .HasColumnType("character varying(100)");
            student.Property(voter => voter.student_email)
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnName("student_email")
                .HasColumnType("character varying(100)");
            student.Property(voter => voter.student_password)
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnName("student_password")
                .HasColumnType("character varying(100)");
            student.Property(voter => voter.year_level)
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnName("year_level")
                .HasColumnType("character varying(100)");
        });
    }
}
