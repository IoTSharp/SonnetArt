using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SonnetArt.Models;

namespace SonnetHost.StudioStorage;

public sealed class SonnetArtStorageSchemaInitializer
{
    private const string PromptLibraryHashKey = "prompt-library-sha256";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SonnetArtStorageSchemaInitializer> _logger;

    public SonnetArtStorageSchemaInitializer(
        IServiceScopeFactory scopeFactory,
        IWebHostEnvironment environment,
        ILogger<SonnetArtStorageSchemaInitializer> logger)
    {
        _scopeFactory = scopeFactory;
        _environment = environment;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SonnetArtDbContext>();
            await CreateSchemaAsync(db, cancellationToken);
            await SeedPromptLibraryAsync(db, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "SonnetArt PostgreSQL schema initialization failed.");
            throw;
        }
    }

    private static async Task CreateSchemaAsync(SonnetArtDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS sonnet_art_snapshots (
                owner_key text PRIMARY KEY,
                owner_user_id bigint NULL,
                owner_email_hash text NULL,
                device_key_hash text NULL,
                snapshot_json jsonb NOT NULL,
                created_at_unix_ms bigint NOT NULL,
                updated_at_unix_ms bigint NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sonnet_art_auth_sessions (
                session_id_hash text PRIMARY KEY,
                owner_key text NOT NULL,
                owner_user_id bigint NOT NULL,
                owner_email_hash text NULL,
                created_at_unix_ms bigint NOT NULL,
                updated_at_unix_ms bigint NOT NULL,
                expires_at_unix_ms bigint NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_sonnet_art_auth_sessions_owner_key
                ON sonnet_art_auth_sessions (owner_key);
            CREATE INDEX IF NOT EXISTS ix_sonnet_art_auth_sessions_expires_at
                ON sonnet_art_auth_sessions (expires_at_unix_ms);

            CREATE TABLE IF NOT EXISTS sonnet_art_prompt_library (
                id text PRIMARY KEY,
                title_zh text NOT NULL,
                title_en text NOT NULL,
                source text NOT NULL,
                category_zh text NOT NULL,
                category_en text NOT NULL,
                description_zh text NOT NULL,
                description_en text NOT NULL,
                prompt_zh text NOT NULL,
                prompt_en text NOT NULL,
                source_url text NOT NULL,
                author text NOT NULL,
                needs_reference_images boolean NOT NULL,
                language text NOT NULL,
                tags text[] NOT NULL,
                preview_images text[] NOT NULL,
                search_text text NOT NULL,
                has_beauty_tag boolean NOT NULL,
                is_featured_with_image boolean NOT NULL,
                preview_image_count integer NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_sonnet_art_prompt_library_source
                ON sonnet_art_prompt_library (source);
            CREATE INDEX IF NOT EXISTS ix_sonnet_art_prompt_library_category_zh
                ON sonnet_art_prompt_library (category_zh);
            CREATE INDEX IF NOT EXISTS ix_sonnet_art_prompt_library_category_en
                ON sonnet_art_prompt_library (category_en);

            CREATE TABLE IF NOT EXISTS sonnet_art_metadata (
                key text PRIMARY KEY,
                value text NOT NULL
            );
            """, cancellationToken);
    }

    private async Task SeedPromptLibraryAsync(SonnetArtDbContext db, CancellationToken cancellationToken)
    {
        var sourcePath = Path.Combine(_environment.ContentRootPath, "SeedData", "prompt-library.json");
        if (!File.Exists(sourcePath))
        {
            _logger.LogWarning("Prompt library seed file was not found at {Path}.", sourcePath);
            return;
        }

        var sourceHash = Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(sourcePath, cancellationToken)))
            .ToLowerInvariant();
        var currentHash = await db.Metadata
            .AsNoTracking()
            .Where(item => item.Key == PromptLibraryHashKey)
            .Select(item => item.Value)
            .SingleOrDefaultAsync(
                cancellationToken);
        if (currentHash == sourceHash && await db.PromptLibraryItems.AnyAsync(cancellationToken))
        {
            return;
        }

        await using var stream = File.OpenRead(sourcePath);
        var items = await JsonSerializer.DeserializeAsync<List<PromptLibraryItem>>(
            stream,
            JsonOptions,
            cancellationToken) ?? [];

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.PromptLibraryItems.ExecuteDeleteAsync(cancellationToken);
            db.PromptLibraryItems.AddRange(items.Select(ToRecord));

            var state = await db.Metadata.SingleOrDefaultAsync(
                item => item.Key == PromptLibraryHashKey,
                cancellationToken);
            if (state is null)
            {
                state = new StorageMetadataRecord { Key = PromptLibraryHashKey };
                db.Metadata.Add(state);
            }

            state.Value = sourceHash;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
        _logger.LogInformation("Seeded {Count} prompt library records into PostgreSQL.", items.Count);
    }

    private static PromptLibraryRecord ToRecord(PromptLibraryItem item)
    {
        var tags = item.Tags
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray();
        var previewImages = item.PreviewImages
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray();
        var values = new[]
        {
            item.TitleZh,
            item.TitleEn,
            item.Source,
            item.CategoryZh,
            item.CategoryEn,
            item.DescriptionZh,
            item.DescriptionEn,
            item.PromptZh,
            item.PromptEn,
            item.Author,
        }.Concat(tags);

        return new PromptLibraryRecord
        {
            Id = Normalize(item.Id),
            TitleZh = Normalize(item.TitleZh),
            TitleEn = Normalize(item.TitleEn),
            Source = Normalize(item.Source),
            CategoryZh = Normalize(item.CategoryZh),
            CategoryEn = Normalize(item.CategoryEn),
            DescriptionZh = Normalize(item.DescriptionZh),
            DescriptionEn = Normalize(item.DescriptionEn),
            PromptZh = Normalize(item.PromptZh),
            PromptEn = Normalize(item.PromptEn),
            SourceUrl = Normalize(item.SourceUrl),
            Author = Normalize(item.Author),
            NeedsReferenceImages = item.NeedsReferenceImages,
            Language = Normalize(item.Language),
            Tags = tags,
            PreviewImages = previewImages,
            SearchText = string.Join('\n', values).ToLowerInvariant(),
            HasBeautyTag = HasTag(tags, "美图"),
            IsFeaturedWithImage = previewImages.Length > 0 && HasTag(tags, "精选"),
            PreviewImageCount = previewImages.Length,
        };
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static bool HasTag(IEnumerable<string> tags, string expected) =>
        tags.Any(tag => string.Equals(tag, expected, StringComparison.OrdinalIgnoreCase));
}
