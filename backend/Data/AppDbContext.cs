using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<LoginChallenge> LoginChallenges => Set<LoginChallenge>();
    public DbSet<AdminAuditEntry> AdminAuditEntries => Set<AdminAuditEntry>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .HasIndex(user => user.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .Property(user => user.Role)
            .HasConversion<string>()
            .HasMaxLength(20);

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => token.UserId);
            entity.HasIndex(token => token.FamilyId);
            // Of two parallel rotations or revocations only one may save.
            entity.Property(token => token.RevokedAt).IsConcurrencyToken();
            entity.HasOne(token => token.User)
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailVerificationToken>(entity =>
        {
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => token.UserId);
            entity.HasOne(token => token.User)
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => token.UserId);
            entity.HasOne(token => token.User)
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LoginChallenge>(entity =>
        {
            entity.HasIndex(challenge => challenge.UserId);
            // Two requests must not both spend the same attempt or the same code.
            entity.Property(challenge => challenge.Attempts).IsConcurrencyToken();
            entity.Property(challenge => challenge.UsedAt).IsConcurrencyToken();
            entity.HasOne(challenge => challenge.User)
                .WithMany()
                .HasForeignKey(challenge => challenge.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // The audit trail must outlive the accounts it describes, so no cascade from either user.
        modelBuilder.Entity<AdminAuditEntry>(entity =>
        {
            entity.Property(entry => entry.Action).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(entry => entry.AdminId);
            entity.HasIndex(entry => entry.TargetUserId);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(entry => entry.AdminId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(entry => entry.TargetUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.Property(account => account.Name).HasMaxLength(200);
            entity.Property(account => account.Institution).HasMaxLength(200);
            entity.HasIndex(account => account.UserId);
            entity.HasOne(account => account.User)
                .WithMany()
                .HasForeignKey(account => account.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.Property(transaction => transaction.Source).HasConversion<string>().HasMaxLength(20);
            entity.Property(transaction => transaction.Description).HasMaxLength(500);
            entity.Property(transaction => transaction.Category).HasMaxLength(100);
            entity.Property(transaction => transaction.ExternalId).HasMaxLength(200);
            entity.Property(transaction => transaction.Amount).HasPrecision(18, 2);
            // The list query is always "this user, newest first", optionally within a date range.
            entity.HasIndex(transaction => new { transaction.UserId, transaction.Date });
            entity.HasIndex(transaction => transaction.AccountId);
            // Lets P4/P6 skip a row they have already written. Null ids (seed rows) never collide.
            entity.HasIndex(transaction => new { transaction.UserId, transaction.Source, transaction.ExternalId })
                .IsUnique();
            entity.HasOne(transaction => transaction.User)
                .WithMany()
                .HasForeignKey(transaction => transaction.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            // Account and Transaction both cascade from User; NoAction avoids a second cascade path
            // and is checked at statement end, so deleting a user still removes both.
            entity.HasOne(transaction => transaction.Account)
                .WithMany()
                .HasForeignKey(transaction => transaction.AccountId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
