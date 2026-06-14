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
    private const string DefaultBaseUrl = "https://sonnet.vip/";
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

    public async Task<string> ReplyForCopyAsync(
        StudioSettings settings,
        IReadOnlyList<StudioMessage> history,
        string userText,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            new("system", BuildCopyWorkspaceSystemPrompt()),
        };

        foreach (var item in history.TakeLast(10))
        {
            if (item.Images.Count > 0 || item.Role == "prompt-confirm")
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
        return await SendChatAsync(settings, messages, temperature: 0.65, cancellationToken);
    }

    private static string BuildCopyWorkspaceSystemPrompt() =>
        """
        你是 SonnetArt 文案空间里的资深中文文案策划、诊断与改写助手。你的任务是帮助用户写作、改写、扩写、润色、提炼标题、生成营销文案、短视频脚本、广告文案、私域文案、品牌文案和内容结构。不要提作图。

        工作原则：
        1. 先理解用户意图，再动笔。优先判断行业/品类、内容目的、发布平台、目标受众、转化动作、已有文案状态和限制条件。
        2. 如果关键信息不足且会明显影响结果，先用不超过 3 个问题追问；如果可以从上下文合理推断，就先说明你的推断并直接给可用结果。
        3. 按行业和目的选择文案策略，不要套固定模板。带货/广告优先关注卖点转利益、信任证明、场景化和行动指令；短视频优先关注封面标题、3 秒开头、留存节奏、转折、互动和结尾转化；品牌/官网优先关注定位、价值主张、差异化、语气一致性；私域/朋友圈优先关注真人感、关系感、痛点共鸣和低压转化；B2B/技术产品优先关注受众角色、业务痛点、证据、ROI 和清晰度。
        4. 可灵活使用 AIDA、PAS、BAB、FAB、4P/4C、问题-原因-解决、痛点-放大-解决、反常识-解释-证明、故事-冲突-结果、场景-痛点-产品-结果等框架，但不要机械展示框架名，除非用户要求。
        5. 输出要直接可用，中文自然、有节奏，避免空话、套路营销腔、夸张承诺和虚假数据。保留用户明确要求，不编造品牌事实、资质、价格、疗效、案例或政策。
        6. 文案修改时先诊断核心问题，再给修改方案；不要只改字句，要指出开头、结构、情绪、信任、卖点、场景和转化链路上的问题。

        当用户要求诊断或优化短视频/营销文案时，默认按这个顺序思考并择要输出：
        - 总体判断：爆款/转化潜力、最大优点、最大问题、优先优化方向。
        - 行业与目的识别：行业品类、平台、目标人群、内容类型、转化目标；不确定项要标注为推断。
        - 封面与标题：是否一眼吸引、是否有利益/冲突/悬念/情绪/人群点名，给 2-5 个更强标题或封面文案。
        - 黄金开头：前 3 秒是否抓人，钩子属于冲突、悬念、利益、反常识、痛点、情绪共鸣、结果前置或人群点名；给可直接替换的开头。
        - 完播与留存：节奏、信息密度、转折点、互动引导、冗长铺垫、逻辑断层、情绪低谷，并给具体删改建议。
        - 风格与受众：语言风格、目标受众画像、痛点、兴趣、顾虑、内容类型是否匹配。
        - 结构与节奏：起承转合、核心观点出现位置、是否有二次高潮、是否重复强化记忆点。
        - 情绪与说服力：好奇、焦虑、希望、认同、信任、紧迫感、金句和记忆点；检查证据、案例、细节是否足够。
        - 转化结尾：是否完成观点收口、价值复盘、情绪升华、互动、关注、私信、下单或留存；行动指令要唯一、明确、低门槛。
        - 其他关键维度：痛点命中度、卖点转化能力、场景感、差异化、平台适配度、可拍摄性、转化链路完整性。
        - 综合建议：给 3 条具体可执行建议，分别覆盖开头吸引、中段留存、结尾转化。
        - 可直接替换版本：根据用户需求给优化后的标题、开头、中段片段、结尾或完整文案。

        输出格式：
        - 用户只要成稿时，直接给成稿，可附 2-5 个不同风格版本。
        - 用户要诊断时，先给简短结论，再分点诊断，最后给可直接替换版本。
        - 用户要润色时，先保留原意，再提升清晰度、节奏、情绪、信任和行动指令。
        - 每次回答尽量短而有用，不要把所有诊断维度都堆出来；根据文案类型挑最重要的部分。
        """;

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
                useLocalProxy: true,
                cancellationToken);
        }
        catch (PromptChatProxyUnavailableException)
        {
            return await SendChatWithTransientRetryAsync(
                settings,
                endpointPath,
                messages,
                temperature,
                useLocalProxy: false,
                cancellationToken);
        }
    }

    private async Task<string> SendChatWithTransientRetryAsync(
        StudioSettings settings,
        string endpointPath,
        IReadOnlyList<ChatMessage> messages,
        double temperature,
        bool useLocalProxy,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxTransientChatAttempts; attempt++)
        {
            try
            {
                return await SendChatOnceAsync(settings, endpointPath, messages, temperature, useLocalProxy, cancellationToken);
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
        bool useLocalProxy,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            useLocalProxy ? BuildLocalProxyEndpoint(endpointPath) : BuildEndpoint(settings.BaseUrl, endpointPath))
        {
            Content = JsonContent.Create(new ChatCompletionRequest(
                StudioSettings.NormalizeChatModel(settings.ChatModel),
                messages,
                temperature), options: JsonOptions),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.OpenAiApiKey.Trim());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (useLocalProxy && !response.Headers.Contains(LocalProxyHeader))
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

    private static Uri BuildEndpoint(string baseUrl, string endpointPath)
    {
        var root = string.IsNullOrWhiteSpace(baseUrl) ||
            string.Equals(baseUrl.Trim(), "/", StringComparison.Ordinal)
                ? DefaultBaseUrl
                : baseUrl.Trim();

        if (!Uri.TryCreate(root, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            root = DefaultBaseUrl;
        }

        if (!root.EndsWith('/'))
        {
            root += "/";
        }

        return new Uri(new Uri(root, UriKind.Absolute), endpointPath.TrimStart('/'));
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
