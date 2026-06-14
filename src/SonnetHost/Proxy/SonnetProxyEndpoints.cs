using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Extensions;

namespace SonnetHost.Proxy;

public static class SonnetProxyEndpoints
{
    public const string HttpClientName = "sonnet-proxy";
    private const string ProxyMarkerHeader = "X-SonnetArt-Proxy";
    private const string DefaultUpstreamBaseUrl = "https://sonnet.vip/";
    private const long MaxProxyRequestBodyBytes = 96L * 1024 * 1024;

    private static readonly string[] SupportedMethods =
    [
        HttpMethods.Get,
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete,
    ];

    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
    };

    private static readonly HashSet<string> LocalOnlyHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "Origin",
        "Referer",
    };

    public static void MapSonnetProxyEndpoints(this WebApplication app)
    {
        app.MapMethods("/api/sonnet/{**path}", SupportedMethods, (RequestDelegate)ProxyAccountAsync);
        app.MapMethods("/api/openai/{**path}", SupportedMethods, (RequestDelegate)ProxyOpenAiAsync);
    }

    private static Task ProxyAccountAsync(HttpContext context)
    {
        return ProxyConfiguredUpstreamAsync(
            context,
            "SONNET_ART_ACCOUNT_UPSTREAM_URL",
            rewriteAccountPath: true,
            "账户服务");
    }

    private static Task ProxyOpenAiAsync(HttpContext context)
    {
        return ProxyConfiguredUpstreamAsync(
            context,
            "SONNET_ART_AI_UPSTREAM_URL",
            rewriteAccountPath: false,
            "上游服务");
    }

    private static async Task ProxyConfiguredUpstreamAsync(
        HttpContext context,
        string environmentVariable,
        bool rewriteAccountPath,
        string serviceLabel)
    {
        Uri baseUrl;
        try
        {
            baseUrl = ReadEnvironmentUrl(environmentVariable);
        }
        catch (InvalidOperationException ex)
        {
            await WriteProxyErrorAsync(context, StatusCodes.Status502BadGateway, ex.Message, context.RequestAborted);
            return;
        }

        await ProxyAsync(context, baseUrl, rewriteAccountPath, serviceLabel);
    }

    private static async Task ProxyAsync(
        HttpContext context,
        Uri upstreamRoot,
        bool rewriteAccountPath,
        string serviceLabel)
    {
        var httpClientFactory = context.RequestServices.GetRequiredService<IHttpClientFactory>();
        var cancellationToken = context.RequestAborted;
        context.Response.Headers[ProxyMarkerHeader] = "1";

        Uri target;
        try
        {
            var path = GetRoutePath(context);
            target = BuildTargetUri(context.Request, upstreamRoot, path, rewriteAccountPath);
        }
        catch (InvalidOperationException ex)
        {
            await WriteProxyErrorAsync(context, StatusCodes.Status400BadRequest, ex.Message, cancellationToken);
            return;
        }

        using var proxyRequest = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
        CopyRequestHeaders(context.Request, proxyRequest);

        if (HasRequestBody(context.Request))
        {
            try
            {
                proxyRequest.Content = await BufferRequestContentAsync(context.Request, cancellationToken);
            }
            catch (ProxyRequestBodyTooLargeException ex)
            {
                await WriteProxyErrorAsync(context, StatusCodes.Status413PayloadTooLarge, ex.Message, cancellationToken);
                return;
            }
        }

        try
        {
            var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using var proxyResponse = await httpClient.SendAsync(
                proxyRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            await CopyProxyResponseAsync(context, proxyResponse, cancellationToken);
        }
        catch (OperationCanceledException) when (IsClientAbort(context, cancellationToken))
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await TryWriteProxyErrorAsync(
                context,
                StatusCodes.Status502BadGateway,
                $"无法连接{serviceLabel}：{ex.Message}",
                cancellationToken);
        }
    }

    private static Uri ReadEnvironmentUrl(string variableName)
    {
        var raw = Environment.GetEnvironmentVariable(variableName);
        var root = string.IsNullOrWhiteSpace(raw) ? DefaultUpstreamBaseUrl : raw.Trim();
        if (!root.EndsWith('/'))
        {
            root += "/";
        }

        if (!Uri.TryCreate(root, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"{variableName} 必须是 http 或 https 绝对地址。");
        }

        return uri;
    }

    private static Uri BuildTargetUri(
        HttpRequest request,
        Uri upstreamRoot,
        string path,
        bool rewriteAccountPath)
    {
        var cleanPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(cleanPath) ||
            cleanPath.Contains("..", StringComparison.Ordinal) ||
            cleanPath.Contains('\\') ||
            cleanPath.Contains("://", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("代理路径无效。");
        }

        var targetPath = rewriteAccountPath ? $"api/v1/{cleanPath}" : cleanPath;
        var target = new Uri(upstreamRoot, targetPath);
        if (!request.QueryString.HasValue)
        {
            return target;
        }

        var builder = new UriBuilder(target)
        {
            Query = request.QueryString.Value!.TrimStart('?'),
        };
        return builder.Uri;
    }

    private static string GetRoutePath(HttpContext context)
    {
        return context.Request.RouteValues.TryGetValue("path", out var value)
            ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().TrimStart('/');
    }

    private static bool HasRequestBody(HttpRequest request)
    {
        return !HttpMethods.IsGet(request.Method) &&
            !HttpMethods.IsHead(request.Method) &&
            (request.ContentLength is > 0 || request.Headers.ContainsKey("Transfer-Encoding"));
    }

    private static async Task<HttpContent> BufferRequestContentAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength is > MaxProxyRequestBodyBytes)
        {
            throw new ProxyRequestBodyTooLargeException("请求体超过代理大小限制。");
        }

        using var buffer = new MemoryStream(
            request.ContentLength is > 0 and <= int.MaxValue ? (int)request.ContentLength.Value : 0);
        await CopyRequestBodyAsync(request.Body, buffer, cancellationToken);
        var content = new ByteArrayContent(buffer.ToArray());
        CopyRequestContentHeaders(request, content);
        return content;
    }

    private static async Task CopyRequestBodyAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                return;
            }

            if (destination.Length + read > MaxProxyRequestBodyBytes)
            {
                throw new ProxyRequestBodyTooLargeException("请求体超过代理大小限制。");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static void CopyRequestHeaders(HttpRequest source, HttpRequestMessage target)
    {
        foreach (var header in source.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key) ||
                LocalOnlyHeaders.Contains(header.Key) ||
                header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            target.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        target.Headers.TryAddWithoutValidation("X-Forwarded-Host", source.Host.Value);
        target.Headers.TryAddWithoutValidation("X-Forwarded-Proto", source.Scheme);
    }

    private static void CopyRequestContentHeaders(HttpRequest source, HttpContent target)
    {
        foreach (var header in source.Headers)
        {
            if (!header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            target.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    private static async Task CopyProxyResponseAsync(
        HttpContext context,
        HttpResponseMessage proxyResponse,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = (int)proxyResponse.StatusCode;

        foreach (var header in proxyResponse.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        foreach (var header in proxyResponse.Content.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        context.Response.Headers.Remove("transfer-encoding");
        context.Response.Headers[ProxyMarkerHeader] = "1";
        await proxyResponse.Content.CopyToAsync(context.Response.Body, cancellationToken);
    }

    private static async Task WriteProxyErrorAsync(
        HttpContext context,
        int statusCode,
        string message,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers[ProxyMarkerHeader] = "1";

        var body = JsonSerializer.Serialize(new ProxyErrorResponse(statusCode, message));
        await context.Response.WriteAsync(body, cancellationToken);
    }

    private static async Task TryWriteProxyErrorAsync(
        HttpContext context,
        int statusCode,
        string message,
        CancellationToken cancellationToken)
    {
        if (IsClientAbort(context, cancellationToken))
        {
            return;
        }

        try
        {
            await WriteProxyErrorAsync(context, statusCode, message, cancellationToken);
        }
        catch (OperationCanceledException) when (IsClientAbort(context, cancellationToken))
        {
        }
    }

    private static bool IsClientAbort(HttpContext context, CancellationToken cancellationToken)
    {
        return context.RequestAborted.IsCancellationRequested ||
            cancellationToken.IsCancellationRequested;
    }

    private sealed record ProxyErrorResponse(int StatusCode, string Message);

    private sealed class ProxyRequestBodyTooLargeException(string message) : Exception(message);
}
