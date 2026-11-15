using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using voting.Models;

namespace voting.Data;

public class VotingDbContext(DbContextOptions<VotingDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<Voter> Students => Set<Voter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var account = modelBuilder.Entity<UserAccount>();

        account.ToTable("user_accounts");
        account.HasKey(user => user.Id);
        account.Property(user => user.Id).HasColumnName("id");
        account.Property(user => user.FullName).HasColumnName("full_name").HasMaxLength(100).IsRequired();
        account.Property(user => user.Email).HasColumnName("email").HasMaxLength(254).IsRequired();
        account.Property(user => user.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(254).IsRequired();
        account.Property(user => user.PasswordHash).HasColumnName("password_hash").HasMaxLength(500).IsRequired();
        account.Property(user => user.Role).HasColumnName("role").HasMaxLength(20).IsRequired();
        account.Property(user => user.YearLevel).HasColumnName("year_level").HasMaxLength(100);
        account.Property(user => user.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        account.HasIndex(user => user.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("IX_user_accounts_normalized_email");

        var student = modelBuilder.Entity<Voter>();
        student.ToTable("students");
        student.HasKey(voter => voter.student_id);
        student.Property(voter => voter.student_id)
            .ValueGeneratedOnAdd()
            .HasAnnotation(
                "Npgsql:ValueGenerationStrategy",
                NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
    }
}
