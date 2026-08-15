using System.Net;
using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using SonnetHost.Configuration;
using SonnetHost.PromptLibrary;
using SonnetHost.Proxy;
using SonnetHost.StudioStorage;

var builder = WebApplication.CreateSlimBuilder(args);

var sonnetArtConfiguration = builder.Configuration.GetRequiredSection(SonnetArtHostOptions.SectionName);
var hostOptions = sonnetArtConfiguration
    .Get<SonnetArtHostOptions>() ?? new SonnetArtHostOptions();
hostOptions.Validate();
var databaseConnection = builder.Configuration.GetConnectionString("SonnetArt");
if (string.IsNullOrWhiteSpace(databaseConnection))
{
    throw new InvalidOperationException("ConnectionStrings:SonnetArt must be configured.");
}

builder.Services
    .AddOptions<SonnetArtHostOptions>()
    .Bind(sonnetArtConfiguration)
    .Validate(options =>
    {
        try
        {
            options.Validate();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }, $"{SonnetArtHostOptions.SectionName} configuration is invalid.")
    .ValidateOnStart();

builder.WebHost.UseUrls(hostOptions.ResolveListenUrl());
builder.WebHost.UseStaticWebAssets();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
    [
        "application/octet-stream",
        "application/wasm",
    ]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
builder.Services.AddHttpClient(SonnetProxyEndpoints.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
    client.DefaultRequestVersion = HttpVersion.Version11;
    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
})
.ConfigurePrimaryHttpMessageHandler(CreateProxyHandler);
builder.Services.AddHttpClient("prompt-library-images", client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
    client.DefaultRequestVersion = HttpVersion.Version11;
    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
})
.ConfigurePrimaryHttpMessageHandler(CreateProxyHandler);
builder.Services.AddHttpClient("sonnet-storage-auth", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestVersion = HttpVersion.Version11;
    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
})
.ConfigurePrimaryHttpMessageHandler(CreateProxyHandler);
builder.Services.AddDbContext<SonnetArtDbContext>(options =>
    options.UseNpgsql(databaseConnection, postgres => postgres.EnableRetryOnFailure()));
builder.Services.AddScoped<StudioSnapshotStore>();
builder.Services.AddScoped<SonnetArtIdentityResolver>();
builder.Services.AddScoped<PromptLibraryStore>();
builder.Services.AddSingleton<SonnetArtStorageSchemaInitializer>();
builder.Services.AddHostedService<PromptLibraryImageCacheWarmupService>();

var app = builder.Build();
await app.Services.GetRequiredService<SonnetArtStorageSchemaInitializer>().InitializeAsync();

app.Use(async (context, next) =>
{
    if (SonnetArtStaticFileApplicationBuilderExtensions.ShouldDisableHtmlCache(context.Request.Path))
    {
        context.Response.OnStarting(() =>
        {
            if (!context.Response.HasStarted &&
                context.Response.StatusCode == StatusCodes.Status200OK &&
                SonnetArtStaticFileApplicationBuilderExtensions.IsHtmlResponse(context.Response.ContentType))
            {
                SonnetArtStaticFileApplicationBuilderExtensions.DisableResponseCache(context.Response);
            }

            return Task.CompletedTask;
        });
    }

    try
    {
        await next(context);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
    }
});

app.UseSonnetArtSecurityHeaders();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPromptLibraryEndpoints();
app.MapStudioSnapshotEndpoints();
app.MapSonnetProxyEndpoints();
app.UseResponseCompression();
app.UseSonnetArtFrameworkAliases();
app.UseBlazorFrameworkFiles();
app.UseSonnetArtClientStaticFiles();
app.UseSonnetArtStaticFiles();
app.MapFallbackToFile("index.html");

await app.RunAsync();

static SocketsHttpHandler CreateProxyHandler()
{
    return new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(20),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    };
}

internal static class SonnetArtStaticFileApplicationBuilderExtensions
{
    public static IApplicationBuilder UseSonnetArtSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            await next(context);
        });
    }

    public static IApplicationBuilder UseSonnetArtStaticFiles(this IApplicationBuilder app)
    {
        var contentTypeProvider = new FileExtensionContentTypeProvider();
        contentTypeProvider.Mappings[".dat"] = "application/octet-stream";
        contentTypeProvider.Mappings[".dll"] = "application/octet-stream";
        contentTypeProvider.Mappings[".wasm"] = "application/wasm";
        contentTypeProvider.Mappings[".webcil"] = "application/octet-stream";

        return app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = contentTypeProvider,
            OnPrepareResponse = context =>
            {
                var path = context.Context.Request.Path;
                if (IsNoCacheAsset(path))
                {
                    DisableResponseCache(context.Context.Response);
                }
            },
        });
    }

    public static IApplicationBuilder UseSonnetArtClientStaticFiles(this IApplicationBuilder app)
    {
        var clientWebRoot = ResolveClientWebRoot(app.ApplicationServices);
        if (!Directory.Exists(clientWebRoot))
        {
            return app;
        }

        var contentTypeProvider = new FileExtensionContentTypeProvider();
        contentTypeProvider.Mappings[".dat"] = "application/octet-stream";
        contentTypeProvider.Mappings[".dll"] = "application/octet-stream";
        contentTypeProvider.Mappings[".wasm"] = "application/wasm";
        contentTypeProvider.Mappings[".webcil"] = "application/octet-stream";

        return app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(clientWebRoot),
            ContentTypeProvider = contentTypeProvider,
            OnPrepareResponse = context =>
            {
                var path = context.Context.Request.Path;
                if (IsNoCacheAsset(path))
                {
                    DisableResponseCache(context.Context.Response);
                }
            },
        });
    }

    public static IApplicationBuilder UseSonnetArtFrameworkAliases(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value;
            if (HttpMethods.IsGet(context.Request.Method) &&
                path is not null &&
                TryResolveFrameworkAlias(context.RequestServices, path, out var physicalFile))
            {
                context.Response.ContentType = "text/javascript";
                await context.Response.SendFileAsync(physicalFile, context.RequestAborted);
                return;
            }

            await next(context);
        });
    }

    private static bool TryResolveFrameworkAlias(IServiceProvider services, string requestPath, out string physicalFile)
    {
        physicalFile = string.Empty;
        var fileName = requestPath switch
        {
            "/_framework/blazor.webassembly.js" => "blazor.webassembly.*.js",
            "/_framework/dotnet.js" => "dotnet.*.js",
            "/_framework/dotnet.native.js" => "dotnet.native.*.js",
            "/_framework/dotnet.runtime.js" => "dotnet.runtime.*.js",
            _ => string.Empty,
        };

        if (fileName.Length == 0)
        {
            return false;
        }

        var clientFrameworkDirectory = Path.Combine(ResolveClientWebRoot(services), "_framework");
        if (!Directory.Exists(clientFrameworkDirectory))
        {
            return false;
        }

        physicalFile = Directory.EnumerateFiles(clientFrameworkDirectory, fileName, SearchOption.TopDirectoryOnly)
            .Order(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? string.Empty;
        return physicalFile.Length > 0;
    }

    private static string ResolveClientWebRoot(IServiceProvider services)
    {
        var environment = services.GetRequiredService<IWebHostEnvironment>();
        return Path.GetFullPath(Path.Combine(
            environment.ContentRootPath,
            "..",
            "SonnetArt",
            "bin",
            environment.EnvironmentName.Equals("Development", StringComparison.OrdinalIgnoreCase) ? "Debug" : "Release",
            "net10.0",
            "wwwroot"));
    }

    private static bool IsNoCacheAsset(PathString path)
    {
        return path.Equals("/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/index.html", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/_framework/blazor.boot.json", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/_framework/blazor.boot", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/service-worker-assets.js", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldDisableHtmlCache(PathString path)
    {
        return path.Equals("/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/index.html", StringComparison.OrdinalIgnoreCase) ||
            !Path.HasExtension(path.Value);
    }

    public static bool IsHtmlResponse(string? contentType)
    {
        return contentType is not null &&
            contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
    }

    public static void DisableResponseCache(HttpResponse response)
    {
        response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
    }
}
