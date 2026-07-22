using Anima.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anima.Server.Data;

public class AnimaDbContext(DbContextOptions<AnimaDbContext> options) : DbContext(options)
{
    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();
    public DbSet<PersistedAnimaEntity> PersistedAnimas => Set<PersistedAnimaEntity>();
    public DbSet<PersistedLedgerEntryEntity> LedgerEntries => Set<PersistedLedgerEntryEntity>();
    public DbSet<PasswordResetTokenEntity> PasswordResetTokens => Set<PasswordResetTokenEntity>();
    public DbSet<AccountArtifactStatEntity> ArtifactStats => Set<AccountArtifactStatEntity>();
    public DbSet<PendingWeaveEntity> PendingWeaves => Set<PendingWeaveEntity>();
    public DbSet<PendingPurchasedEmberEntity> PendingPurchasedEmbers => Set<PendingPurchasedEmberEntity>();
    public DbSet<PendingBossHatchEntity> PendingBossHatches => Set<PendingBossHatchEntity>();
    public DbSet<DelveHistoryEntity> DelveHistories => Set<DelveHistoryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountEntity>(e =>
        {
            e.HasIndex(a => a.NormalizedUsername).IsUnique();
            e.Property(a => a.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<PersistedAnimaEntity>(e =>
        {
            e.HasIndex(a => new { a.AccountId, a.AnimaId }).IsUnique();
            e.Property(a => a.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<PersistedLedgerEntryEntity>(e =>
        {
            e.HasIndex(l => new { l.AccountId, l.ResourceType }).IsUnique();
            e.Property(l => l.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<PasswordResetTokenEntity>(e =>
        {
            e.HasIndex(t => t.AccountId);
        });

        modelBuilder.Entity<AccountArtifactStatEntity>(e =>
        {
            e.HasIndex(s => new { s.AccountId, s.ArtifactName }).IsUnique();
            e.Property(s => s.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<PendingWeaveEntity>(e =>
        {
            e.HasIndex(w => w.AccountId).IsUnique();
            e.Property(w => w.Version).IsConcurrencyToken();
        });

        // NOT unique on AccountId -- more than one purchased Ember can be pending at once (a
        // single Shop visit can sell up to 3), unlike PendingWeaveEntity's "at most one" rule.
        modelBuilder.Entity<PendingPurchasedEmberEntity>(e =>
        {
            e.HasIndex(w => w.AccountId);
            e.Property(w => w.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<PendingBossHatchEntity>(e =>
        {
            e.HasIndex(h => h.AccountId).IsUnique();
            e.Property(h => h.Version).IsConcurrencyToken();
        });

        // NOT unique -- up to 5 rows per (AccountId, AnimaId) by design (the capped last-5 log).
        // Indexed on the (AccountId, AnimaId) pair since every real read/trim filters on both.
        modelBuilder.Entity<DelveHistoryEntity>(e =>
        {
            e.HasIndex(h => new { h.AccountId, h.AnimaId });
            e.Property(h => h.Version).IsConcurrencyToken();
        });
    }

    // Sqlite has no server-generated rowversion column, so every IConcurrencyStamped entry gets its
    // Version bumped here, immediately before the real save -- see IConcurrencyStamped's own
    // comment for why this is the mechanism (not a DB trigger/computed column).
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<IConcurrencyStamped>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.Version++;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
