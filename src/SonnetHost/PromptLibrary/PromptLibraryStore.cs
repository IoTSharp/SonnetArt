using System.Security.Cryptography;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using SonnetArt.Models;
using SonnetHost.StudioStorage;

namespace SonnetHost.PromptLibrary;

public sealed class PromptLibraryStore
{
    private const string ImageProxyPrefix = "/api/prompt-library/images/";

    private readonly SonnetArtDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebHostEnvironment _environment;
    private readonly FileExtensionContentTypeProvider _contentTypes = new();

    public PromptLibraryStore(
        SonnetArtDbContext db,
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment environment)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _environment = environment;
        _contentTypes.Mappings[".avif"] = "image/avif";
        _contentTypes.Mappings[".webp"] = "image/webp";
    }

    public async Task<PromptLibraryPage> QueryAsync(
        PromptLibraryQuery query,
        CancellationToken cancellationToken)
    {
        var filtered = _db.PromptLibraryItems.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            filtered = filtered.Where(item => item.Source == query.Source);
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            filtered = query.Language == PromptLibraryLanguage.Chinese
                ? filtered.Where(item => item.CategoryZh == query.Category)
                : filtered.Where(item => item.CategoryEn == query.Category);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            filtered = filtered.Where(item => item.SearchText.Contains(search));
        }

        var total = await filtered.CountAsync(cancellationToken);
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)query.PageSize));
        var page = Math.Min(query.Page, pages);
        var records = await filtered
            .OrderBy(item => item.HasBeautyTag)
            .ThenByDescending(item => item.IsFeaturedWithImage)
            .ThenByDescending(item => item.PreviewImageCount > 0)
            .ThenByDescending(item => item.PreviewImageCount)
            .ThenBy(item => item.Id)
            .Skip((page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PromptLibraryPage
        {
            Items = records.Select(ToItem).ToList(),
            Total = total,
            Page = page,
            PageSize = query.PageSize,
            Pages = pages,
            Sources = await LoadDistinctAsync(
                _db.PromptLibraryItems.Select(item => item.Source),
                cancellationToken),
            CategoriesZh = await LoadDistinctAsync(
                _db.PromptLibraryItems.Select(item => item.CategoryZh),
                cancellationToken),
            CategoriesEn = await LoadDistinctAsync(
                _db.PromptLibraryItems.Select(item => item.CategoryEn),
                cancellationToken),
        };
    }

    public async Task<PromptLibraryImage?> OpenImageAsync(
        string id,
        int index,
        CancellationToken cancellationToken)
    {
        var previewImages = await _db.PromptLibraryItems
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => item.PreviewImages)
            .SingleOrDefaultAsync(cancellationToken);
        if (previewImages is null || index >= previewImages.Length)
        {
            return null;
        }

        var imageSource = previewImages[index];
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

    private static async Task<List<string>> LoadDistinctAsync(
        IQueryable<string> query,
        CancellationToken cancellationToken)
    {
        return await query
            .Where(value => value != string.Empty)
            .Distinct()
            .OrderBy(value => value)
            .ToListAsync(cancellationToken);
    }

    private static PromptLibraryItem ToItem(PromptLibraryRecord record)
    {
        return new PromptLibraryItem
        {
            Id = record.Id,
            TitleZh = record.TitleZh,
            TitleEn = record.TitleEn,
            Source = record.Source,
            CategoryZh = record.CategoryZh,
            CategoryEn = record.CategoryEn,
            DescriptionZh = record.DescriptionZh,
            DescriptionEn = record.DescriptionEn,
            PromptZh = record.PromptZh,
            PromptEn = record.PromptEn,
            SourceUrl = record.SourceUrl,
            Author = record.Author,
            NeedsReferenceImages = record.NeedsReferenceImages,
            Language = record.Language,
            Tags = record.Tags,
            PreviewImages = record.PreviewImages
                .Select((_, index) => $"{ImageProxyPrefix}{Uri.EscapeDataString(record.Id)}/{index}")
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

        return new PromptLibraryImage(file.CreateReadStream(), ResolveContentType(normalized));
    }

    private async Task<PromptLibraryImage?> OpenRemoteImageAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        var cachePath = ResolveRemoteImageCachePath(uri);
        if (!File.Exists(cachePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var client = _httpClientFactory.CreateClient("prompt-library-images");
            using var response = await client.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode ||
                response.Content.Headers.ContentType?.MediaType?.StartsWith(
                    "image/",
                    StringComparison.OrdinalIgnoreCase) != true)
            {
                return null;
            }

            var tempPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using (var destination = File.Create(tempPath))
                {
                    await source.CopyToAsync(destination, cancellationToken);
                }

                File.Move(tempPath, cachePath, overwrite: true);
            }
            finally
            {
                File.Delete(tempPath);
            }
        }

        return new PromptLibraryImage(File.OpenRead(cachePath), ResolveContentType(cachePath));
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
        var publishedRoot = Path.Combine(_environment.ContentRootPath, "wwwroot");
        if (Directory.Exists(publishedRoot))
        {
            return publishedRoot;
        }

        return Path.GetFullPath(Path.Combine(
            _environment.ContentRootPath,
            "..",
            "SonnetArt",
            "bin",
            _environment.EnvironmentName.Equals("Development", StringComparison.OrdinalIgnoreCase) ? "Debug" : "Release",
            "net10.0",
            "wwwroot"));
    }

    private string ResolveRemoteImageCachePath(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
        {
            extension = ".img";
        }

        var key = Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri.AbsoluteUri)))
            .ToLowerInvariant();
        return Path.Combine(_environment.ContentRootPath, "data", "prompt-library-images", key + extension);
    }

    private string ResolveContentType(string path) =>
        _contentTypes.TryGetContentType(path, out var contentType)
            ? contentType
            : "application/octet-stream";
}

public sealed record PromptLibraryImage(Stream Stream, string ContentType);
