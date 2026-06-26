using Microsoft.EntityFrameworkCore;

namespace SonnetHost.StudioStorage;

public sealed class SonnetArtStorageSchemaInitializer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SonnetArtStorageSchemaInitializer> _logger;

    public SonnetArtStorageSchemaInitializer(
        IServiceScopeFactory scopeFactory,
        ILogger<SonnetArtStorageSchemaInitializer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SonnetArtDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "sonnet_art_snapshots" (
                    "OwnerKey" STRING NOT NULL,
                    "OwnerUserId" INT NULL,
                    "OwnerEmailHash" STRING NULL,
                    "DeviceKeyHash" STRING NULL,
                    "SnapshotJson" JSON NOT NULL,
                    "CreatedAtUnixMs" INT NOT NULL,
                    "UpdatedAtUnixMs" INT NOT NULL,
                    PRIMARY KEY ("OwnerKey")
                )
                """, cancellationToken);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "sonnet_art_auth_sessions" (
                    "SessionIdHash" STRING NOT NULL,
                    "OwnerKey" STRING NOT NULL,
                    "OwnerUserId" INT NOT NULL,
                    "OwnerEmailHash" STRING NULL,
                    "CreatedAtUnixMs" INT NOT NULL,
                    "UpdatedAtUnixMs" INT NOT NULL,
                    "ExpiresAtUnixMs" INT NOT NULL,
                    PRIMARY KEY ("SessionIdHash")
                )
                """, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "SonnetArt storage schema initialization failed.");
            throw;
        }
    }
}
