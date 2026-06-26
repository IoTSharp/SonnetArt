using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SonnetArt.Models;

namespace SonnetHost.StudioStorage;

public sealed class StudioSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly SonnetArtDbContext _db;

    public StudioSnapshotStore(SonnetArtDbContext db)
    {
        _db = db;
    }

    public async Task<StudioSnapshot?> LoadAsync(StudioStorageIdentity identity, CancellationToken cancellationToken)
    {
        var record = await _db.Snapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerKey == identity.OwnerKey, cancellationToken);
        if (record is null || string.IsNullOrWhiteSpace(record.SnapshotJson))
        {
            return null;
        }

        var snapshot = JsonSerializer.Deserialize<StudioSnapshot>(record.SnapshotJson, JsonOptions);
        snapshot?.Normalize();
        return snapshot;
    }

    public async Task SaveAsync(
        StudioStorageIdentity identity,
        StudioSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        snapshot.Normalize();
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var record = await _db.Snapshots
            .SingleOrDefaultAsync(item => item.OwnerKey == identity.OwnerKey, cancellationToken);
        if (record is null)
        {
            record = new StudioSnapshotRecord
            {
                OwnerKey = identity.OwnerKey,
                CreatedAtUnixMs = now,
            };
            _db.Snapshots.Add(record);
        }

        record.OwnerUserId = identity.UserId;
        record.OwnerEmailHash = identity.EmailHash;
        record.DeviceKeyHash = identity.DeviceKeyHash;
        record.SnapshotJson = json;
        record.UpdatedAtUnixMs = now;

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(StudioStorageIdentity identity, CancellationToken cancellationToken)
    {
        var record = await _db.Snapshots
            .SingleOrDefaultAsync(item => item.OwnerKey == identity.OwnerKey, cancellationToken);
        if (record is null)
        {
            return;
        }

        _db.Snapshots.Remove(record);
        await _db.SaveChangesAsync(cancellationToken);
    }

}
