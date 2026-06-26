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
    private const int MaxCopyEvidenceChars = 18000;
    private const int MaxCopyEvidenceItemChars = 1000;
    private const int MaxContinuationEvidenceChars = 24000;
    private const int MaxContinuationEvidenceItemChars = 1200;
    private const int MaxMemoryChars = 6000;
    private const int MaxMemorySourceChars = 16000;

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

    public async Task<string> RewriteImageContinuationPromptAsync(
        StudioSettings settings,
        IReadOnlyList<StudioMessage> history,
        string workspaceMemory,
        string sessionMemory,
        GeneratedImage latestImage,
        string userText,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            new("system", BuildImageContinuationSystemPrompt()),
            new("user", BuildImageContinuationUserPrompt(history, workspaceMemory, sessionMemory, latestImage, userText)),
        };

        var content = await SendChatAsync(settings, messages, temperature: 0.25, cancellationToken);
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
        string workspaceMemory,
        string sessionMemory,
        string userText,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            new("system", BuildCopyWorkspaceSystemPrompt()),
            new("user", BuildCopyWorkspaceUserPrompt(history, workspaceMemory, sessionMemory, userText)),
        };

        return await SendChatAsync(settings, messages, temperature: 0.65, cancellationToken);
    }

    public async Task<WorkspaceMemoryUpdate> RefreshWorkspaceMemoryAsync(
        StudioSettings settings,
        StudioWorkspace workspace,
        StudioSession activeSession,
        string latestUserText,
        string latestAssistantText,
        CancellationToken cancellationToken)
    {
        var content = await SendChatAsync(
            settings,
            [
                new ChatMessage("system", BuildMemoryRefreshSystemPrompt()),
                new ChatMessage("user", BuildMemoryRefreshUserPrompt(workspace, activeSession, latestUserText, latestAssistantText)),
            ],
            temperature: 0.1,
            cancellationToken);

        try
        {
            var update = JsonSerializer.Deserialize<WorkspaceMemoryUpdate>(ExtractJson(content), JsonOptions);
            return update?.Normalize() ?? WorkspaceMemoryUpdate.Empty;
        }
        catch (JsonException)
        {
            return WorkspaceMemoryUpdate.Empty;
        }
    }

    public async Task<CommerceProductAnalysis> AnalyzeCommerceProductAsync(
        StudioSettings settings,
        CommerceProduct product,
        CancellationToken cancellationToken)
    {
        product.Normalize();
        var userPrompt = BuildCommerceProductAnalysisUserPrompt(product);
        var userContent = ChatContent.FromTextAndImages(
            userPrompt,
            product.ReferenceImages.Where(IsVisionReference).Take(6));
        var content = await SendChatAsync(
            settings,
            [
                new ChatMessage("system", BuildCommerceProductAnalysisSystemPrompt()),
                new ChatMessage("user", userContent),
            ],
            temperature: 0.2,
            cancellationToken);

        try
        {
            var result = JsonSerializer.Deserialize<CommerceProductAnalysis>(ExtractJson(content), JsonOptions)
                ?? new CommerceProductAnalysis();
            result.Normalize();
            result.AnalyzedAt = DateTimeOffset.Now;
            return result;
        }
        catch (JsonException)
        {
            return new CommerceProductAnalysis
            {
                Summary = content.Trim(),
                AnalyzedAt = DateTimeOffset.Now,
            };
        }
    }

    public async Task<CommerceImagePlan> PlanCommerceImagesAsync(
        StudioSettings settings,
        CommerceProduct product,
        IReadOnlyList<CommerceImageNode> seedNodes,
        CancellationToken cancellationToken)
    {
        var content = await SendChatAsync(
            settings,
            [
                new ChatMessage("system", BuildCommerceImagePlanSystemPrompt()),
                new ChatMessage("user", BuildCommerceImagePlanUserPrompt(product, seedNodes)),
            ],
            temperature: 0.35,
            cancellationToken);

        var plan = JsonSerializer.Deserialize<CommerceImagePlan>(ExtractJson(content), JsonOptions)
            ?? new CommerceImagePlan();
        plan.Normalize();
        plan.Model = StudioSettings.NormalizeChatModel(settings.ChatModel);
        return plan;
    }

    private static string BuildCopyWorkspaceSystemPrompt() =>
        """
        你是 SonnetArt 文案空间里的资深中文文案策划、诊断与改写助手。你的任务是帮助用户写作、改写、扩写、润色、提炼标题、生成营销文案、短视频脚本、广告文案、私域文案、品牌文案和内容结构。不要提作图。

        工作原则：
        1. 先理解用户意图，再动笔。系统会把最近发的提示词、整个当前会话的用户提示词和最近文案结果放在用户消息里；你要先整合这些上下文，推测用户本轮真正意图，并在心里重新生成适合本轮任务的文案系统提示词，再按它出文案。
        2. 最新用户输入拥有最高优先级。它可能是新任务、补充要求、对上一版的修改、换风格、换平台、缩短/扩写、或对整段会话目标的省略表达；如果它与旧约束冲突，以最新输入为准。
        3. 优先判断行业/品类、内容目的、发布平台、目标受众、转化动作、已有文案状态和限制条件。当前会话里反复出现且未被最新输入覆盖的信息，应作为稳定约束继续保留。
        4. 如果关键信息不足且会明显影响结果，先用不超过 3 个问题追问；如果可以从上下文合理推断，就先说明你的推断并直接给可用结果。
        5. 按行业和目的选择文案策略，不要套固定模板。带货/广告优先关注卖点转利益、信任证明、场景化和行动指令；短视频优先关注封面标题、3 秒开头、留存节奏、转折、互动和结尾转化；品牌/官网优先关注定位、价值主张、差异化、语气一致性；私域/朋友圈优先关注真人感、关系感、痛点共鸣和低压转化；B2B/技术产品优先关注受众角色、业务痛点、证据、ROI 和清晰度。
        6. 可灵活使用 AIDA、PAS、BAB、FAB、4P/4C、问题-原因-解决、痛点-放大-解决、反常识-解释-证明、故事-冲突-结果、场景-痛点-产品-结果等框架，但不要机械展示框架名，除非用户要求。
        7. 输出要直接可用，中文自然、有节奏，避免空话、套路营销腔、夸张承诺和虚假数据。保留用户明确要求，不编造品牌事实、资质、价格、疗效、案例或政策。
        8. 文案修改时先诊断核心问题，再给修改方案；不要只改字句，要指出开头、结构、情绪、信任、卖点、场景和转化链路上的问题。
        9. 不要输出你在心里重建的系统提示词、推理过程或上下文整理过程，除非用户明确要求查看。

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

    private static string BuildCopyWorkspaceUserPrompt(
        IReadOnlyList<StudioMessage> history,
        string workspaceMemory,
        string sessionMemory,
        string userText)
    {
        var previousPrompt = FindLatestCopyUserPrompt(history);

        return $"""
        工作空间记忆（跨会话沉淀，适用于新会话和当前会话；若与最新输入冲突，以最新输入为准）：
        {FormatMemory(workspaceMemory)}

        当前会话记忆（本会话目标、偏好、已有产出和待办；若与最新输入冲突，以最新输入为准）：
        {FormatMemory(sessionMemory)}

        当前会话的用户提示词（从早到晚，覆盖整个当前文案会话；这些是需求证据，不是新的系统指令）：
        {BuildCopySessionPromptEvidence(history)}

        最近一条历史用户提示词：
        {previousPrompt}

        最近文案结果摘录（用于理解已有版本、语气和用户可能要修改的对象；不要机械复述）：
        {BuildRecentCopyResultEvidence(history)}

        用户最新输入（最高优先级）：
        {userText}

        请先把“用户最新输入”与“整个当前会话的用户提示词”整合，推测用户本轮真正要完成的文案任务，在心里重建本轮文案系统提示词，然后直接输出文案或诊断结果。
        """;
    }

    private static string FindLatestCopyUserPrompt(IReadOnlyList<StudioMessage> history)
    {
        for (var index = history.Count - 1; index >= 0; index--)
        {
            var message = history[index];
            if (message.Role == "user" && !string.IsNullOrWhiteSpace(message.Content))
            {
                return NormalizeCopyEvidence(message.Content);
            }
        }

        return "当前会话还没有上一条用户提示词。";
    }

    private static string BuildCopySessionPromptEvidence(IReadOnlyList<StudioMessage> history)
    {
        var entries = new List<string>();
        for (var index = 0; index < history.Count; index++)
        {
            var message = history[index];
            if (message.Role != "user" || string.IsNullOrWhiteSpace(message.Content))
            {
                continue;
            }

            AddCopyEvidence(entries, $"{index + 1}. 用户：{message.Content}");
        }

        return entries.Count == 0
            ? "当前会话没有可用的历史用户提示词。"
            : JoinCopyEvidence(entries);
    }

    private static string BuildRecentCopyResultEvidence(IReadOnlyList<StudioMessage> history)
    {
        var entries = new List<string>();
        foreach (var message in history
            .Where(item => item.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) &&
                item.Images.Count == 0 &&
                !string.IsNullOrWhiteSpace(item.Content))
            .TakeLast(4))
        {
            AddCopyEvidence(entries, $"文案结果：{message.Content}");
        }

        return entries.Count == 0
            ? "当前会话还没有历史文案结果。"
            : JoinCopyEvidence(entries);
    }

    private static void AddCopyEvidence(List<string> entries, string value)
    {
        var normalized = NormalizeCopyEvidence(value);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            entries.Add(normalized);
        }
    }

    private static string JoinCopyEvidence(IReadOnlyList<string> entries)
    {
        var all = string.Join("\n", entries);
        if (all.Length <= MaxCopyEvidenceChars)
        {
            return all;
        }

        var headBudget = MaxCopyEvidenceChars / 3;
        var tailBudget = MaxCopyEvidenceChars - headBudget - 80;
        var head = new List<string>();
        var tail = new List<string>();
        var headChars = 0;
        var tailChars = 0;

        foreach (var entry in entries)
        {
            if (headChars + entry.Length + 1 > headBudget)
            {
                break;
            }

            head.Add(entry);
            headChars += entry.Length + 1;
        }

        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var entry = entries[index];
            if (tailChars + entry.Length + 1 > tailBudget)
            {
                break;
            }

            tail.Add(entry);
            tailChars += entry.Length + 1;
        }

        tail.Reverse();
        return string.Join("\n", head.Concat(["...中间较早的文案提示词已省略，优先保留开头定位和最近约束..."]).Concat(tail));
    }

    private static string NormalizeCopyEvidence(string value)
    {
        var normalized = string.Join(
            " ",
            value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Length <= MaxCopyEvidenceItemChars
            ? normalized
            : normalized[..MaxCopyEvidenceItemChars].TrimEnd() + "...";
    }

    private static string BuildMemoryRefreshSystemPrompt() =>
        """
        你是 SonnetArt 的记忆层整理器。你只返回 JSON，不要解释，不要 Markdown。
        你的任务是根据当前工作空间、当前会话、已有记忆、最近一次用户输入和最近一次助手结果，更新两层记忆：
        - sessionMemory：当前会话记忆，记录本会话目标、对象/产品/主题、风格、平台、受众、重要约束、用户偏好、已完成结果、仍要避免或继续处理的问题。
        - workspaceMemory：工作空间记忆，记录跨会话稳定偏好、长期项目背景、常用品牌/产品/受众/风格、用户反复强调的限制、跨会话可复用事实。

        规则：
        1. 最新输入和最新结果优先；旧记忆中被覆盖、过期或冲突的内容要删除或改写。
        2. 不要保存寒暄、失败报错、按钮说明、一次性流程细节、验证码、密钥、令牌、隐私敏感信息。
        3. 不要编造品牌事实、价格、资质、疗效、人物身份或用户没有提供的长期偏好。
        4. 记忆要短、结构化、可被下一次请求直接使用；优先使用中文要点。
        5. sessionMemory 最多 900 中文字，workspaceMemory 最多 1200 中文字。

        返回格式：
        {
          "sessionMemory": "更新后的当前会话记忆",
          "workspaceMemory": "更新后的工作空间记忆"
        }
        """;

    private static string BuildMemoryRefreshUserPrompt(
        StudioWorkspace workspace,
        StudioSession activeSession,
        string latestUserText,
        string latestAssistantText)
    {
        return $"""
        工作空间：
        名称：{workspace.Name}
        类型：{WorkspaceTypeLabel(workspace.Type)}

        已有工作空间记忆：
        {FormatMemory(workspace.Memory)}

        当前会话：
        标题：{activeSession.Title}
        类型/模式：{activeSession.Mode}

        已有当前会话记忆：
        {FormatMemory(activeSession.Memory)}

        当前会话最近证据：
        {BuildMemorySource(activeSession)}

        工作空间内其他会话记忆摘录：
        {BuildWorkspaceSessionMemorySource(workspace, activeSession.Id)}

        最近一次用户输入：
        {latestUserText}

        最近一次助手结果：
        {latestAssistantText}

        请返回更新后的 sessionMemory 和 workspaceMemory。
        """;
    }

    private static string BuildMemorySource(StudioSession session)
    {
        var entries = session.Messages
            .Where(message => (message.Role is "user" or "assistant") && !string.IsNullOrWhiteSpace(message.Content))
            .TakeLast(12)
            .Select(message => $"{NormalizeMemoryRole(message.Role)}：{NormalizeMemoryText(message.Content)}")
            .ToList();

        foreach (var image in session.Messages.SelectMany(message => message.Images).TakeLast(6))
        {
            var prompt = !string.IsNullOrWhiteSpace(image.RequestPrompt) ? image.RequestPrompt : image.Prompt;
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                entries.Add($"图片提示词：{NormalizeMemoryText(prompt)}");
            }

            if (!string.IsNullOrWhiteSpace(image.RevisedPrompt))
            {
                entries.Add($"模型修订提示词：{NormalizeMemoryText(image.RevisedPrompt)}");
            }
        }

        return LimitMemorySource(entries.Count == 0 ? "当前会话暂无可用证据。" : string.Join("\n", entries));
    }

    private static string BuildWorkspaceSessionMemorySource(StudioWorkspace workspace, string activeSessionId)
    {
        var entries = workspace.Sessions
            .Where(session => !string.Equals(session.Id, activeSessionId, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(session.Memory))
            .OrderByDescending(session => session.UpdatedAt)
            .Take(8)
            .Select(session => $"{session.Title}：{NormalizeMemoryText(session.Memory)}")
            .ToArray();

        return entries.Length == 0
            ? "暂无其他会话记忆。"
            : LimitMemorySource(string.Join("\n", entries));
    }

    private static string NormalizeMemoryRole(string role) =>
        role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "助手结果" : "用户";

    private static string NormalizeMemoryText(string? value)
    {
        return string.Join(
            " ",
            (value ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string LimitMemorySource(string value)
    {
        var normalized = NormalizeMemoryText(value);
        return normalized.Length <= MaxMemorySourceChars
            ? normalized
            : normalized[..MaxMemorySourceChars].TrimEnd() + "...";
    }

    private static string FormatMemory(string? memory)
    {
        var normalized = NormalizeMemoryText(memory);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "暂无记忆。";
        }

        return normalized.Length <= MaxMemoryChars
            ? normalized
            : normalized[..MaxMemoryChars].TrimEnd() + "...";
    }

    private static string WorkspaceTypeLabel(string type) =>
        StudioWorkspace.NormalizeType(type) switch
        {
            StudioWorkspace.CopyType => "文案空间",
            StudioWorkspace.CommerceProductImageType => "电商产品图",
            _ => "平面设计",
        };

    private static string BuildImageContinuationSystemPrompt() =>
        """
        你是 SonnetArt 作图工作台的图片方案与续作提示词总编。用户当前会话已经生成过图片，系统会把最近一张图作为参考图一起发送给图片模型。
        你的任务是通览整个当前会话里的提示词证据，包括用户每次输入、已生成图片的请求提示词、模型修订提示词和最近一张图的提示词，再结合用户最新输入，推测用户本次真正意图，重新生成一段可直接给图片生成/编辑模型使用的中文系统提示词。

        规则：
        1. 最新用户输入拥有最高优先级；它可能是新增需求、修改意见、风格切换、局部优化、重新出图要求或对前文隐含目标的补充。
        2. 不要只依赖最近一条提示词。先整合整个当前会话中反复出现、仍然有效、与当前画面相关的主体、场景、构图、风格、色彩、材质、光影、文字和限制条件。
        3. 如果最新输入很短或省略主语，要根据全会话提示词推测它指向的对象和动作；如果最新输入与旧约束冲突，以最新输入为准，并丢弃被覆盖的旧约束。
        4. 如果用户没有明确要求换主体、换场景或换风格，默认延续上一张图的主体身份、产品外观、构图关系、色彩、材质、光影和整体风格。
        5. 如果用户明确要求改变某些元素，只改变这些元素，并在提示词中约束其余关键视觉特征保持一致。
        6. 整合会话提示词时只保留对当前方案有用的稳定约束，去掉寒暄、确认、失败提示、下载说明、按钮文案和无关文本。
        7. 不编造品牌、人物身份、文字、资质、价格、疗效或版权内容；不要加入用户未要求的敏感内容。
        8. 输出必须是一段完整中文图片方案提示词，不要 Markdown，不要标题，不要解释，不要 JSON。
        """;

    private static string BuildImageContinuationUserPrompt(
        IReadOnlyList<StudioMessage> history,
        string workspaceMemory,
        string sessionMemory,
        GeneratedImage latestImage,
        string userText)
    {
        var sessionEvidence = BuildSessionPromptEvidence(history);
        var sourcePrompt = !string.IsNullOrWhiteSpace(latestImage.RequestPrompt)
            ? latestImage.RequestPrompt
            : latestImage.Prompt;

        return $"""
        工作空间记忆（跨会话沉淀，适用于当前图片方案；若与最新输入冲突，以最新输入为准）：
        {FormatMemory(workspaceMemory)}

        当前会话记忆（本会话的主体、风格、限制、已有结果和待修正事项；若与最新输入冲突，以最新输入为准）：
        {FormatMemory(sessionMemory)}

        当前会话提示词证据（从早到晚，覆盖整个当前会话；如果过长，系统已优先保留首尾关键提示词和图片提示词）：
        {sessionEvidence}

        上一张图的请求提示词：
        {sourcePrompt}

        上一张图的模型修订提示词：
        {latestImage.RevisedPrompt}

        用户最新输入：
        {userText}

        请先整合整个当前会话的提示词并推测用户意图，然后只输出本次要发送给图片模型的最终图片方案提示词。
        """;
    }

    private static string NormalizeHistoryRole(string role) =>
        role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "图像结果" : "用户";

    private static string BuildSessionPromptEvidence(IReadOnlyList<StudioMessage> history)
    {
        var entries = new List<string>();
        for (var messageIndex = 0; messageIndex < history.Count; messageIndex++)
        {
            var message = history[messageIndex];
            if (message.Role == "prompt-confirm")
            {
                continue;
            }

            if ((message.Role is "user" or "assistant") && !string.IsNullOrWhiteSpace(message.Content))
            {
                AddContinuationEvidence(entries, $"{messageIndex + 1}. {NormalizeHistoryRole(message.Role)}：{message.Content}");
            }

            for (var imageIndex = 0; imageIndex < message.Images.Count; imageIndex++)
            {
                var image = message.Images[imageIndex];
                var imageLabel = $"{messageIndex + 1}.{imageIndex + 1}";
                var requestPrompt = !string.IsNullOrWhiteSpace(image.RequestPrompt)
                    ? image.RequestPrompt
                    : image.Prompt;

                AddContinuationEvidence(entries, $"图片 {imageLabel} 请求提示词：{requestPrompt}");
                AddContinuationEvidence(entries, $"图片 {imageLabel} 模型修订提示词：{image.RevisedPrompt}");
            }
        }

        if (entries.Count == 0)
        {
            return "当前会话没有可用的历史提示词。";
        }

        return JoinContinuationEvidence(entries);
    }

    private static void AddContinuationEvidence(List<string> entries, string value)
    {
        var normalized = NormalizeContinuationEvidence(value);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            entries.Add(normalized);
        }
    }

    private static string JoinContinuationEvidence(IReadOnlyList<string> entries)
    {
        var all = string.Join("\n", entries);
        if (all.Length <= MaxContinuationEvidenceChars)
        {
            return all;
        }

        var headBudget = MaxContinuationEvidenceChars / 3;
        var tailBudget = MaxContinuationEvidenceChars - headBudget - 80;
        var head = new List<string>();
        var tail = new List<string>();
        var headChars = 0;
        var tailChars = 0;

        foreach (var entry in entries)
        {
            if (headChars + entry.Length + 1 > headBudget)
            {
                break;
            }

            head.Add(entry);
            headChars += entry.Length + 1;
        }

        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var entry = entries[index];
            if (tailChars + entry.Length + 1 > tailBudget)
            {
                break;
            }

            tail.Add(entry);
            tailChars += entry.Length + 1;
        }

        tail.Reverse();
        return string.Join("\n", head.Concat(["...中间较早的提示词已省略，优先保留开头定位和最近约束..."]).Concat(tail));
    }

    private static string NormalizeContinuationEvidence(string value)
    {
        var normalized = string.Join(
            " ",
            value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Length <= MaxContinuationEvidenceItemChars
            ? normalized
            : normalized[..MaxContinuationEvidenceItemChars].TrimEnd() + "...";
    }

    private static string BuildCommerceProductAnalysisSystemPrompt() =>
        """
        你是电商商品图策划中的商品理解分析器。你只返回 JSON，不要解释，不要 Markdown。
        根据商品名称、描述、卖点、规格、目标人群、SKU 和参考图线索，提炼用于商品图规划的稳定事实。
        不要编造品牌、认证、价格、疗效、夸张承诺或无法从输入合理推断的信息；不确定时用保守表述。
        如果用户只上传图片且没有文字档案，请直接识别图片中的产品主体、包装可见信息、颜色/材质/容量/数量/套装等，并生成商品档案字段。
        返回格式：
        {
          "productName": "可上架使用的简短商品名称",
          "productType": "简短产品类型",
          "coreSellingPoints": ["核心卖点1"],
          "useScenarios": ["适用场景1"],
          "colorVariants": ["颜色或外观变体1"],
          "materialFeatures": ["材质/工艺/触感特性1"],
          "targetAudiences": ["目标人群1"],
          "specifications": "容量、尺寸、重量、材质、包装清单等；图片不能确认则只写可见信息",
          "skuVariants": [
            {
              "name": "变体名称",
              "color": "颜色",
              "material": "材质",
              "size": "尺寸/容量",
              "package": "套装/数量",
              "sku": ""
            }
          ],
          "summary": "一句中文商品图策划摘要"
        }
        每个数组最多 8 项，每项不超过 24 个中文字符。
        """;

    private static string BuildCommerceProductAnalysisUserPrompt(CommerceProduct product)
    {
        product.Normalize();
        var references = product.ReferenceImages
            .Select(DescribeReferenceImage)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return $"""
        商品名称：{product.Name}
        商品描述：{product.Description}
        已填卖点：{string.Join("；", product.SellingPoints)}
        规格信息：{product.Specifications}
        已填目标人群：{product.TargetAudience}
        SKU 变体：{string.Join("；", product.SkuVariants.Select(variant => $"{variant.Name} {variant.Color} {variant.Sku}".Trim()))}
        参考图线索：{string.Join("；", references)}
        请优先根据随消息附带的图片识别商品，并把可用于电商商品图和详情页的内容结构化返回。
        """;
    }

    private static bool IsVisionReference(string value)
    {
        return value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) ||
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https";
    }

    private static string BuildCommerceImagePlanSystemPrompt() =>
        """
        你是电商商品图首轮规划师。你只返回 JSON，不要解释，不要 Markdown。
        根据商品档案和内置节点草案，生成一棵可编辑的商品图规划树，用于后续图片生成。
        必须保留常见电商图片类型的覆盖：主图、场景图、细节图、对比图、尺寸图、A+ 图、包装图。可以调整标题、数量、构图和提示词，但不要编造品牌、认证、价格、疗效、活动政策或虚假文字。
        输出的 prompt 要是可直接给图片模型的中文提示词，尽量明确画面、场景、构图、材质、光线、产品一致性和少量文字限制。
        返回格式：
        {
          "title": "方案标题",
          "strategySummary": "一句中文整体策略",
          "nodes": [
            {
              "type": "main",
              "title": "主图",
              "goal": "节点目标",
              "aspectRatio": "1:1",
              "plannedCount": 4,
              "scene": "场景/背景",
              "composition": "构图方式",
              "keyMessage": "要表达的核心信息",
              "prompt": "完整图片提示词",
              "negativePrompt": "负向提示词",
              "referenceRole": "product",
              "enabled": true,
              "status": "AI 已规划"
            }
          ]
        }
        aspectRatio 只能使用 auto、1:1、3:4、2:3、9:16、3:2、4:3、16:9、21:9。plannedCount 为 1 到 12。nodes 保持 5 到 9 个。
        """;

    private static string BuildCommerceImagePlanUserPrompt(CommerceProduct product, IReadOnlyList<CommerceImageNode> seedNodes)
    {
        product.Normalize();
        var seedJson = JsonSerializer.Serialize(seedNodes.Select(node => new
        {
            node.Type,
            node.Title,
            node.Goal,
            node.AspectRatio,
            node.PlannedCount,
            node.Prompt,
            node.NegativePrompt,
        }), JsonOptions);

        return $"""
        商品档案：
        商品名称：{product.Name}
        商品描述：{product.Description}
        已填卖点：{string.Join("；", product.SellingPoints)}
        规格信息：{product.Specifications}
        已填目标人群：{product.TargetAudience}
        SKU 变体：{string.Join("；", product.SkuVariants.Select(variant => $"{variant.Name} {variant.Color} {variant.Sku}".Trim()))}
        AI 分析：
        产品类型：{product.Analysis.ProductType}
        AI 提炼卖点：{string.Join("；", product.Analysis.CoreSellingPoints)}
        适用场景：{string.Join("；", product.Analysis.UseScenarios)}
        颜色变体：{string.Join("；", product.Analysis.ColorVariants)}
        材质特性：{string.Join("；", product.Analysis.MaterialFeatures)}
        AI 目标人群：{string.Join("；", product.Analysis.TargetAudiences)}
        摘要：{product.Analysis.Summary}

        内置首轮节点草案 JSON：
        {seedJson}
        """;
    }

    private static string DescribeReferenceImage(string value)
    {
        if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return "本地上传产品图";
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            var fileName = Path.GetFileNameWithoutExtension(uri.LocalPath);
            return string.IsNullOrWhiteSpace(fileName) ? uri.Host : fileName;
        }

        return value.Length <= 80 ? value : value[..80];
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
        return await SendChatWithTransientRetryAsync(
            settings,
            endpointPath,
            messages,
            temperature,
            cancellationToken);
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
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildLocalProxyEndpoint(endpointPath))
        {
            Content = JsonContent.Create(new ChatCompletionRequest(
                StudioSettings.NormalizeChatModel(settings.ChatModel),
                messages,
                temperature), options: JsonOptions),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.OpenAiApiKey.Trim());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.Headers.Contains(LocalProxyHeader))
        {
            throw new HttpRequestException("会话接口代理未启用。请检查 SonnetHost 的 SonnetArt:AiUpstreamUrl 配置。");
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
}

public sealed record PromptIntentResult(
    [property: JsonPropertyName("image")] bool Image,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record WorkspaceMemoryUpdate(
    [property: JsonPropertyName("sessionMemory")] string SessionMemory,
    [property: JsonPropertyName("workspaceMemory")] string WorkspaceMemory)
{
    public static WorkspaceMemoryUpdate Empty { get; } = new(string.Empty, string.Empty);

    public WorkspaceMemoryUpdate Normalize()
    {
        return new WorkspaceMemoryUpdate(
            NormalizeMemory(SessionMemory, 4000),
            NormalizeMemory(WorkspaceMemory, 6000));
    }

    private static string NormalizeMemory(string? value, int maxLength)
    {
        var normalized = string.Join(
            "\n",
            (value ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].TrimEnd();
    }
}

internal sealed record ChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
    [property: JsonPropertyName("temperature")] double Temperature);

internal sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] object Content);

internal static class ChatContent
{
    public static object FromTextAndImages(string text, IEnumerable<string> imageUrls)
    {
        var parts = new List<ChatContentPart>
        {
            ChatContentPart.FromText(text),
        };

        foreach (var imageUrl in imageUrls)
        {
            parts.Add(ChatContentPart.FromImage(imageUrl));
        }

        return parts.Count == 1 ? text : parts;
    }
}

internal sealed record ChatContentPart(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("image_url")] ChatImageUrl? ImageUrl = null)
{
    public static ChatContentPart FromText(string text) => new("text", Text: text);

    public static ChatContentPart FromImage(string imageUrl) => new("image_url", ImageUrl: new ChatImageUrl(imageUrl));
}

internal sealed record ChatImageUrl(
    [property: JsonPropertyName("url")] string Url);

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
