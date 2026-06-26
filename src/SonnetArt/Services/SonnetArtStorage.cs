using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SonnetArt.Models;

namespace SonnetArt.Services;

public sealed class SonnetArtStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly HttpClient _httpClient;

    public SonnetArtStorage(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async ValueTask<StudioSnapshot> LoadAsync(string? accessToken = null)
    {
        return await TryLoadAsync(accessToken) ?? CreateDefaultSnapshot();
    }

    public async ValueTask<StudioSnapshot?> TryLoadAsync(string? accessToken = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/studio/snapshot");
            ApplyAuthorization(request, accessToken);
            using var response = await _httpClient.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var snapshot = await response.Content.ReadFromJsonAsync<StudioSnapshot>(JsonOptions);
            if (snapshot is null)
            {
                return null;
            }

            EnsureSnapshot(snapshot);
            return snapshot;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }

    public async ValueTask SaveAsync(StudioSnapshot snapshot, string? accessToken = null)
    {
        EnsureSnapshot(snapshot);

        using var request = new HttpRequestMessage(HttpMethod.Put, "api/studio/snapshot")
        {
            Content = JsonContent.Create(snapshot, options: JsonOptions),
        };
        ApplyAuthorization(request, accessToken);
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async ValueTask ClearAsync(string? accessToken = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "api/studio/snapshot");
        ApplyAuthorization(request, accessToken);
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async ValueTask ClearAuthSessionAsync()
    {
        using var response = await _httpClient.DeleteAsync("api/studio/auth-session");
        response.EnsureSuccessStatusCode();
    }

    private static void ApplyAuthorization(HttpRequestMessage request, string? accessToken)
    {
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken.Trim());
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
