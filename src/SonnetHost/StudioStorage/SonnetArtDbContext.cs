using Microsoft.EntityFrameworkCore;

namespace SonnetHost.StudioStorage;

public sealed class SonnetArtDbContext(DbContextOptions<SonnetArtDbContext> options) : DbContext(options)
{
    public DbSet<StudioSnapshotRecord> Snapshots => Set<StudioSnapshotRecord>();

    public DbSet<StudioAuthSessionRecord> AuthSessions => Set<StudioAuthSessionRecord>();

    public DbSet<PromptLibraryRecord> PromptLibraryItems => Set<PromptLibraryRecord>();

    public DbSet<StorageMetadataRecord> Metadata => Set<StorageMetadataRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StudioSnapshotRecord>(entity =>
        {
            entity.ToTable("sonnet_art_snapshots");
            entity.HasKey(item => item.OwnerKey);
            entity.Property(item => item.OwnerKey).HasColumnName("owner_key").HasColumnType("text").ValueGeneratedNever();
            entity.Property(item => item.OwnerUserId).HasColumnName("owner_user_id").HasColumnType("bigint");
            entity.Property(item => item.OwnerEmailHash).HasColumnName("owner_email_hash").HasColumnType("text");
            entity.Property(item => item.DeviceKeyHash).HasColumnName("device_key_hash").HasColumnType("text");
            entity.Property(item => item.SnapshotJson).HasColumnName("snapshot_json").HasColumnType("jsonb").IsRequired();
            entity.Property(item => item.CreatedAtUnixMs).HasColumnName("created_at_unix_ms").HasColumnType("bigint");
            entity.Property(item => item.UpdatedAtUnixMs).HasColumnName("updated_at_unix_ms").HasColumnType("bigint");
        });

        modelBuilder.Entity<StudioAuthSessionRecord>(entity =>
        {
            entity.ToTable("sonnet_art_auth_sessions");
            entity.HasKey(item => item.SessionIdHash);
            entity.Property(item => item.SessionIdHash).HasColumnName("session_id_hash").HasColumnType("text").ValueGeneratedNever();
            entity.Property(item => item.OwnerKey).HasColumnName("owner_key").HasColumnType("text").IsRequired();
            entity.Property(item => item.OwnerUserId).HasColumnName("owner_user_id").HasColumnType("bigint");
            entity.Property(item => item.OwnerEmailHash).HasColumnName("owner_email_hash").HasColumnType("text");
            entity.Property(item => item.CreatedAtUnixMs).HasColumnName("created_at_unix_ms").HasColumnType("bigint");
            entity.Property(item => item.UpdatedAtUnixMs).HasColumnName("updated_at_unix_ms").HasColumnType("bigint");
            entity.Property(item => item.ExpiresAtUnixMs).HasColumnName("expires_at_unix_ms").HasColumnType("bigint");
        });

        modelBuilder.Entity<PromptLibraryRecord>(entity =>
        {
            entity.ToTable("sonnet_art_prompt_library");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id").HasColumnType("text").ValueGeneratedNever();
            entity.Property(item => item.TitleZh).HasColumnName("title_zh").HasColumnType("text").IsRequired();
            entity.Property(item => item.TitleEn).HasColumnName("title_en").HasColumnType("text").IsRequired();
            entity.Property(item => item.Source).HasColumnName("source").HasColumnType("text").IsRequired();
            entity.Property(item => item.CategoryZh).HasColumnName("category_zh").HasColumnType("text").IsRequired();
            entity.Property(item => item.CategoryEn).HasColumnName("category_en").HasColumnType("text").IsRequired();
            entity.Property(item => item.DescriptionZh).HasColumnName("description_zh").HasColumnType("text").IsRequired();
            entity.Property(item => item.DescriptionEn).HasColumnName("description_en").HasColumnType("text").IsRequired();
            entity.Property(item => item.PromptZh).HasColumnName("prompt_zh").HasColumnType("text").IsRequired();
            entity.Property(item => item.PromptEn).HasColumnName("prompt_en").HasColumnType("text").IsRequired();
            entity.Property(item => item.SourceUrl).HasColumnName("source_url").HasColumnType("text").IsRequired();
            entity.Property(item => item.Author).HasColumnName("author").HasColumnType("text").IsRequired();
            entity.Property(item => item.NeedsReferenceImages).HasColumnName("needs_reference_images");
            entity.Property(item => item.Language).HasColumnName("language").HasColumnType("text").IsRequired();
            entity.Property(item => item.Tags).HasColumnName("tags").HasColumnType("text[]").IsRequired();
            entity.Property(item => item.PreviewImages).HasColumnName("preview_images").HasColumnType("text[]").IsRequired();
            entity.Property(item => item.SearchText).HasColumnName("search_text").HasColumnType("text").IsRequired();
            entity.Property(item => item.HasBeautyTag).HasColumnName("has_beauty_tag");
            entity.Property(item => item.IsFeaturedWithImage).HasColumnName("is_featured_with_image");
            entity.Property(item => item.PreviewImageCount).HasColumnName("preview_image_count");
        });

        modelBuilder.Entity<StorageMetadataRecord>(entity =>
        {
            entity.ToTable("sonnet_art_metadata");
            entity.HasKey(item => item.Key);
            entity.Property(item => item.Key).HasColumnName("key").HasColumnType("text").ValueGeneratedNever();
            entity.Property(item => item.Value).HasColumnName("value").HasColumnType("text").IsRequired();
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

public sealed class PromptLibraryRecord
{
    public string Id { get; set; } = string.Empty;
    public string TitleZh { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string CategoryZh { get; set; } = string.Empty;
    public string CategoryEn { get; set; } = string.Empty;
    public string DescriptionZh { get; set; } = string.Empty;
    public string DescriptionEn { get; set; } = string.Empty;
    public string PromptZh { get; set; } = string.Empty;
    public string PromptEn { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public bool NeedsReferenceImages { get; set; }
    public string Language { get; set; } = string.Empty;
    public string[] Tags { get; set; } = [];
    public string[] PreviewImages { get; set; } = [];
    public string SearchText { get; set; } = string.Empty;
    public bool HasBeautyTag { get; set; }
    public bool IsFeaturedWithImage { get; set; }
    public int PreviewImageCount { get; set; }
}

public sealed class StorageMetadataRecord
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
