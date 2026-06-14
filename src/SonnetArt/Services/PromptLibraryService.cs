using System.Net.Http.Json;
using System.Text.Json;
using SonnetArt.Models;

namespace SonnetArt.Services;

public sealed class PromptLibraryService
{
    private const int MaxPageSize = 60;

    private readonly HttpClient _httpClient;
    private IReadOnlyList<string>? _sources;
    private IReadOnlyList<string>? _categoriesZh;
    private IReadOnlyList<string>? _categoriesEn;
    private IReadOnlyList<PromptLibraryItem>? _cache;

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

        return ApplyUserPreviewImages(await LoadStaticPageAsync(normalizedQuery), userPreviewImages);
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

    private async ValueTask<IReadOnlyList<PromptLibraryItem>> LoadAsync()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        _cache = await _httpClient.GetFromJsonAsync<List<PromptLibraryItem>>("data/prompt-library.json") ?? [];
        return _cache;
    }

    private async ValueTask<PromptLibraryPage> LoadStaticPageAsync(PromptLibraryQuery query)
    {
        var items = await LoadAsync();
        var matchedItems = items
            .Where(item => Matches(item, query))
            .OrderBy(item => HasTag(item, "美图"))
            .ThenByDescending(item => HasTag(item, "精选") && item.PreviewImages.Count > 0)
            .ThenByDescending(item => item.PreviewImages.Count > 0)
            .ThenByDescending(item => item.PreviewImages.Count)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var pages = Math.Max(1, (int)Math.Ceiling(matchedItems.Length / (double)pageSize));
        page = Math.Min(page, pages);

        _sources ??= items
            .Select(item => item.Source)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        _categoriesZh ??= items
            .Select(item => item.GetCategory(PromptLibraryLanguage.Chinese))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        _categoriesEn ??= items
            .Select(item => item.GetCategory(PromptLibraryLanguage.English))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new PromptLibraryPage
        {
            Items = matchedItems.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            Total = matchedItems.Length,
            Page = page,
            PageSize = pageSize,
            Pages = pages,
            Sources = _sources.ToList(),
            CategoriesZh = _categoriesZh.ToList(),
            CategoriesEn = _categoriesEn.ToList(),
        };
    }

    private static bool Matches(PromptLibraryItem item, PromptLibraryQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Source) &&
            !string.Equals(item.Source, query.Source, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Category) &&
            !string.Equals(item.GetCategory(query.Language), query.Category, StringComparison.Ordinal))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(query.Search) || item.Contains(query.Search.Trim());
    }

    private static bool HasTag(PromptLibraryItem item, string tag)
    {
        return item.Tags.Any(value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));
    }
}
