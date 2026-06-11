using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SonnetArt.ImageStudio.Models;

namespace SonnetArt.ImageStudio.Services;

public sealed class ImageGenerationClient
{
    private const string LocalProxyRoot = "/api/openai/";
    private const string LocalProxyHeader = "X-Cosmos-Sonnet-Proxy";
    private const string UpstreamBaseHeader = "X-Cosmos-Sonnet-Base";
    private const string HttpProxyHeader = "X-Cosmos-Sonnet-Http-Proxy";
    private const string DefaultImageBaseUrl = "https://sonnet.vip/";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly HttpClient _httpClient;

    public ImageGenerationClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<StudioImageResult> GenerateAsync(StudioImageRequest studioRequest, CancellationToken cancellationToken)
    {
        var settings = studioRequest.Settings;
        if (string.IsNullOrWhiteSpace(settings.ImageApiKey))
        {
            throw new InvalidOperationException("请先登录账户。");
        }
        ValidateRequest(studioRequest);
        var upstreamEndpoint = BuildLegacyEndpoint(settings.BaseUrl, studioRequest);

        try
        {
            return await SendOnceAsync(studioRequest, upstreamEndpoint, useLocalProxy: true, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw CreateImageTimeoutException(ex);
        }
        catch (HttpRequestException ex) when (LooksLikeImageTimeout(ex.Message))
        {
            throw CreateImageTimeoutException(ex);
        }
        catch (ImageProxyUnavailableException)
        {
            try
            {
                return await SendOnceAsync(studioRequest, upstreamEndpoint, useLocalProxy: false, cancellationToken);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw CreateImageTimeoutException(ex);
            }
            catch (HttpRequestException ex) when (LooksLikeImageTimeout(ex.Message))
            {
                throw CreateImageTimeoutException(ex);
            }
            catch (HttpRequestException ex) when (LooksLikeBrowserLoadFailure(ex.Message))
            {
                throw new HttpRequestException("无法连接图像接口：浏览器直连失败，请确认本地 Host 正在运行，或检查网络/代理设置。", ex);
            }
        }
    }

    private async Task<StudioImageResult> SendOnceAsync(
        StudioImageRequest studioRequest,
        Uri upstreamEndpoint,
        bool useLocalProxy,
        CancellationToken cancellationToken)
    {
        var settings = studioRequest.Settings;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            useLocalProxy ? BuildLocalProxyEndpoint(upstreamEndpoint) : upstreamEndpoint)
        {
            Content = RequiresMultipart(studioRequest)
                ? BuildMultipartContent(studioRequest)
                : JsonContent.Create(BuildPayload(studioRequest), options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ImageApiKey.Trim());
        if (useLocalProxy)
        {
            request.Headers.TryAddWithoutValidation(UpstreamBaseHeader, BuildProxyUpstreamRoot(upstreamEndpoint));
            if (!string.IsNullOrWhiteSpace(settings.SonnetProxyUrl))
            {
                request.Headers.TryAddWithoutValidation(HttpProxyHeader, settings.SonnetProxyUrl.Trim());
            }
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (useLocalProxy && !response.Headers.Contains(LocalProxyHeader))
        {
            throw new ImageProxyUnavailableException();
        }

        var rawBuilder = new StringBuilder();
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var images = Array.Empty<GeneratedImage>() as IReadOnlyList<GeneratedImage>;
        if (mediaType.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            images = await ParseStreamingImagesAsync(stream, settings.Format, rawBuilder, cancellationToken);
        }
        else
        {
            rawBuilder.Append(await response.Content.ReadAsStringAsync(cancellationToken));
            images = ParseImages(rawBuilder.ToString(), settings.Format);
        }

        var raw = rawBuilder.ToString();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"图像接口返回 {(int)response.StatusCode} {response.ReasonPhrase}: {ExtractErrorMessage(raw)}。{BuildErrorContext(studioRequest, upstreamEndpoint)}");
        }

        return new StudioImageResult(images, raw);
    }

    private static Uri BuildLocalProxyEndpoint(Uri upstreamEndpoint)
    {
        upstreamEndpoint = NormalizeLegacyVariationsEndpoint(upstreamEndpoint);
        var path = upstreamEndpoint.AbsolutePath.TrimStart('/');
        return new Uri(LocalProxyRoot + path + upstreamEndpoint.Query, UriKind.Relative);
    }

    private static string BuildProxyUpstreamRoot(Uri upstreamEndpoint)
    {
        upstreamEndpoint = NormalizeLegacyVariationsEndpoint(upstreamEndpoint);
        return upstreamEndpoint.GetLeftPart(UriPartial.Authority) + "/";
    }

    private static Uri NormalizeLegacyVariationsEndpoint(Uri endpoint)
    {
        return endpoint.AbsolutePath.TrimEnd('/').EndsWith("/images/variations", StringComparison.OrdinalIgnoreCase)
            ? new Uri(ReplaceLastPathSegment(endpoint.ToString(), "edits"), UriKind.Absolute)
            : endpoint;
    }

    private static Uri BuildLegacyEndpoint(string baseUrl, StudioImageRequest request)
    {
        var normalized = NormalizeKnownOpenAiBaseUrl(NormalizeBaseUrl(baseUrl));
        var imageEndpoint = TrimTrailingSlashBeforeQuery(normalized);

        if (imageEndpoint.EndsWith("/v1/images/generations", StringComparison.OrdinalIgnoreCase) ||
            imageEndpoint.EndsWith("/images/generations", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(imageEndpoint, UriKind.Absolute);
        }

        if (imageEndpoint.EndsWith("/v1/images/edits", StringComparison.OrdinalIgnoreCase) ||
            imageEndpoint.EndsWith("/images/edits", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(imageEndpoint, UriKind.Absolute);
        }

        if (imageEndpoint.EndsWith("/v1/images/variations", StringComparison.OrdinalIgnoreCase) ||
            imageEndpoint.EndsWith("/images/variations", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(ReplaceLastPathSegment(imageEndpoint, "edits"), UriKind.Absolute);
        }

        if (!normalized.EndsWith('/'))
        {
            normalized += "/";
        }

        var path = RequiresEditEndpoint(request)
                ? "images/edits"
                : "images/generations";

        return new Uri(BuildOpenAiRoot(normalized), path);
    }

    private static string TrimTrailingSlashBeforeQuery(string value)
    {
        var queryIndex = value.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0)
        {
            return value.TrimEnd('/');
        }

        return value[..queryIndex].TrimEnd('/') + value[queryIndex..];
    }

    private static Uri BuildOpenAiRoot(string normalizedBaseUrl)
    {
        var uri = new Uri(normalizedBaseUrl, UriKind.Absolute);
        var path = uri.AbsolutePath.Trim('/');
        if (path.Equals("v1", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        return new Uri(uri, "v1/");
    }

    private static string ReplaceLastPathSegment(string normalizedUrl, string segment)
    {
        var queryIndex = normalizedUrl.IndexOf('?', StringComparison.Ordinal);
        var query = queryIndex >= 0 ? normalizedUrl[queryIndex..] : string.Empty;
        var path = (queryIndex >= 0 ? normalizedUrl[..queryIndex] : normalizedUrl).TrimEnd('/');
        var slashIndex = path.LastIndexOf('/');
        return slashIndex < 0
            ? normalizedUrl
            : $"{path[..(slashIndex + 1)]}{segment}{query}";
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var normalized = string.IsNullOrWhiteSpace(baseUrl)
            ? DefaultImageBaseUrl
            : baseUrl.Trim();

        if (!normalized.EndsWith('/'))
        {
            normalized += "/";
        }

        return normalized;
    }

    private static string NormalizeKnownOpenAiBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, "sonnet.vip", StringComparison.OrdinalIgnoreCase))
        {
            return baseUrl;
        }

        var path = uri.AbsolutePath.Trim('/');
        if (path.Equals("api/v1", StringComparison.OrdinalIgnoreCase))
        {
            return $"{uri.Scheme}://{uri.Authority}/v1/";
        }

        const string apiImagePrefix = "/api/v1/images/";
        if (uri.AbsolutePath.StartsWith(apiImagePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return $"{uri.Scheme}://{uri.Authority}/v1/images/{uri.AbsolutePath[apiImagePrefix.Length..]}{uri.Query}";
        }

        return baseUrl;
    }

    private static bool RequiresMultipart(StudioImageRequest studioRequest)
    {
        return RequiresEditEndpoint(studioRequest) && studioRequest.ReferenceFiles.Count > 0;
    }

    private static bool RequiresEditEndpoint(StudioImageRequest studioRequest)
    {
        return string.Equals(studioRequest.Mode, "image", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(studioRequest.Mode, "edit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(studioRequest.Mode, "variation", StringComparison.OrdinalIgnoreCase) ||
            studioRequest.ReferenceFiles.Count > 0 ||
            studioRequest.ImageReferences.Count > 0;
    }

    private static string NormalizeImageSize(string? size, string model)
    {
        var value = size?.Trim();
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return "auto";
        }

        return value;
    }

    private static ImageGenerationRequest BuildPayload(StudioImageRequest studioRequest)
    {
        var settings = ImageModelCatalog.NormalizeSettings(studioRequest.Settings, studioRequest.Mode);
        var capabilities = ImageModelCatalog.Get(settings.Model, studioRequest.Mode);
        var model = capabilities.Model;
        var stream = capabilities.SupportsStream &&
            string.Equals(settings.RequestMode, "stream", StringComparison.OrdinalIgnoreCase);

        var request = new ImageGenerationRequest
        {
            Model = model,
            Prompt = ResolvePrompt(studioRequest),
            Count = Math.Clamp(studioRequest.Count, 1, 8),
            Size = NullIfAuto(NormalizeImageSize(settings.Size, model)),
            Quality = NullIfAuto(settings.Quality),
            Style = capabilities.Styles.Count > 0 ? NullIfDefault(settings.Style) : null,
            Background = capabilities.SupportsBackground ? NullIfAuto(settings.Background) : null,
            OutputFormat = capabilities.SupportsOutputFormat ? NullIfAuto(settings.Format) : null,
            OutputCompression = !capabilities.SupportsOutputCompression || string.Equals(settings.Format, "png", StringComparison.OrdinalIgnoreCase)
                ? null
                : Math.Clamp(settings.Compression, 0, 100),
            Moderation = capabilities.SupportsModeration ? NullIfAuto(settings.Moderation) : null,
            InputFidelity = ResolveInputFidelity(studioRequest),
            ResponseFormat = null,
            Stream = stream ? true : null,
            PartialImages = stream
                ? Math.Clamp(settings.PartialImages, 0, 3)
                : null,
            User = string.IsNullOrWhiteSpace(settings.User) ? null : settings.User.Trim(),
            Images = ParseImageReferences(studioRequest.ImageReferences).ToList(),
            Mask = ParseImageReference(studioRequest.MaskReference),
        };

        foreach (var file in studioRequest.ReferenceFiles)
        {
            request.Images.Add(new ImageReferencePayload
            {
                ImageUrl = ToDataUrl(file),
            });
        }

        MergeAdvancedJson(request, settings.AdvancedJson);
        return request;
    }

    private static MultipartFormDataContent BuildMultipartContent(StudioImageRequest studioRequest)
    {
        var settings = ImageModelCatalog.NormalizeSettings(studioRequest.Settings, studioRequest.Mode);
        var content = new MultipartFormDataContent();
        var capabilities = ImageModelCatalog.Get(settings.Model, studioRequest.Mode);
        var model = capabilities.Model;
        AddString(content, "model", model);
        AddString(content, "prompt", ResolvePrompt(studioRequest));
        AddString(content, "n", Math.Clamp(studioRequest.Count, 1, 8).ToString());
        AddString(content, "size", NullIfAuto(NormalizeImageSize(settings.Size, model)));
        AddString(content, "quality", NullIfAuto(settings.Quality));
        AddString(content, "style", capabilities.Styles.Count > 0 ? NullIfDefault(settings.Style) : null);
        AddString(content, "background", capabilities.SupportsBackground ? NullIfAuto(settings.Background) : null);
        AddString(content, "output_format", capabilities.SupportsOutputFormat ? NullIfAuto(settings.Format) : null);
        if (capabilities.SupportsOutputCompression && !string.Equals(settings.Format, "png", StringComparison.OrdinalIgnoreCase))
        {
            AddString(content, "output_compression", Math.Clamp(settings.Compression, 0, 100).ToString());
        }
        AddString(content, "moderation", capabilities.SupportsModeration ? NullIfAuto(settings.Moderation) : null);
        AddString(content, "input_fidelity", ResolveInputFidelity(studioRequest));
        AddString(content, "response_format", null);
        AddString(content, "user", string.IsNullOrWhiteSpace(settings.User) ? null : settings.User.Trim());
        var stream = capabilities.SupportsStream && string.Equals(settings.RequestMode, "stream", StringComparison.OrdinalIgnoreCase);
        if (stream)
        {
            AddString(content, "stream", "true");
            AddString(content, "partial_images", Math.Clamp(settings.PartialImages, 0, 3).ToString());
        }

        foreach (var file in studioRequest.ReferenceFiles)
        {
            var fileContent = new ByteArrayContent(file.Content);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
            content.Add(fileContent, "image", string.IsNullOrWhiteSpace(file.FileName) ? "reference.png" : file.FileName);
        }

        foreach (var reference in ParseImageReferences(studioRequest.ImageReferences))
        {
            AddString(content, "image", reference.FileId ?? reference.ImageUrl);
        }

        var mask = ParseImageReference(studioRequest.MaskReference);
        AddString(content, "mask", mask?.FileId ?? mask?.ImageUrl);

        AddAdvancedJsonFields(content, settings.AdvancedJson);
        return content;
    }

    private static void AddString(MultipartFormDataContent content, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        content.Add(new StringContent(value, Encoding.UTF8), name);
    }

    private static void AddAdvancedJsonFields(MultipartFormDataContent content, string advancedJson)
    {
        if (string.IsNullOrWhiteSpace(advancedJson))
        {
            return;
        }

        try
        {
            if (JsonNode.Parse(advancedJson) is not JsonObject obj)
            {
                return;
            }

            foreach (var pair in obj)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
                {
                    continue;
                }

                var value = AdvancedFieldToFormValue(pair.Value);
                if (value is not null)
                {
                    AddString(content, pair.Key, value);
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private static void ValidateRequest(StudioImageRequest request)
    {
        var settings = ImageModelCatalog.NormalizeSettings(request.Settings, request.Mode);
        if (settings.Background == "transparent" &&
            string.Equals(settings.Format, "jpeg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("透明背景不能使用 jpeg 输出，请改用 png 或 webp。");
        }

        if (string.Equals(request.Mode, "variation", StringComparison.OrdinalIgnoreCase))
        {
            if (request.ReferenceFiles.Count == 0 && request.ImageReferences.Count == 0)
            {
                throw new InvalidOperationException("生成变化需要选择一张参考图。");
            }

            if (request.ReferenceFiles.Count > 1 || request.ImageReferences.Count > 1)
            {
                throw new InvalidOperationException("生成变化一次只能使用一张图片。");
            }
        }

        if (RequiresEditEndpoint(request) &&
            request.ReferenceFiles.Count == 0 &&
            request.ImageReferences.Count == 0)
        {
            throw new InvalidOperationException("图生图或编辑模式需要至少一张输入图片。");
        }
    }

    private static void MergeAdvancedJson(ImageGenerationRequest request, string advancedJson)
    {
        if (string.IsNullOrWhiteSpace(advancedJson))
        {
            return;
        }

        try
        {
            var node = JsonNode.Parse(advancedJson);
            if (node is not JsonObject obj)
            {
                return;
            }

            foreach (var pair in obj)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
                {
                    continue;
                }

                if (ApplyKnownAdvancedField(request, pair.Key, pair.Value))
                {
                    continue;
                }

                request.ExtensionData[pair.Key] = pair.Value.Deserialize<object?>(JsonOptions);
            }
        }
        catch (JsonException)
        {
            // Advanced JSON is optional; invalid drafts should not block the main request.
        }
    }

    private static bool ApplyKnownAdvancedField(ImageGenerationRequest request, string key, JsonNode value)
    {
        switch (key.Trim())
        {
            case "model":
                request.Model = ImageModelCatalog.NormalizeModel(StringFromJson(value) ?? request.Model);
                return true;
            case "prompt":
                request.Prompt = StringFromJson(value) ?? request.Prompt;
                return true;
            case "n":
                request.Count = ClampIntFromJson(value, request.Count, 1, 8);
                return true;
            case "size":
                var size = StringFromJson(value);
                request.Size = ImageModelCatalog.NormalizeSize(size, ImageModelCatalog.Get(request.Model).Sizes, request.Size ?? "1024x1024");
                return true;
            case "quality":
                request.Quality = NormalizeAdvancedChoice(StringFromJson(value), ImageModelCatalog.Get(request.Model).Qualities, request.Quality);
                return true;
            case "style":
                request.Style = ImageModelCatalog.Get(request.Model).Styles.Count > 0
                    ? StringFromJson(value)
                    : null;
                return true;
            case "background":
                var background = StringFromJson(value);
                request.Background = IsSupportedAdvancedChoice(background, ImageModelCatalog.Get(request.Model).Backgrounds)
                    ? background!.Trim()
                    : null;
                return true;
            case "output_format":
                var outputFormat = StringFromJson(value);
                request.OutputFormat = IsSupportedAdvancedChoice(outputFormat, ImageModelCatalog.Get(request.Model).OutputFormats)
                    ? outputFormat!.Trim()
                    : request.OutputFormat;
                return true;
            case "output_compression":
                request.OutputCompression = ClampIntFromJson(value, request.OutputCompression ?? 100, 0, 100);
                return true;
            case "moderation":
                request.Moderation = NormalizeAdvancedChoice(StringFromJson(value), ["auto", "low"], request.Moderation);
                return true;
            case "input_fidelity":
                request.InputFidelity = ImageModelCatalog.Get(request.Model).SupportsInputFidelity
                    ? StringFromJson(value)
                    : null;
                return true;
            case "response_format":
                request.ResponseFormat = null;
                return true;
            case "stream":
                request.Stream = ImageModelCatalog.Get(request.Model).SupportsStream ? BoolFromJson(value) : null;
                return true;
            case "partial_images":
                request.PartialImages = ImageModelCatalog.Get(request.Model).SupportsPartialImages
                    ? ClampIntFromJson(value, request.PartialImages ?? 0, 0, 3)
                    : null;
                return true;
            case "user":
                request.User = StringFromJson(value);
                return true;
            case "images":
            case "image":
            case "mask":
                return true;
            default:
                return false;
        }
    }

    private static string BuildErrorContext(StudioImageRequest studioRequest, Uri upstreamEndpoint)
    {
        var settings = ImageModelCatalog.NormalizeSettings(studioRequest.Settings, studioRequest.Mode);
        var capabilities = ImageModelCatalog.Get(settings.Model, studioRequest.Mode);
        var endpoint = upstreamEndpoint.AbsolutePath.EndsWith("/images/edits", StringComparison.OrdinalIgnoreCase)
            ? "images/edits"
            : "images/generations";
        var requestMode = capabilities.SupportsStream &&
            string.Equals(settings.RequestMode, "stream", StringComparison.OrdinalIgnoreCase)
                ? "stream"
                : "sync";

        var parts = new List<string>
        {
            $"model={settings.Model}",
            $"endpoint={endpoint}",
            $"mode={studioRequest.Mode}",
            $"size={NormalizeImageSize(settings.Size, settings.Model)}",
            $"quality={settings.Quality}",
            $"request={requestMode}",
        };

        if (capabilities.SupportsOutputFormat)
        {
            parts.Add($"output_format={settings.Format}");
        }

        if (capabilities.SupportsBackground)
        {
            parts.Add($"background={settings.Background}");
        }

        return $"请求上下文：{string.Join("，", parts)}。";
    }

    private static string? NormalizeAdvancedChoice(string? value, IReadOnlyList<string> options, string? fallback)
    {
        var trimmed = value?.Trim();
        return options.FirstOrDefault(option => string.Equals(option, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? fallback;
    }

    private static bool IsSupportedAdvancedChoice(string? value, IReadOnlyList<string> options)
    {
        var trimmed = value?.Trim();
        return options.Any(option => string.Equals(option, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolvePrompt(StudioImageRequest request)
    {
        var prompt = request.Prompt.Trim();
        if (prompt.Length > 0)
        {
            return prompt;
        }

        return string.Equals(request.Mode, "variation", StringComparison.OrdinalIgnoreCase)
            ? "基于参考图生成一个自然变化版本，保持主体一致，细节和构图略有变化。"
            : prompt;
    }

    private static string? AdvancedFieldToFormValue(JsonNode value)
    {
        var element = value.GetValueKind() == JsonValueKind.Undefined
            ? default
            : value.Deserialize<JsonElement>(JsonOptions);

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.GetRawText(),
        };
    }

    private static string? StringFromJson(JsonNode value)
    {
        var element = value.Deserialize<JsonElement>(JsonOptions);
        return element.ValueKind switch
        {
            JsonValueKind.String => NullIfEmpty(element.GetString()),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.GetRawText(),
            _ => null,
        };
    }

    private static int? IntFromJson(JsonNode value)
    {
        var element = value.Deserialize<JsonElement>(JsonOptions);
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
        {
            return number;
        }

        if (element.ValueKind == JsonValueKind.String &&
            int.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int ClampIntFromJson(JsonNode value, int fallback, int min, int max)
    {
        return Math.Clamp(IntFromJson(value) ?? fallback, min, max);
    }

    private static bool? BoolFromJson(JsonNode value)
    {
        var element = value.Deserialize<JsonElement>(JsonOptions);
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static async Task<IReadOnlyList<GeneratedImage>> ParseStreamingImagesAsync(
        Stream stream,
        string format,
        StringBuilder rawBuilder,
        CancellationToken cancellationToken)
    {
        var images = new List<GeneratedImage>();
        var partialImages = new List<GeneratedImage>();
        await foreach (var sseEvent in ReadServerSentEventsAsync(stream, rawBuilder, cancellationToken))
        {
            var data = sseEvent.Data;
            if (data is "[DONE]")
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(data);
                AddResponsesEventImages(images, partialImages, document.RootElement, format, sseEvent.Event);
            }
            catch (JsonException)
            {
            }
        }

        if (images.Count > 0)
        {
            return DeduplicateImages(images);
        }

        if (partialImages.Count > 0)
        {
            return [partialImages[^1]];
        }

        return ParseImages(rawBuilder.ToString(), format);
    }

    private static async IAsyncEnumerable<SseEvent> ReadServerSentEventsAsync(
        Stream stream,
        StringBuilder rawBuilder,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var dataBuilder = new StringBuilder();
        var eventName = string.Empty;
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            rawBuilder.AppendLine(line);
            if (line.Length == 0)
            {
                if (dataBuilder.Length > 0)
                {
                    yield return new SseEvent(eventName, dataBuilder.ToString().TrimEnd('\n'));
                    dataBuilder.Clear();
                    eventName = string.Empty;
                }

                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line[6..].Trim();
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            dataBuilder.AppendLine(line[5..].TrimStart());
        }

        if (dataBuilder.Length > 0)
        {
            yield return new SseEvent(eventName, dataBuilder.ToString().TrimEnd('\n'));
        }
    }

    private static IReadOnlyList<GeneratedImage> ParseImages(string raw, string format)
    {
        var images = new List<GeneratedImage>();
        var partialImages = new List<GeneratedImage>();
        ImageGenerationResponse? response = null;
        try
        {
            response = JsonSerializer.Deserialize<ImageGenerationResponse>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                var data = line[5..].Trim();
                if (data is "[DONE]")
                {
                    continue;
                }

                try
                {
                    response = JsonSerializer.Deserialize<ImageGenerationResponse>(data, JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                AddResponseImages(images, response, format);
                try
                {
                    using var eventDocument = JsonDocument.Parse(data);
                    AddResponsesEventImages(images, partialImages, eventDocument.RootElement, format);
                }
                catch (JsonException)
                {
                }
            }

            return images.Count > 0
                ? DeduplicateImages(images)
                : partialImages.Count > 0
                    ? [partialImages[^1]]
                    : [];
        }

        AddResponseImages(images, response, format);
        AddResponsesImages(images, raw, format);
        return DeduplicateImages(images);
    }

    private static void AddResponseImages(List<GeneratedImage> images, ImageGenerationResponse? response, string format)
    {
        if (response?.Data is not null)
        {
            foreach (var item in response.Data)
            {
                AddImage(images, item.Url, item.Base64Json, item.MimeType, item.RevisedPrompt, format);
            }
        }

        if (response?.Output is not null)
        {
            foreach (var item in response.Output)
            {
                AddImage(images, item.Url ?? item.Result, item.Base64Json, item.MimeType, item.RevisedPrompt, format);
            }
        }
    }

    private static void AddResponsesImages(List<GeneratedImage> images, string raw, string format)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            AddResponsesElementImages(images, document.RootElement, format);
        }
        catch (JsonException)
        {
        }
    }

    private static void AddResponsesEventImages(
        List<GeneratedImage> images,
        List<GeneratedImage> partialImages,
        JsonElement root,
        string format,
        string? eventName = null)
    {
        var target = IsPartialImageEvent(root, eventName) ? partialImages : images;
        if (TryGetString(root, "b64_json", out var b64Json) ||
            TryGetString(root, "partial_image_b64", out b64Json) ||
            TryGetString(root, "image_b64", out b64Json))
        {
            AddImage(target, null, b64Json, null, null, format);
        }

        if (TryGetString(root, "result", out var result))
        {
            AddImage(target, LooksLikeImageUrl(result) ? result : null, LooksLikeBase64Image(result) ? result : null, null, null, format);
        }

        if (root.TryGetProperty("item", out var item))
        {
            AddResponsesElementImages(target, item, format);
        }

        if (root.TryGetProperty("response", out var response))
        {
            AddResponsesElementImages(target, response, format);
        }
    }

    private static bool IsPartialImageEvent(JsonElement root, string? eventName)
    {
        if (!string.IsNullOrWhiteSpace(eventName) &&
            eventName.Contains("partial_image", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TryGetString(root, "type", out var type) &&
            type.Contains("partial_image", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddResponsesElementImages(List<GeneratedImage> images, JsonElement element, string format)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                AddImageFields(images, element, format);

                foreach (var property in element.EnumerateObject())
                {
                    AddResponsesElementImages(images, property.Value, format);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AddResponsesElementImages(images, item, format);
                }

                break;
        }
    }

    private static void AddImageFields(List<GeneratedImage> images, JsonElement element, string format)
    {
        if (TryGetFirstString(element, ["url", "image_url", "file_url"], out var url) &&
            LooksLikeImageUrl(url))
        {
            AddImage(images, url, null, GetOptionalString(element, "mime_type"), GetOptionalString(element, "revised_prompt"), format);
        }

        if (TryGetFirstString(element, ["result", "image", "image_data"], out var result))
        {
            AddImage(
                images,
                LooksLikeImageUrl(result) ? result : null,
                LooksLikeBase64Image(result) ? result : null,
                GetOptionalString(element, "mime_type"),
                GetOptionalString(element, "revised_prompt"),
                format);
        }

        if (TryGetFirstString(
            element,
            ["b64_json", "base64", "image_base64", "image_b64", "partial_image_b64", "data"],
            out var b64Json) &&
            LooksLikeBase64Image(b64Json))
        {
            AddImage(images, null, b64Json, GetOptionalString(element, "mime_type"), GetOptionalString(element, "revised_prompt"), format);
        }
    }

    private static bool TryGetFirstString(JsonElement element, IReadOnlyList<string> propertyNames, out string value)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetString(element, propertyName, out value))
            {
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return TryGetString(element, propertyName, out var value) ? value : null;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool LooksLikeImageUrl(string value)
    {
        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeBase64Image(string value)
    {
        if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return value.Length > 100 &&
            value.All(ch => char.IsLetterOrDigit(ch) || ch is '+' or '/' or '=' or '-' or '_');
    }

    private static IReadOnlyList<GeneratedImage> DeduplicateImages(List<GeneratedImage> images)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<GeneratedImage>();
        foreach (var image in images)
        {
            if (seen.Add(image.Url))
            {
                result.Add(image);
            }
        }

        return result;
    }

    private static void AddImage(
        List<GeneratedImage> images,
        string? url,
        string? b64Json,
        string? mimeType,
        string? revisedPrompt,
        string format)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            images.Add(new GeneratedImage
            {
                Url = url,
                RevisedPrompt = revisedPrompt,
                MimeType = mimeType,
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(b64Json))
        {
            var resolvedMime = string.IsNullOrWhiteSpace(mimeType)
                ? $"image/{(string.IsNullOrWhiteSpace(format) ? "png" : format.Trim())}"
                : mimeType;

            images.Add(new GeneratedImage
            {
                Url = $"data:{resolvedMime};base64,{b64Json}",
                RevisedPrompt = revisedPrompt,
                MimeType = resolvedMime,
            });
        }
    }

    private static string? NullIfAuto(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
    }

    private static string? NullIfDefault(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "默认", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
    }

    private static string? ResolveInputFidelity(StudioImageRequest studioRequest)
    {
        var capabilities = ImageModelCatalog.Get(studioRequest.Settings.Model, studioRequest.Mode);
        if (!RequiresEditEndpoint(studioRequest) || !capabilities.SupportsInputFidelity)
        {
            return null;
        }

        var explicitValue = NullIfDefault(studioRequest.Settings.InputFidelity);
        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue;
        }

        return string.IsNullOrWhiteSpace(studioRequest.MaskReference) ? null : "high";
    }

    private static string ExtractErrorMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "响应体为空";
        }

        try
        {
            var root = JsonNode.Parse(raw);
            var error = root?["error"];
            var message =
                JsonText(error?["message"]) ??
                JsonText(root?["message"]) ??
                JsonText(root?["detail"]) ??
                JsonText(root?["reason"]);
            var type = JsonText(error?["type"]) ?? JsonText(root?["type"]);
            var code = JsonText(error?["code"]) ?? JsonText(root?["code"]);
            var param = JsonText(error?["param"]) ?? JsonText(root?["param"]);

            var detailParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(type))
            {
                detailParts.Add(type);
            }

            if (!string.IsNullOrWhiteSpace(code) &&
                !string.Equals(code, type, StringComparison.OrdinalIgnoreCase))
            {
                detailParts.Add(code);
            }

            if (!string.IsNullOrWhiteSpace(param))
            {
                detailParts.Add($"param: {param}");
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                return detailParts.Count == 0
                    ? message
                    : $"{message} ({string.Join(", ", detailParts)})";
            }
        }
        catch (JsonException)
        {
        }

        return raw;
    }

    private static string? JsonText(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool LooksLikeBrowserLoadFailure(string? message)
    {
        return !string.IsNullOrWhiteSpace(message) &&
            (message.Contains("TypeError: Load failed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("TypeError: Failed to fetch", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Failed to fetch", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeImageTimeout(string? message)
    {
        return !string.IsNullOrWhiteSpace(message) &&
            (message.Contains("net_http_request_timedout", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("request timed out", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("The request was canceled due to the configured HttpClient.Timeout", StringComparison.OrdinalIgnoreCase));
    }

    private static HttpRequestException CreateImageTimeoutException(Exception innerException)
    {
        return new HttpRequestException("图像接口请求超时：这次生成耗时超过等待上限。已将应用等待时间提高到 10 分钟；如果仍然超时，请改用同步请求、降低数量/质量，或稍后重试。", innerException);
    }

    private static IEnumerable<ImageReferencePayload> ParseImageReferences(IReadOnlyList<string> references)
    {
        foreach (var reference in references)
        {
            var value = reference.Trim();
            if (value.Length == 0)
            {
                continue;
            }

            yield return value.StartsWith("file-", StringComparison.OrdinalIgnoreCase)
                ? new ImageReferencePayload { FileId = value }
                : new ImageReferencePayload { ImageUrl = value };
        }
    }

    private static ImageReferencePayload? ParseImageReference(string? reference)
    {
        var value = reference?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.StartsWith("file-", StringComparison.OrdinalIgnoreCase)
            ? new ImageReferencePayload { FileId = value }
            : new ImageReferencePayload { ImageUrl = value };
    }

    private static string ToDataUrl(ImageReferenceFile file)
    {
        var mimeType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType.Trim();
        return $"data:{mimeType};base64,{Convert.ToBase64String(file.Content)}";
    }

    private sealed class ImageProxyUnavailableException : Exception
    {
    }

    private readonly record struct SseEvent(string Event, string Data);
}
