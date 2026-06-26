using Microsoft.EntityFrameworkCore;

namespace SonnetHost.StudioStorage;

public sealed class SonnetArtDbContext(DbContextOptions<SonnetArtDbContext> options) : DbContext(options)
{
    public DbSet<StudioSnapshotRecord> Snapshots => Set<StudioSnapshotRecord>();

    public DbSet<StudioAuthSessionRecord> AuthSessions => Set<StudioAuthSessionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StudioSnapshotRecord>(entity =>
        {
            entity.ToTable("sonnet_art_snapshots");
            entity.HasKey(item => item.OwnerKey);
            entity.Property(item => item.OwnerKey).HasColumnType("STRING").ValueGeneratedNever();
            entity.Property(item => item.OwnerUserId).HasColumnType("INT");
            entity.Property(item => item.OwnerEmailHash).HasColumnType("STRING");
            entity.Property(item => item.DeviceKeyHash).HasColumnType("STRING");
            entity.Property(item => item.SnapshotJson).HasColumnType("JSON").IsRequired();
            entity.Property(item => item.CreatedAtUnixMs).HasColumnType("INT");
            entity.Property(item => item.UpdatedAtUnixMs).HasColumnType("INT");
        });

        modelBuilder.Entity<StudioAuthSessionRecord>(entity =>
        {
            entity.ToTable("sonnet_art_auth_sessions");
            entity.HasKey(item => item.SessionIdHash);
            entity.Property(item => item.SessionIdHash).HasColumnType("STRING").ValueGeneratedNever();
            entity.Property(item => item.OwnerKey).HasColumnType("STRING").IsRequired();
            entity.Property(item => item.OwnerUserId).HasColumnType("INT");
            entity.Property(item => item.OwnerEmailHash).HasColumnType("STRING");
            entity.Property(item => item.CreatedAtUnixMs).HasColumnType("INT");
            entity.Property(item => item.UpdatedAtUnixMs).HasColumnType("INT");
            entity.Property(item => item.ExpiresAtUnixMs).HasColumnType("INT");
        });
    }
}

public sealed class StudioSnapshotRecord
{
    public string OwnerKey { get; set; } = string.Empty;

    public long? OwnerUserId { get; set; }

    public string? OwnerEmailHash { get; set; }

    public string? DeviceKeyHash { get; set; }

    public string SnapshotJson { get; set; } = string.Empty;

    public long CreatedAtUnixMs { get; set; }

    public long UpdatedAtUnixMs { get; set; }
}

public sealed class StudioAuthSessionRecord
{
    public string SessionIdHash { get; set; } = string.Empty;

    public string OwnerKey { get; set; } = string.Empty;

    public long OwnerUserId { get; set; }

    public string? OwnerEmailHash { get; set; }

    public long CreatedAtUnixMs { get; set; }

    public long UpdatedAtUnixMs { get; set; }

    public long ExpiresAtUnixMs { get; set; }
}
