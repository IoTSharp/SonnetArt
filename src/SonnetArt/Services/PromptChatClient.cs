using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SonnetArt.Models;

namespace SonnetArt.Services;

public sealed class PromptChatClient
{
    private const string LocalProxyRoot = "/api/openai/";
    private const string LocalProxyHeader = "X-SonnetArt-Proxy";
    private const string DefaultModel = "gpt-5.5";
    private const int MaxTransientChatAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;

    public PromptChatClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PromptIntentResult> AnalyzeIntentAsync(
        StudioSettings settings,
        string userText,
        CancellationToken cancellationToken)
    {
        var content = await SendChatAsync(
            settings,
            [
                new ChatMessage("system", "你是一个作图工作台的意图分类器。只返回 JSON，不要解释。格式：{\"image\":true|false,\"reason\":\"一句中文\"}。当用户要求生成、画、做、设计、绘制、出图、改图或描述想要的画面时，image=true。"),
                new ChatMessage("user", userText),
            ],
            temperature: 0,
            cancellationToken);

        try
        {
            var result = JsonSerializer.Deserialize<PromptIntentResult>(ExtractJson(content), JsonOptions);
            return result ?? new PromptIntentResult(true, "准备作图");
        }
        catch (JsonException)
        {
            return new PromptIntentResult(true, "准备作图");
        }
    }

    public async Task<string> PolishPromptAsync(
        StudioSettings settings,
        string userText,
        CancellationToken cancellationToken)
    {
        var content = await SendChatAsync(
            settings,
            [
                new ChatMessage("system", "你是专业图片提示词编辑。把用户的中文需求润色成适合作图模型的单段中文提示词。保留用户明确要求，不新增人物身份、品牌或敏感内容，不解释，不加标题。"),
                new ChatMessage("user", userText),
            ],
            temperature: 0.4,
            cancellationToken);

        return string.IsNullOrWhiteSpace(content) ? userText : content.Trim();
    }

    public async Task<string> ReplyAsync(
        StudioSettings settings,
        IReadOnlyList<StudioMessage> history,
        string userText,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            new("system", "你是 SonnetArt 作图工作台里的简洁中文助手。用户闲聊或询问时正常回答；如果用户要作图，提醒他可以直接描述画面。"),
        };

        foreach (var item in history.TakeLast(8))
        {
            if (item.Images.Count > 0)
            {
                continue;
            }

            var role = item.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";
            if (!string.IsNullOrWhiteSpace(item.Content))
            {
                messages.Add(new ChatMessage(role, item.Content));
            }
        }

        messages.Add(new ChatMessage("user", userText));
        return await SendChatAsync(settings, messages, temperature: 0.5, cancellationToken);
    }

    private async Task<string> SendChatAsync(
        StudioSettings settings,
        IReadOnlyList<ChatMessage> messages,
        double temperature,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.OpenAiApiKey))
        {
            throw new InvalidOperationException("账户尚未准备好会话能力，请重新登录。");
        }

        const string endpointPath = "v1/chat/completions";
        try
        {
            return await SendChatWithTransientRetryAsync(
                settings,
                endpointPath,
                messages,
                temperature,
                cancellationToken);
        }
        catch (PromptChatProxyUnavailableException)
        {
            throw new HttpRequestException("会话代理不可用，请检查 SonnetArt 服务配置。");
        }
    }

    private async Task<string> SendChatWithTransientRetryAsync(
        StudioSettings settings,
        string endpointPath,
        IReadOnlyList<ChatMessage> messages,
        double temperature,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxTransientChatAttempts; attempt++)
        {
            try
            {
                return await SendChatOnceAsync(settings, endpointPath, messages, temperature, cancellationToken);
            }
            catch (HttpRequestException ex) when (!cancellationToken.IsCancellationRequested &&
                IsTransientChatStatusCode(ex.StatusCode) &&
                attempt < MaxTransientChatAttempts)
            {
                await Task.Delay(attempt == 1 ? 250 : 750, cancellationToken);
            }
        }

        throw new HttpRequestException("会话接口请求失败。");
    }

    private async Task<string> SendChatOnceAsync(
        StudioSettings settings,
        string endpointPath,
        IReadOnlyList<ChatMessage> messages,
        double temperature,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildLocalProxyEndpoint(endpointPath))
        {
            Content = JsonContent.Create(new ChatCompletionRequest(
                string.IsNullOrWhiteSpace(settings.ChatModel) ? DefaultModel : settings.ChatModel.Trim(),
                messages,
                temperature), options: JsonOptions),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.OpenAiApiKey.Trim());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.Headers.Contains(LocalProxyHeader))
        {
            throw new PromptChatProxyUnavailableException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"会话接口返回 {(int)response.StatusCode} {response.ReasonPhrase}: {ExtractErrorMessage(raw)}",
                null,
                response.StatusCode);
        }

        var chat = JsonSerializer.Deserialize<ChatCompletionResponse>(raw, JsonOptions);
        return chat?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? string.Empty;
    }

    private static bool IsTransientChatStatusCode(HttpStatusCode? statusCode)
    {
        return statusCode is null or
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;
    }

    private static Uri BuildLocalProxyEndpoint(string endpointPath)
    {
        return new Uri(LocalProxyRoot + endpointPath.TrimStart('/'), UriKind.Relative);
    }

    private static string ExtractJson(string value)
    {
        var trimmed = value.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : trimmed;
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
            return string.IsNullOrWhiteSpace(message) ? raw : message;
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static string? JsonText(JsonNode? node)
    {
        return node is JsonValue value &&
            value.TryGetValue<string>(out var text) &&
            !string.IsNullOrWhiteSpace(text)
                ? text.Trim()
                : null;
    }

    private sealed class PromptChatProxyUnavailableException : Exception;
}

public sealed record PromptIntentResult(
    [property: JsonPropertyName("image")] bool Image,
    [property: JsonPropertyName("reason")] string Reason);

internal sealed record ChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
    [property: JsonPropertyName("temperature")] double Temperature);

internal sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

internal sealed class ChatCompletionResponse
{
    [JsonPropertyName("choices")] public List<ChatCompletionChoice>? Choices { get; set; }
}

internal sealed class ChatCompletionChoice
{
    [JsonPropertyName("message")] public ChatCompletionMessage? Message { get; set; }
}

internal sealed class ChatCompletionMessage
{
    [JsonPropertyName("content")] public string? Content { get; set; }
}
