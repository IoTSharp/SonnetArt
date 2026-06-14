using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using IoTSharp.Data.JsonDB;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using SonnetArt.Models;

namespace SonnetHost.PromptLibrary;

public static class PromptLibraryEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 60;
    private const string ImageProxyPrefix = "/api/prompt-library/images/";

    public static void MapPromptLibraryEndpoints(this WebApplication app)
    {
        var store = new JsonDbPromptLibraryStore(app.Services);

        app.MapGet("/api/prompt-library", (RequestDelegate)(async context =>
        {
            var queryValues = context.Request.Query;
            var query = new PromptLibraryQuery(
                Math.Max(1, ParseNullableInt(queryValues["page"].ToString()) ?? 1),
                Math.Clamp(ParseNullableInt(queryValues["pageSize"].ToString()) ?? DefaultPageSize, 1, MaxPageSize),
                ParseLanguage(queryValues["language"].ToString()),
                Normalize(queryValues["source"].ToString()),
                Normalize(queryValues["category"].ToString()),
                Normalize(queryValues["search"].ToString()));

            var result = await store.QueryAsync(query, context.RequestAborted);
            await Results.Json(result).ExecuteAsync(context);
        }));

        app.MapGet($"{ImageProxyPrefix}{{id}}/{{index:int}}", (RequestDelegate)(async context =>
        {
            var id = Convert.ToString(context.Request.RouteValues["id"], System.Globalization.CultureInfo.InvariantCulture);
            var index = ParseNullableInt(Convert.ToString(context.Request.RouteValues["index"], System.Globalization.CultureInfo.InvariantCulture)) ?? -1;
            if (string.IsNullOrWhiteSpace(id) || index < 0)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var image = await store.OpenImageAsync(id, index, context.RequestAborted);
            if (image is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = image.ContentType;
            context.Response.Headers.CacheControl = "public, max-age=86400";
            await using var stream = image.Stream;
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
        }));
    }

    private static int? ParseNullableInt(string? value)
    {
        return int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static PromptLibraryLanguage ParseLanguage(string? language)
    {
        return Enum.TryParse<PromptLibraryLanguage>(language, ignoreCase: true, out var value)
            ? value
            : PromptLibraryLanguage.Chinese;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed class JsonDbPromptLibraryStore
    {
        private readonly IServiceProvider _services;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly FileExtensionContentTypeProvider _contentTypes = new();
        private List<string>? _sources;
        private List<string>? _categoriesZh;
        private List<string>? _categoriesEn;

        public JsonDbPromptLibraryStore(IServiceProvider services)
        {
            _services = services;
            _contentTypes.Mappings[".avif"] = "image/avif";
            _contentTypes.Mappings[".webp"] = "image/webp";
        }

        public async Task<PromptLibraryPage> QueryAsync(PromptLibraryQuery query, CancellationToken cancellationToken)
        {
            var promptLibraryPath = ResolvePromptLibraryPath();
            var filters = await LoadFiltersAsync(cancellationToken);
            if (!File.Exists(promptLibraryPath))
            {
                return new PromptLibraryPage
                {
                    Page = 1,
                    PageSize = query.PageSize,
                    Sources = filters.Sources,
                    CategoriesZh = filters.CategoriesZh,
                    CategoriesEn = filters.CategoriesEn,
                };
            }

            var total = CountItems(promptLibraryPath, query);
            var pages = Math.Max(1, (int)Math.Ceiling(total / (double)query.PageSize));
            var page = Math.Min(query.Page, pages);

            return new PromptLibraryPage
            {
                Items = QueryItems(promptLibraryPath, query, (page - 1) * query.PageSize, query.PageSize)
                    .Select(RewritePreviewImages)
                    .ToList(),
                Total = total,
                Page = page,
                PageSize = query.PageSize,
                Pages = pages,
                Sources = filters.Sources,
                CategoriesZh = filters.CategoriesZh,
                CategoriesEn = filters.CategoriesEn,
            };
        }

        public async Task<PromptLibraryImage?> OpenImageAsync(string id, int index, CancellationToken cancellationToken)
        {
            var promptLibraryPath = ResolvePromptLibraryPath();
            if (!File.Exists(promptLibraryPath))
            {
                return null;
            }

            var item = QueryItemById(promptLibraryPath, id);
            if (item is null || index >= item.PreviewImages.Count)
            {
                return null;
            }

            var imageSource = item.PreviewImages[index];
            if (string.IsNullOrWhiteSpace(imageSource))
            {
                return null;
            }

            if (Uri.TryCreate(imageSource, UriKind.Absolute, out var uri) &&
                uri.Scheme is "http" or "https")
            {
                return await OpenRemoteImageAsync(uri, cancellationToken);
            }

            return OpenLocalImage(imageSource);
        }

        private async Task<PromptLibraryFilters> LoadFiltersAsync(CancellationToken cancellationToken)
        {
            if (_sources is not null && _categoriesZh is not null && _categoriesEn is not null)
            {
                return new PromptLibraryFilters(_sources, _categoriesZh, _categoriesEn);
            }

            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_sources is not null && _categoriesZh is not null && _categoriesEn is not null)
                {
                    return new PromptLibraryFilters(_sources, _categoriesZh, _categoriesEn);
                }

                var promptLibraryPath = ResolvePromptLibraryPath();
                if (!File.Exists(promptLibraryPath))
                {
                    _sources = [];
                    _categoriesZh = [];
                    _categoriesEn = [];
                    return new PromptLibraryFilters(_sources, _categoriesZh, _categoriesEn);
                }

                var filters = QueryFilters(promptLibraryPath);
                _sources = filters.Sources;
                _categoriesZh = filters.CategoriesZh;
                _categoriesEn = filters.CategoriesEn;
                return filters;
            }
            finally
            {
                _gate.Release();
            }
        }

        private static int CountItems(string promptLibraryPath, PromptLibraryQuery query)
        {
            using var connection = OpenConnection(promptLibraryPath);
            using var command = CreateCommand(connection, query);
            command.CommandText = $"SELECT COUNT(*) AS total FROM input {BuildWhereClause(query)}";
            var value = command.ExecuteScalar();
            return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static List<PromptLibraryItem> QueryItems(
            string promptLibraryPath,
            PromptLibraryQuery query,
            int offset,
            int count)
        {
            using var connection = OpenConnection(promptLibraryPath);
            using var command = CreateCommand(connection, query);
            command.Parameters.AddWithValue("@offset", offset);
            command.Parameters.AddWithValue("@count", count);
            command.CommandText = $"""
                {SelectItemColumns()}
                FROM input
                {BuildWhereClause(query)}
                {SortClause()}
                LIMIT @offset, @count
                """;

            return ReadItems(command);
        }

        private static PromptLibraryItem? QueryItemById(string promptLibraryPath, string id)
        {
            using var connection = OpenConnection(promptLibraryPath);
            using var command = (JsonDbCommand)connection.CreateCommand();
            command.Parameters.AddWithValue("@id", id);
            command.CommandText = $"""
                {SelectItemColumns()}
                FROM input
                WHERE id = @id
                LIMIT 1
                """;

            return ReadItems(command).FirstOrDefault();
        }

        private static PromptLibraryFilters QueryFilters(string promptLibraryPath)
        {
            using var connection = OpenConnection(promptLibraryPath);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT source, categoryZh, categoryEn
                FROM input
                """;

            using var reader = command.ExecuteReader(CommandBehavior.SequentialAccess);
            var sources = new HashSet<string>(StringComparer.Ordinal);
            var categoriesZh = new HashSet<string>(StringComparer.Ordinal);
            var categoriesEn = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                AddFilterValue(sources, ReadString(reader, "source"));
                AddFilterValue(categoriesZh, ReadString(reader, "categoryZh"));
                AddFilterValue(categoriesEn, ReadString(reader, "categoryEn"));
            }

            return new PromptLibraryFilters(
                sources.OrderBy(value => value, StringComparer.Ordinal).ToList(),
                categoriesZh.OrderBy(value => value, StringComparer.Ordinal).ToList(),
                categoriesEn.OrderBy(value => value, StringComparer.Ordinal).ToList());
        }

        private static List<PromptLibraryItem> ReadItems(JsonDbCommand command)
        {
            using var reader = command.ExecuteReader(CommandBehavior.SequentialAccess);
            var items = new List<PromptLibraryItem>();
            while (reader.Read())
            {
                items.Add(ReadItem(reader));
            }

            return items;
        }

        private static JsonDbConnection OpenConnection(string promptLibraryPath)
        {
            var connection = new JsonDbConnection($"Data Source={promptLibraryPath}");
            connection.AutoSave = false;
            connection.Open();
            RegisterMethods(connection);
            return connection;
        }

        private static JsonDbCommand CreateCommand(JsonDbConnection connection, PromptLibraryQuery query)
        {
            var command = (JsonDbCommand)connection.CreateCommand();
            if (!string.IsNullOrWhiteSpace(query.Source))
            {
                command.Parameters.AddWithValue("@source", query.Source);
            }

            if (!string.IsNullOrWhiteSpace(query.Category))
            {
                command.Parameters.AddWithValue("@category", query.Category);
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                command.Parameters.AddWithValue("@search", query.Search.Trim());
            }

            return command;
        }

        private static void RegisterMethods(JsonDbConnection connection)
        {
            connection.RegisterMethod("coalesce", args =>
            {
                foreach (var arg in args)
                {
                    if (arg is not null && !string.IsNullOrWhiteSpace(Convert.ToString(arg, System.Globalization.CultureInfo.InvariantCulture)))
                    {
                        return arg;
                    }
                }

                return string.Empty;
            });
            connection.RegisterMethod("hasTag", args =>
            {
                if (args.Count < 2 || args[1] is not string tag)
                {
                    return false;
                }

                return ContainsJsonArrayValue(args[0], tag);
            });
            connection.RegisterMethod("arrayLength", args => args.Count == 0 ? 0 : CountJsonArray(args[0]));
            connection.RegisterMethod("hasImages", args => args.Count > 0 && CountJsonArray(args[0]) > 0);
            connection.RegisterMethod("featuredImageRank", args =>
            {
                if (args.Count < 2)
                {
                    return 0;
                }

                return ContainsJsonArrayValue(args[0], "精选") && CountJsonArray(args[1]) > 0 ? 1 : 0;
            });
            connection.RegisterMethod("matchesSearch", args =>
            {
                if (args.Count == 0)
                {
                    return false;
                }

                var query = Convert.ToString(args[0], System.Globalization.CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(query))
                {
                    return true;
                }

                return args.Skip(1).Any(value => ContainsSearchValue(value, query));
            });
        }

        private static string SelectItemColumns() => """
            SELECT
                id,
                titleZh,
                titleEn,
                source,
                categoryZh,
                categoryEn,
                descriptionZh,
                descriptionEn,
                promptZh,
                promptEn,
                sourceUrl,
                author,
                needsReferenceImages,
                language,
                coalesce(tags, "[]") AS tagsJson,
                coalesce(previewImages, "[]") AS previewImagesJson
            """;

        private static string SortClause() => """
            ORDER BY
                hasTag(coalesce(tags, "[]"), "美图") ASCNUM,
                featuredImageRank(coalesce(tags, "[]"), coalesce(previewImages, "[]")) DESCNUM,
                hasImages(coalesce(previewImages, "[]")) DESCNUM,
                arrayLength(coalesce(previewImages, "[]")) DESCNUM,
                id ASC
            """;

        private static string BuildWhereClause(PromptLibraryQuery query)
        {
            var filters = new List<string>();
            if (!string.IsNullOrWhiteSpace(query.Source))
            {
                filters.Add("source = @source");
            }

            if (!string.IsNullOrWhiteSpace(query.Category))
            {
                filters.Add(query.Language == PromptLibraryLanguage.Chinese
                    ? "categoryZh = @category"
                    : "categoryEn = @category");
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                filters.Add("""
                    matchesSearch(
                        @search,
                        titleZh,
                        titleEn,
                        source,
                        categoryZh,
                        categoryEn,
                        descriptionZh,
                        descriptionEn,
                        promptZh,
                        promptEn,
                        author,
                        coalesce(tags, "[]"))
                    """);
            }

            return filters.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", filters)}";
        }

        private static void AddFilterValue(HashSet<string> values, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        private static bool ContainsSearchValue(object? value, string query)
        {
            if (value is null)
            {
                return false;
            }

            if (value is JsonArray or JsonNode or string)
            {
                try
                {
                    var arrayValues = ReadStringValues(value);
                    if (arrayValues.Count > 0)
                    {
                        return arrayValues.Any(text => ContainsSearchText(text, query));
                    }
                }
                catch (JsonException)
                {
                }
            }

            return ContainsSearchText(
                Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                query);
        }

        private static bool ContainsSearchText(string value, string query) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(query, StringComparison.OrdinalIgnoreCase);

        private static PromptLibraryItem ReadItem(IDataRecord reader)
        {
            return new PromptLibraryItem
            {
                Id = ReadString(reader, "id"),
                TitleZh = ReadString(reader, "titleZh"),
                TitleEn = ReadString(reader, "titleEn"),
                Source = ReadString(reader, "source"),
                CategoryZh = ReadString(reader, "categoryZh"),
                CategoryEn = ReadString(reader, "categoryEn"),
                DescriptionZh = ReadString(reader, "descriptionZh"),
                DescriptionEn = ReadString(reader, "descriptionEn"),
                PromptZh = ReadString(reader, "promptZh"),
                PromptEn = ReadString(reader, "promptEn"),
                SourceUrl = ReadString(reader, "sourceUrl"),
                Author = ReadString(reader, "author"),
                NeedsReferenceImages = ReadBool(reader, "needsReferenceImages"),
                Language = ReadString(reader, "language"),
                Tags = ReadStringArray(reader, "tagsJson"),
                PreviewImages = ReadStringArray(reader, "previewImagesJson").ToList(),
            };
        }

        private static string ReadString(IDataRecord reader, string name)
        {
            var value = reader[name];
            return value is null or DBNull ? string.Empty : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static bool ReadBool(IDataRecord reader, string name)
        {
            var value = reader[name];
            if (value is null or DBNull)
            {
                return false;
            }

            return value is bool boolValue
                ? boolValue
                : bool.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out var result) && result;
        }

        private static IReadOnlyList<string> ReadStringArray(IDataRecord reader, string name)
        {
            var value = ReadString(reader, name);
            if (string.IsNullOrWhiteSpace(value))
            {
                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<List<string>>(value) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private PromptLibraryItem RewritePreviewImages(PromptLibraryItem source)
        {
            return new PromptLibraryItem
            {
                Id = source.Id,
                TitleZh = source.TitleZh,
                TitleEn = source.TitleEn,
                Source = source.Source,
                CategoryZh = source.CategoryZh,
                CategoryEn = source.CategoryEn,
                DescriptionZh = source.DescriptionZh,
                DescriptionEn = source.DescriptionEn,
                PromptZh = source.PromptZh,
                PromptEn = source.PromptEn,
                SourceUrl = source.SourceUrl,
                Author = source.Author,
                NeedsReferenceImages = source.NeedsReferenceImages,
                Language = source.Language,
                Tags = source.Tags,
                PreviewImages = source.PreviewImages
                    .Select((_, index) => $"{ImageProxyPrefix}{Uri.EscapeDataString(source.Id)}/{index}")
                    .ToList(),
            };
        }

        private PromptLibraryImage? OpenLocalImage(string imageSource)
        {
            var normalized = imageSource.Replace('\\', '/').TrimStart('/');
            if (normalized.Contains("..", StringComparison.Ordinal))
            {
                return null;
            }

            var file = ResolveClientFileProvider().GetFileInfo(normalized);
            if (!file.Exists)
            {
                return null;
            }

            var contentType = ResolveContentType(normalized);
            return new PromptLibraryImage(file.CreateReadStream(), contentType);
        }

        private async Task<PromptLibraryImage?> OpenRemoteImageAsync(Uri uri, CancellationToken cancellationToken)
        {
            var cachePath = ResolveRemoteImageCachePath(uri);
            if (!File.Exists(cachePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                var clientFactory = _services.GetRequiredService<IHttpClientFactory>();
                var client = clientFactory.CreateClient("prompt-library-images");
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode ||
                    response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
                {
                    return null;
                }

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var destination = File.Create(cachePath);
                await source.CopyToAsync(destination, cancellationToken);
            }

            var contentType = ResolveContentType(cachePath);
            return new PromptLibraryImage(File.OpenRead(cachePath), contentType);
        }

        private string ResolvePromptLibraryPath()
        {
            var file = ResolveClientFileProvider().GetFileInfo("data/prompt-library.json");
            return file.Exists && !string.IsNullOrWhiteSpace(file.PhysicalPath)
                ? file.PhysicalPath
                : Path.Combine(ResolveClientWebRoot(), "data", "prompt-library.json");
        }

        private IFileProvider ResolveClientFileProvider()
        {
            var webRoot = ResolveClientWebRoot();
            return Directory.Exists(webRoot)
                ? new PhysicalFileProvider(webRoot)
                : new NullFileProvider();
        }

        private string ResolveClientWebRoot()
        {
            var environment = _services.GetRequiredService<IWebHostEnvironment>();
            var publishedRoot = Path.Combine(environment.ContentRootPath, "wwwroot");
            if (File.Exists(Path.Combine(publishedRoot, "data", "prompt-library.json")))
            {
                return publishedRoot;
            }

            return Path.GetFullPath(Path.Combine(
                environment.ContentRootPath,
                "..",
                "SonnetArt",
                "bin",
                environment.EnvironmentName.Equals("Development", StringComparison.OrdinalIgnoreCase) ? "Debug" : "Release",
                "net10.0",
                "wwwroot"));
        }

        private string ResolveRemoteImageCachePath(Uri uri)
        {
            var environment = _services.GetRequiredService<IWebHostEnvironment>();
            var extension = Path.GetExtension(uri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
            {
                extension = ".img";
            }

            var key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri.AbsoluteUri))).ToLowerInvariant();
            return Path.Combine(environment.ContentRootPath, "data", "prompt-library-images", key + extension);
        }

        private string ResolveContentType(string path)
        {
            return _contentTypes.TryGetContentType(path, out var contentType)
                ? contentType
                : "application/octet-stream";
        }

        private static bool ContainsJsonArrayValue(object? json, string value)
        {
            try
            {
                var values = ReadStringValues(json);
                return values.Any(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static int CountJsonArray(object? json)
        {
            try
            {
                return ReadStringValues(json).Count;
            }
            catch (JsonException)
            {
                return 0;
            }
        }

        private static IReadOnlyList<string> ReadStringValues(object? value)
        {
            return value switch
            {
                null => [],
                JsonArray array => array
                    .Select(node => node?.GetValue<string>())
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Select(text => text!)
                    .ToList(),
                JsonNode node => JsonSerializer.Deserialize<List<string>>(node.ToJsonString()) ?? [],
                string text when !string.IsNullOrWhiteSpace(text) => JsonSerializer.Deserialize<List<string>>(text) ?? [],
                _ => [],
            };
        }
    }

    private sealed record PromptLibraryFilters(
        List<string> Sources,
        List<string> CategoriesZh,
        List<string> CategoriesEn);

    private sealed record PromptLibraryImage(Stream Stream, string ContentType);
}
