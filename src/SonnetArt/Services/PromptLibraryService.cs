using System.Net.Http.Json;
using System.Text.Json;
using SonnetArt.ImageStudio.Models;

namespace SonnetArt.ImageStudio.Services;

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

    public async ValueTask<PromptLibraryPage> LoadPageAsync(PromptLibraryQuery query)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var requestUri =
            $"api/local/prompt-library?page={page}&pageSize={pageSize}&language={query.Language}&source={Uri.EscapeDataString(query.Source ?? string.Empty)}&category={Uri.EscapeDataString(query.Category ?? string.Empty)}&search={Uri.EscapeDataString(query.Search ?? string.Empty)}";

        try
        {
            var remotePage = await _httpClient.GetFromJsonAsync<PromptLibraryPage>(requestUri);
            if (remotePage is not null)
            {
                CaptureFilters(remotePage);
                return remotePage;
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

        return await LoadStaticPageAsync(query with { Page = page, PageSize = pageSize });
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
        var matchedItems = items.Where(item => Matches(item, query)).ToArray();
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

    private void CaptureFilters(PromptLibraryPage page)
    {
        _sources = page.Sources;
        _categoriesZh = page.CategoriesZh;
        _categoriesEn = page.CategoriesEn;
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
}
