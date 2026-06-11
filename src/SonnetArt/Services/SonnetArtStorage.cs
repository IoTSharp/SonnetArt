using System.Text.Json;
using System.Text.Json.Serialization;
using SonnetArt.Models;
using Microsoft.JSInterop;

namespace SonnetArt.Services;

public sealed class SonnetArtStorage
{
    private const string StorageKey = "sonnetart.snapshot.v1";
    private const int LocalStorageFullSnapshotCharacterLimit = 4_000_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly IJSRuntime _jsRuntime;

    public SonnetArtStorage(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async ValueTask<StudioSnapshot> LoadAsync()
    {
        string? json;
        try
        {
            json = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        }
        catch (JSException)
        {
            return CreateDefaultSnapshot();
        }
        catch (JSDisconnectedException)
        {
            return CreateDefaultSnapshot();
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return CreateDefaultSnapshot();
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<StudioSnapshot>(json, JsonOptions);
            if (snapshot is null)
            {
                return CreateDefaultSnapshot();
            }

            EnsureSnapshot(snapshot);
            return snapshot;
        }
        catch (JsonException)
        {
            return CreateDefaultSnapshot();
        }
    }

    public async ValueTask SaveAsync(StudioSnapshot snapshot)
    {
        EnsureSnapshot(snapshot);

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        if (json.Length <= LocalStorageFullSnapshotCharacterLimit &&
            await TrySaveToLocalStorageAsync(json))
        {
            return;
        }

        await TryRemoveLocalStorageAsync();

        var compactJson = JsonSerializer.Serialize(
            StudioSnapshotLocalStorageCompactor.CreateCompactSnapshot(snapshot),
            JsonOptions);
        if (compactJson.Length <= LocalStorageFullSnapshotCharacterLimit &&
            await TrySaveToLocalStorageAsync(compactJson))
        {
            return;
        }

        await TryRemoveLocalStorageAsync();

        var minimalJson = JsonSerializer.Serialize(
            StudioSnapshotLocalStorageCompactor.CreateMinimalSnapshot(snapshot),
            JsonOptions);
        await TrySaveToLocalStorageAsync(minimalJson);
    }

    public async ValueTask ClearAsync()
    {
        await TryRemoveLocalStorageAsync();
    }

    private async Task<bool> TrySaveToLocalStorageAsync(string json)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
            return true;
        }
        catch (JSException)
        {
            return false;
        }
        catch (JSDisconnectedException)
        {
            return false;
        }
    }

    private async Task TryRemoveLocalStorageAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        }
        catch (JSException)
        {
        }
        catch (JSDisconnectedException)
        {
        }
    }

    private static StudioSnapshot CreateDefaultSnapshot()
    {
        var session = new StudioSession
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = "透明玻璃茶杯海报",
            Prompt = "一张中文包装海报，主体是透明背景的玻璃茶杯，冷静商业摄影风格",
        };

        return new StudioSnapshot
        {
            Settings = new StudioSettings(),
            Sessions = [session],
            ActiveSessionId = session.Id,
        }.AlsoNormalized();
    }

    private static void EnsureSnapshot(StudioSnapshot snapshot)
    {
        snapshot.Normalize();
        if (string.IsNullOrWhiteSpace(snapshot.Settings.Model) ||
            snapshot.Settings.Model.Equals("gpt-5.4", StringComparison.OrdinalIgnoreCase) ||
            snapshot.Settings.Model.Equals("gpt-5.5", StringComparison.OrdinalIgnoreCase))
        {
            snapshot.Settings.Model = "gpt-image-2";
        }

        snapshot.Normalize();
    }
}

internal static class StudioSnapshotNormalizationExtensions
{
    public static StudioSnapshot AlsoNormalized(this StudioSnapshot snapshot)
    {
        snapshot.Normalize();
        return snapshot;
    }
}
