using System.Text;
using System.Text.Json;

namespace SonnetArt.Models;

public sealed class PromptLibraryItem
{
    public string Id { get; set; } = string.Empty;
    public string TitleZh { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string CategoryZh { get; set; } = string.Empty;
    public string CategoryEn { get; set; } = string.Empty;
    public string DescriptionZh { get; set; } = string.Empty;
    public string DescriptionEn { get; set; } = string.Empty;
    public string PromptZh { get; set; } = string.Empty;
    public string PromptEn { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public bool NeedsReferenceImages { get; set; }
    public string Language { get; set; } = string.Empty;
    public IReadOnlyList<string> Tags { get; set; } = [];
    public List<string> PreviewImages { get; set; } = [];
    public List<string> UserPreviewImages { get; set; } = [];

    public string GetTitle(PromptLibraryLanguage language) =>
        Select(language, TitleZh, TitleEn);

    public string GetCategory(PromptLibraryLanguage language) =>
        Select(language, CategoryZh, CategoryEn);

    public string GetDescription(PromptLibraryLanguage language) =>
        Select(language, DescriptionZh, DescriptionEn);

    public string GetPrompt(PromptLibraryLanguage language) =>
        PromptTextFormatter.Normalize(Select(language, PromptZh, PromptEn));

    public bool Contains(string query)
    {
        return Contains(TitleZh, query) ||
            Contains(TitleEn, query) ||
            Contains(Source, query) ||
            Contains(CategoryZh, query) ||
            Contains(CategoryEn, query) ||
            Contains(DescriptionZh, query) ||
            Contains(DescriptionEn, query) ||
            Contains(PromptZh, query) ||
            Contains(PromptEn, query) ||
            Contains(Author, query) ||
            Tags.Any(tag => Contains(tag, query));
    }

    private static string Select(PromptLibraryLanguage language, string zh, string en)
    {
        var primary = language == PromptLibraryLanguage.Chinese ? zh : en;
        var fallback = language == PromptLibraryLanguage.Chinese ? en : zh;
        return string.IsNullOrWhiteSpace(primary) ? fallback : primary;
    }

    private static bool Contains(string value, string query) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(query, StringComparison.OrdinalIgnoreCase);
}

public enum PromptLibraryLanguage
{
    Chinese,
    English,
}

public sealed record PromptLibrarySelection(PromptLibraryItem Item, PromptLibraryLanguage Language, string Prompt);

public sealed class PromptLibraryPreviewCacheEntry
{
    public string PromptId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public List<string> Images { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class PromptLibraryPage
{
    public List<PromptLibraryItem> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int Pages { get; set; }
    public List<string> Sources { get; set; } = [];
    public List<string> CategoriesZh { get; set; } = [];
    public List<string> CategoriesEn { get; set; } = [];
    public Dictionary<string, List<string>> UserPreviewImages { get; set; } = new(StringComparer.Ordinal);

    public static PromptLibraryPage Empty(int pageSize = 20) => new()
    {
        PageSize = pageSize,
    };
}

public sealed record PromptLibraryQuery(
    int Page,
    int PageSize,
    PromptLibraryLanguage Language,
    string? Source,
    string? Category,
    string? Search);

public static class PromptTextFormatter
{
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed[0] is not ('{' or '['))
        {
            return value;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var builder = new StringBuilder(trimmed.Length);
            WriteElement(builder, document.RootElement, null, 0);
            var text = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(text) ? value : text;
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private static void WriteElement(StringBuilder builder, JsonElement element, string? label, int indent)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(builder, element, label, indent);
                break;
            case JsonValueKind.Array:
                WriteArray(builder, element, label, indent);
                break;
            case JsonValueKind.String:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                WriteScalar(builder, label, FormatScalar(element), indent);
                break;
        }
    }

    private static void WriteObject(StringBuilder builder, JsonElement element, string? label, int indent)
    {
        if (label is not null)
        {
            AppendIndent(builder, indent);
            builder.Append(Humanize(label)).AppendLine(":");
            indent++;
        }

        foreach (var property in element.EnumerateObject())
        {
            WriteElement(builder, property.Value, property.Name, indent);
        }
    }

    private static void WriteArray(StringBuilder builder, JsonElement element, string? label, int indent)
    {
        if (label is not null)
        {
            AppendIndent(builder, indent);
            builder.Append(Humanize(label)).AppendLine(":");
            indent++;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (IsScalar(item))
            {
                AppendIndent(builder, indent);
                builder.Append("- ").AppendLine(FormatScalar(item));
                continue;
            }

            AppendIndent(builder, indent);
            builder.AppendLine("-");
            WriteElement(builder, item, null, indent + 1);
        }
    }

    private static void WriteScalar(StringBuilder builder, string? label, string value, int indent)
    {
        AppendIndent(builder, indent);
        if (label is not null)
        {
            builder.Append(Humanize(label)).Append(": ");
        }

        builder.AppendLine(value);
    }

    private static bool IsScalar(JsonElement element) =>
        element.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or
            JsonValueKind.False or JsonValueKind.Null or JsonValueKind.Undefined;

    private static string FormatScalar(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => element.GetRawText(),
        };

    private static string Humanize(string label) =>
        label.Replace('_', ' ').Replace('-', ' ');

    private static void AppendIndent(StringBuilder builder, int indent)
    {
        builder.Append(' ', indent * 2);
    }
}
