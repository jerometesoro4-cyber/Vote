using Microsoft.EntityFrameworkCore;
using voting.Models;

namespace voting.Data;

public class VotingDbContext(DbContextOptions<VotingDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<AdminInvitation> AdminInvitations => Set<AdminInvitation>();
    public DbSet<Election> Elections => Set<Election>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<Ballot> Ballots => Set<Ballot>();
    public DbSet<BallotSelection> BallotSelections => Set<BallotSelection>();

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
        account.Property(user => user.CreatedAtUtc).HasColumnName("created_at_utc");
        account.HasIndex(user => user.NormalizedEmail).IsUnique().HasDatabaseName("IX_user_accounts_normalized_email");

        modelBuilder.Entity<AdminInvitation>().HasIndex(i => i.CodeHash).IsUnique();
        modelBuilder.Entity<AdminInvitation>().HasOne(i => i.Creator).WithMany().HasForeignKey(i => i.CreatorId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Ballot>().HasIndex(b => new { b.UserAccountId, b.ElectionId }).IsUnique();
        modelBuilder.Entity<BallotSelection>().HasIndex(s => new { s.BallotId, s.PositionId }).IsUnique();
        modelBuilder.Entity<Candidate>().HasAlternateKey(c => new { c.Id, c.PositionId });
        modelBuilder.Entity<BallotSelection>().HasOne(s => s.Candidate).WithMany()
            .HasForeignKey(s => new { s.CandidateId, s.PositionId }).HasPrincipalKey(c => new { c.Id, c.PositionId })
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BallotSelection>().HasOne(s => s.Position).WithMany().HasForeignKey(s => s.PositionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Ballot>().HasOne(b => b.Election).WithMany().HasForeignKey(b => b.ElectionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
