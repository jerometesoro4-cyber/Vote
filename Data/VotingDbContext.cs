using Microsoft.EntityFrameworkCore;
using voting.Models;

namespace voting.Data;

public class VotingDbContext(DbContextOptions<VotingDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<AdminInvitation> AdminInvitations => Set<AdminInvitation>();
    public DbSet<EmailVerificationCode> EmailVerificationCodes => Set<EmailVerificationCode>();
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
        account.Property(user => user.VotingGroup).HasColumnName("voting_group").HasMaxLength(100);
        account.Property(user => user.CreatedAtUtc).HasColumnName("created_at_utc");
        account.Property(user => user.EmailVerifiedAtUtc).HasColumnName("email_verified_at_utc");
        account.Property(user => user.SessionVersion).HasColumnName("session_version").HasDefaultValue(1);
        account.HasIndex(user => user.NormalizedEmail).IsUnique().HasDatabaseName("IX_user_accounts_normalized_email");

        modelBuilder.Entity<AdminInvitation>().HasIndex(i => i.CodeHash).IsUnique();
        modelBuilder.Entity<AdminInvitation>().HasOne(i => i.Creator).WithMany().HasForeignKey(i => i.CreatorId).OnDelete(DeleteBehavior.Restrict);

        var verification = modelBuilder.Entity<EmailVerificationCode>();
        verification.ToTable("EmailVerificationCodes");
        verification.HasKey(v => v.Id);
        verification.Property(v => v.Purpose).HasMaxLength(32).IsRequired();
        verification.Property(v => v.CodeHash).HasMaxLength(64).IsRequired();
        verification.Property(v => v.MaxAttempts).HasDefaultValue(5);
        verification.HasIndex(v => new { v.UserAccountId, v.Purpose, v.CreatedAtUtc });
        verification.HasOne(v => v.UserAccount).WithMany().HasForeignKey(v => v.UserAccountId).OnDelete(DeleteBehavior.Cascade);

        var candidate = modelBuilder.Entity<Candidate>();
        candidate.HasAlternateKey(c => new { c.Id, c.PositionId });
        candidate.Property(c => c.PhotoFileName).HasMaxLength(120);
        candidate.Property(c => c.PhotoContentType).HasMaxLength(50);

        var position = modelBuilder.Entity<Position>();
        position.ToTable("Positions", table =>
            table.HasCheckConstraint("CK_Positions_SelectionLimits", "[MinSelections] >= 1 AND [MaxSelections] >= [MinSelections]"));
        position.Property(p => p.MinSelections).HasDefaultValue(1);
        position.Property(p => p.MaxSelections).HasDefaultValue(1);

        modelBuilder.Entity<Ballot>().HasIndex(b => new { b.UserAccountId, b.ElectionId }).IsUnique();
        modelBuilder.Entity<BallotSelection>().HasIndex(s => new { s.BallotId, s.CandidateId }).IsUnique();
        modelBuilder.Entity<BallotSelection>().HasOne(s => s.Candidate).WithMany()
            .HasForeignKey(s => new { s.CandidateId, s.PositionId }).HasPrincipalKey(c => new { c.Id, c.PositionId })
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BallotSelection>().HasOne(s => s.Position).WithMany().HasForeignKey(s => s.PositionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Ballot>().HasOne(b => b.Election).WithMany().HasForeignKey(b => b.ElectionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
