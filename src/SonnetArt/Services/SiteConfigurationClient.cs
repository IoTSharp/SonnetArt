using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using SonnetArt.Models;

namespace SonnetArt.Services;

public sealed class SiteConfigurationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public SiteConfigurationClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<SiteBranding> LoadBrandingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("api/sonnet/settings/public", cancellationToken);
            response.EnsureSuccessStatusCode();
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseBranding(raw);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return SiteBranding.Default;
        }
    }

    private static SiteBranding ParseBranding(string raw)
    {
        var data = UnwrapData(JsonNode.Parse(raw));
        var name = FirstString(
            data,
            "site_name",
            "siteName",
            "system_name",
            "systemName",
            "app_name",
            "appName",
            "name",
            "title");
        var description = FirstString(
            data,
            "site_description",
            "siteDescription",
            "description",
            "subtitle",
            "slogan");
        var icon = FirstString(
            data,
            "site_icon",
            "siteIcon",
            "icon",
            "icon_url",
            "iconUrl",
            "logo",
            "logo_url",
            "logoUrl",
            "favicon");

        return new SiteBranding
        {
            Name = string.IsNullOrWhiteSpace(name) ? SiteBranding.Default.Name : name,
            Description = string.IsNullOrWhiteSpace(description) ? SiteBranding.Default.Description : description,
            IconUrl = string.IsNullOrWhiteSpace(icon) ? null : icon,
        };
    }

    private static JsonNode? UnwrapData(JsonNode? root)
    {
        if (root is not JsonObject obj)
        {
            return root;
        }

        return obj["data"] ?? obj["settings"] ?? obj["site"] ?? root;
    }

    private static string? FirstString(JsonNode? node, params string[] propertyNames)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            if (TryGetString(obj, propertyName, out var value))
            {
                return value;
            }
        }

        foreach (var child in obj.Select(pair => pair.Value).OfType<JsonObject>())
        {
            foreach (var propertyName in propertyNames)
            {
                if (TryGetString(child, propertyName, out var value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static bool TryGetString(JsonObject obj, string propertyName, out string value)
    {
        value = string.Empty;
        if (!obj.TryGetPropertyValue(propertyName, out var node) ||
            node is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        value = raw.Trim();
        return true;
    }
}
