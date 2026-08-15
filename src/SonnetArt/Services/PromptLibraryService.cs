using System.Net.Http.Json;
using System.Text.Json;
using SonnetArt.Models;

namespace SonnetArt.Services;

public sealed class PromptLibraryService
{
    private const int MaxPageSize = 60;

    private readonly HttpClient _httpClient;

    public PromptLibraryService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async ValueTask<PromptLibraryPage> LoadPageAsync(
        PromptLibraryQuery query,
        IReadOnlyDictionary<string, List<string>>? userPreviewImages = null)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var normalizedQuery = query with { Page = page, PageSize = pageSize };
        try
        {
            var url = BuildApiUrl(normalizedQuery);
            var remotePage = await _httpClient.GetFromJsonAsync<PromptLibraryPage>(url);
            if (remotePage is not null)
            {
                return ApplyUserPreviewImages(remotePage, userPreviewImages);
            }
        }
        catch (HttpRequestException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (JsonException)
        {
        }

        return new PromptLibraryPage
        {
            Page = page,
            PageSize = pageSize,
            Pages = 1,
        };
    }

    private static PromptLibraryPage ApplyUserPreviewImages(
        PromptLibraryPage page,
        IReadOnlyDictionary<string, List<string>>? userPreviewImages)
    {
        if (userPreviewImages is null || userPreviewImages.Count == 0)
        {
            return page;
        }

        page.UserPreviewImages = userPreviewImages
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value.Count > 0)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value
                    .Where(image => !string.IsNullOrWhiteSpace(image))
                    .Take(4)
                    .ToList(),
                StringComparer.Ordinal);

        foreach (var item in page.Items)
        {
            if (!page.UserPreviewImages.TryGetValue(item.Id, out var previews) || previews.Count == 0)
            {
                continue;
            }

            item.UserPreviewImages = previews;
            item.PreviewImages = item.PreviewImages
                .Concat(previews)
                .Where(image => !string.IsNullOrWhiteSpace(image))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        return page;
    }

    private static string BuildApiUrl(PromptLibraryQuery query)
    {
        var values = new List<string>
        {
            $"page={query.Page.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"pageSize={query.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"language={Uri.EscapeDataString(query.Language.ToString())}",
        };

        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            values.Add($"source={Uri.EscapeDataString(query.Source)}");
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            values.Add($"category={Uri.EscapeDataString(query.Category)}");
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            values.Add($"search={Uri.EscapeDataString(query.Search)}");
        }

        return $"api/prompt-library?{string.Join('&', values)}";
    }
}
