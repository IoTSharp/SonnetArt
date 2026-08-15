using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SonnetHost.Configuration;
using SonnetHost.StudioStorage;

namespace SonnetHost.PromptLibrary;

public sealed class PromptLibraryImageCacheWarmupService : BackgroundService
{
    private const int MaxConcurrency = 4;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebHostEnvironment _environment;
    private readonly IOptions<SonnetArtHostOptions> _options;
    private readonly ILogger<PromptLibraryImageCacheWarmupService> _logger;

    public PromptLibraryImageCacheWarmupService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment environment,
        IOptions<SonnetArtHostOptions> options,
        ILogger<PromptLibraryImageCacheWarmupService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _environment = environment;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.PromptImageWarmup)
        {
            return;
        }

        try
        {
            await WarmupAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Prompt library image cache warmup failed.");
        }
    }

    private async Task WarmupAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SonnetArtDbContext>();
        var previewImages = await db.PromptLibraryItems
            .AsNoTracking()
            .Select(item => item.PreviewImages)
            .ToListAsync(cancellationToken);
        var urls = previewImages
            .SelectMany(images => images)
            .Select(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null)
            .Where(uri => uri?.Scheme is "http" or "https")
            .Select(uri => uri!)
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (urls.Length == 0)
        {
            return;
        }

        _logger.LogInformation("Warming prompt library image cache for {Count} remote images.", urls.Length);
        using var throttler = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var tasks = urls.Select(uri => DownloadOneAsync(uri, throttler, cancellationToken)).ToArray();
        await Task.WhenAll(tasks);
    }

    private async Task DownloadOneAsync(Uri uri, SemaphoreSlim throttler, CancellationToken cancellationToken)
    {
        var cachePath = ResolveRemoteImageCachePath(uri);
        if (File.Exists(cachePath))
        {
            return;
        }

        await throttler.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(cachePath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var tempPath = cachePath + ".tmp";
            var client = _httpClientFactory.CreateClient("prompt-library-images");
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode ||
                response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
            {
                return;
            }

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = File.Create(tempPath))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            File.Move(tempPath, cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "Skipping prompt library image {ImageUrl}.", uri.AbsoluteUri);
        }
        finally
        {
            throttler.Release();
        }
    }

    private string ResolveRemoteImageCachePath(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
        {
            extension = ".img";
        }

        var key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri.AbsoluteUri))).ToLowerInvariant();
        return Path.Combine(_environment.ContentRootPath, "data", "prompt-library-images", key + extension);
    }
}
