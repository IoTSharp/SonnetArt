namespace SonnetHost.StudioStorage;

public sealed record StudioStorageIdentity(
    string OwnerKey,
    long? UserId,
    string? EmailHash,
    string? DeviceKeyHash);
