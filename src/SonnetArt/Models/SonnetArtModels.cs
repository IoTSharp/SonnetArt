using System.Text.Json.Serialization;

namespace SonnetArt.Models;

public sealed class StudioSettings
{
    public const string DefaultBaseUrl = "/";

    private string? _legacyBaseUrl;
    private string? _legacyImageApiKey;

    public string BaseUrl { get; set; } = DefaultBaseUrl;
    public string ImageApiKey { get; set; } = string.Empty;

    [JsonPropertyName("apiKey")]
    public string? LegacyImageApiKey
    {
        get => null;
        set => _legacyImageApiKey = value;
    }

    [JsonPropertyName("sonnetBaseUrl")]
    public string? LegacyBaseUrl
    {
        get => null;
        set => _legacyBaseUrl = value;
    }

    public string SonnetAccessToken { get; set; } = string.Empty;
    public string SonnetRefreshToken { get; set; } = string.Empty;
    public DateTimeOffset? SonnetTokenExpiresAt { get; set; }
    public SonnetUser? SonnetUser { get; set; }
    public long? SonnetApiKeyId { get; set; }
    public string SonnetApiKeyName { get; set; } = "SonnetArt Image";
    public long? SonnetGroupId { get; set; }
    public string SonnetGroupName { get; set; } = "SonnetArt Image";
    public string OpenAiApiKey { get; set; } = string.Empty;
    public long? SonnetOpenAiApiKeyId { get; set; }
    public string SonnetOpenAiApiKeyName { get; set; } = "SonnetArt OpenAI";
    public long? SonnetOpenAiGroupId { get; set; }
    public string SonnetOpenAiGroupName { get; set; } = "OpenAi";
    public string SonnetPaymentType { get; set; } = "alipay";
    public long? EmbeddedUserId { get; set; }
    public string EmbeddedUiMode { get; set; } = string.Empty;
    public string EmbeddedLanguage { get; set; } = string.Empty;
    public string EmbeddedSourceHost { get; set; } = string.Empty;
    public string EmbeddedSourceUrl { get; set; } = string.Empty;
    public string ThemeMode { get; set; } = "system";
    public string ChatModel { get; set; } = "gpt-5.5";
    public string PromptPolishMode { get; set; } = "direct";
    public string Model { get; set; } = "gpt-image-2";
    public string Size { get; set; } = "auto";
    public string AspectRatio { get; set; } = "auto";
    public string ResolutionTier { get; set; } = "2k";
    public string Quality { get; set; } = "auto";
    public string Style { get; set; } = "默认";
    public string Background { get; set; } = "auto";
    public string Format { get; set; } = "png";
    public int Compression { get; set; } = 100;
    public string Moderation { get; set; } = "auto";
    public string InputFidelity { get; set; } = "默认";
    public string ResponseFormat { get; set; } = "b64_json";
    public string RequestMode { get; set; } = "sync";
    public int PartialImages { get; set; }
    public string User { get; set; } = string.Empty;
    public string AdvancedJson { get; set; } = string.Empty;

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(ImageApiKey) && !string.IsNullOrWhiteSpace(_legacyImageApiKey))
        {
            ImageApiKey = _legacyImageApiKey.Trim();
        }

        BaseUrl = DefaultBaseUrl;

        ThemeMode = NormalizeThemeMode(ThemeMode);
        EmbeddedUiMode = NormalizeEmbeddedUiMode(EmbeddedUiMode);
        EmbeddedLanguage = NormalizeEmbeddedLanguage(EmbeddedLanguage);
        EmbeddedSourceHost = EmbeddedSourceHost?.Trim() ?? string.Empty;
        EmbeddedSourceUrl = EmbeddedSourceUrl?.Trim() ?? string.Empty;
        ChatModel = NormalizeChatModel(ChatModel);
        PromptPolishMode = NormalizePromptPolishMode(PromptPolishMode);
        AspectRatio = NormalizeAspectRatio(AspectRatio);
        ResolutionTier = NormalizeResolutionTier(ResolutionTier);

        _legacyImageApiKey = null;
        _legacyBaseUrl = null;
    }

    public static string NormalizeThemeMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "light" => "light",
            "dark" => "dark",
            _ => "system",
        };
    }

    public static string NormalizeEmbeddedUiMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "embedded" => "embedded",
            _ => string.Empty,
        };
    }

    public static string NormalizeEmbeddedLanguage(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return normalized.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? "en"
            : "zh";
    }

    public static string NormalizePromptPolishMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "ask" => "ask",
            "auto" => "auto",
            _ => "direct",
        };
    }

    public static string NormalizeChatModel(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "gpt-5.5";
        }

        if (TryParseGptVersion(normalized, out var major, out var minor) &&
            (major > 5 || major == 5 && minor >= 5))
        {
            return $"gpt-{major}.{minor}";
        }

        return "gpt-5.5";
    }

    private static bool TryParseGptVersion(string value, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        var lower = value.Trim().ToLowerInvariant();
        if (!lower.StartsWith("gpt", StringComparison.Ordinal))
        {
            return false;
        }

        var versionStart = 3;
        while (versionStart < lower.Length && (lower[versionStart] == '-' || lower[versionStart] == ' '))
        {
            versionStart++;
        }

        var majorEnd = versionStart;
        while (majorEnd < lower.Length && char.IsDigit(lower[majorEnd]))
        {
            majorEnd++;
        }

        if (majorEnd == versionStart ||
            !int.TryParse(lower.AsSpan(versionStart, majorEnd - versionStart), out major))
        {
            return false;
        }

        if (majorEnd >= lower.Length || lower[majorEnd] != '.')
        {
            return true;
        }

        var minorStart = majorEnd + 1;
        var minorEnd = minorStart;
        while (minorEnd < lower.Length && char.IsDigit(lower[minorEnd]))
        {
            minorEnd++;
        }

        return minorEnd > minorStart &&
            int.TryParse(lower.AsSpan(minorStart, minorEnd - minorStart), out minor);
    }

    public static string NormalizeAspectRatio(string? value)
    {
        return value?.Trim() switch
        {
            "1:1" => "1:1",
            "3:4" => "3:4",
            "2:3" => "2:3",
            "9:16" => "9:16",
            "3:2" => "3:2",
            "4:3" => "4:3",
            "16:9" => "16:9",
            "21:9" => "21:9",
            _ => "auto",
        };
    }

    public static string NormalizeResolutionTier(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "1k" => "1k",
            "2k" => "2k",
            "4k" => "4k",
            "8mp" or "8k" => "8mp",
            _ => "2k",
        };
    }

}

public sealed class StudioSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "新建作图";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string Mode { get; set; } = "generate";
    public string Prompt { get; set; } = string.Empty;
    public string ImageReferences { get; set; } = string.Empty;
    public string ReferenceRole { get; set; } = "auto";
    public string MaskReference { get; set; } = string.Empty;
    public List<StudioMessage> Messages { get; set; } = [];
}

public sealed class StudioMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public List<GeneratedImage> Images { get; set; } = [];
}

public sealed class GeneratedImage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = string.Empty;
    public string? RevisedPrompt { get; set; }
    public string? MimeType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Status { get; set; } = "generated";
    public bool IsFavorite { get; set; }
    public List<string> Tags { get; set; } = [];
    public string Prompt { get; set; } = string.Empty;
    public string RequestPrompt { get; set; } = string.Empty;
    public string Mode { get; set; } = "generate";
    public string Model { get; set; } = "gpt-image-2";
    public string Size { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = string.Empty;
    public string ResolutionTier { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string OutputFormat { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public string Moderation { get; set; } = string.Empty;
    public string RequestMode { get; set; } = "sync";
    public int RequestCount { get; set; } = 1;
    public int BatchIndex { get; set; } = 1;
    public int ReferenceCount { get; set; }
    public string ReferenceRole { get; set; } = "auto";
    public bool HasMask { get; set; }
    public long DurationMs { get; set; }
    public string EstimatedCost { get; set; } = string.Empty;
    public string EstimatedDuration { get; set; } = string.Empty;
    public string RequestSummary { get; set; } = string.Empty;
}

public sealed class StudioWorkspace
{
    public const string GraphicType = "graphic";
    public const string CopyType = "copy";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = GraphicType;
    public string Name { get; set; } = "平面设计";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset LastOpenedAt { get; set; } = DateTimeOffset.Now;
    public List<StudioSession> Sessions { get; set; } = [];
    public string? ActiveSessionId { get; set; }

    public static StudioWorkspace Create(string? name = null, string? type = null)
    {
        var now = DateTimeOffset.Now;
        var normalizedType = NormalizeType(type);
        var workspace = new StudioWorkspace
        {
            Type = normalizedType,
            Name = string.IsNullOrWhiteSpace(name) ? DefaultName(normalizedType) : name.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            LastOpenedAt = now,
        };
        workspace.Normalize();
        return workspace;
    }

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            Id = Guid.NewGuid().ToString("N");
        }

        Type = NormalizeType(Type);
        Name = string.IsNullOrWhiteSpace(Name) ? DefaultName(Type) : Name.Trim();
        Sessions ??= [];

        if (Sessions.Count == 0)
        {
            var session = CreateDefaultSession(Type);
            Sessions.Add(session);
            ActiveSessionId = session.Id;
        }

        if (string.IsNullOrWhiteSpace(ActiveSessionId) ||
            Sessions.All(session => session.Id != ActiveSessionId))
        {
            ActiveSessionId = Sessions
                .OrderByDescending(session => session.UpdatedAt)
                .First()
                .Id;
        }
    }

    public static string NormalizeType(string? type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            CopyType => CopyType,
            _ => GraphicType,
        };
    }

    public static string DefaultName(string? type)
    {
        return NormalizeType(type) == CopyType ? "文案空间" : "平面设计";
    }

    public static StudioSession CreateDefaultSession(string? type)
    {
        return new StudioSession
        {
            Title = NormalizeType(type) == CopyType ? "新建文案" : "新建作图",
        };
    }
}

public sealed record WorkspaceSidebarItem(
    string Id,
    string Title,
    string Description,
    string Icon,
    string Type);

public sealed class StudioSnapshot
{
    public StudioSettings Settings { get; set; } = new();

    [JsonIgnore]
    public List<StudioSession> Sessions { get; set; } = [];

    [JsonIgnore]
    public string? ActiveSessionId { get; set; }

    public List<StudioWorkspace> Workspaces { get; set; } = [];
    public string? ActiveWorkspaceId { get; set; }

    [JsonPropertyName("sessions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<StudioSession>? LegacySessions
    {
        get => Workspaces.Count == 0 ? Sessions : null;
        set => Sessions = value ?? [];
    }

    [JsonPropertyName("activeSessionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyActiveSessionId
    {
        get => Workspaces.Count == 0 ? ActiveSessionId : null;
        set => ActiveSessionId = value;
    }

    public void Normalize()
    {
        Settings ??= new StudioSettings();
        Settings.Normalize();
        Sessions ??= [];
        Workspaces ??= [];

        if (Workspaces.Count == 0)
        {
            MigrateLegacyWorkspaces();
        }

        foreach (var workspace in Workspaces)
        {
            workspace.Normalize();
        }

        NormalizeImageHistory(Workspaces.SelectMany(workspace => workspace.Sessions));

        if (string.IsNullOrWhiteSpace(ActiveWorkspaceId) ||
            Workspaces.All(workspace => workspace.Id != ActiveWorkspaceId))
        {
            ActiveWorkspaceId = Workspaces
                .OrderByDescending(workspace => workspace.LastOpenedAt)
                .First()
                .Id;
        }

        SyncLegacyFieldsFromWorkspaces();
    }

    public StudioWorkspace GetActiveWorkspace()
    {
        Normalize();
        return Workspaces.First(workspace => workspace.Id == ActiveWorkspaceId);
    }

    public StudioWorkspace? SetActiveWorkspace(string? workspaceId)
    {
        Normalize();
        var workspace = Workspaces.FirstOrDefault(item => item.Id == workspaceId);
        if (workspace is null)
        {
            return null;
        }

        var now = DateTimeOffset.Now;
        workspace.LastOpenedAt = now;
        workspace.UpdatedAt = now;
        ActiveWorkspaceId = workspace.Id;
        SyncLegacyFieldsFromWorkspaces();
        return workspace;
    }

    public StudioWorkspace AddWorkspace(string? name = null, string? type = null)
    {
        Normalize();
        var workspace = StudioWorkspace.Create(name, type);
        Workspaces.Add(workspace);
        ActiveWorkspaceId = workspace.Id;
        SyncLegacyFieldsFromWorkspaces();
        return workspace;
    }

    private void MigrateLegacyWorkspaces()
    {
        var graphic = StudioWorkspace.Create("平面设计");
        graphic.Sessions = Sessions.Count == 0 ? [new StudioSession()] : Sessions;
        graphic.ActiveSessionId = ActiveSessionId;
        graphic.Normalize();

        if (graphic.Sessions.Count > 0)
        {
            graphic.CreatedAt = graphic.Sessions.Min(session => session.CreatedAt);
            graphic.UpdatedAt = graphic.Sessions.Max(session => session.UpdatedAt);
            graphic.LastOpenedAt = graphic.UpdatedAt;
        }

        Workspaces = [graphic];
        ActiveWorkspaceId = graphic.Id;
    }

    private void SyncLegacyFieldsFromWorkspaces()
    {
        var active = Workspaces.FirstOrDefault(workspace => workspace.Id == ActiveWorkspaceId)
            ?? Workspaces.OrderByDescending(workspace => workspace.LastOpenedAt).FirstOrDefault();
        if (active is null)
        {
            return;
        }

        Sessions = active.Sessions;
        ActiveSessionId = active.ActiveSessionId;
    }

    private static void NormalizeImageHistory(IEnumerable<StudioSession> sessions)
    {
        foreach (var session in sessions)
        {
            session.ReferenceRole = NormalizeReferenceRole(session.ReferenceRole);
            session.Messages ??= [];
            foreach (var message in session.Messages)
            {
                message.Images ??= [];
                foreach (var image in message.Images)
                {
                    NormalizeImage(image, message.CreatedAt, session.Mode);
                }
            }
        }
    }

    private static void NormalizeImage(GeneratedImage image, DateTimeOffset fallbackCreatedAt, string? fallbackMode)
    {
        if (string.IsNullOrWhiteSpace(image.Id))
        {
            image.Id = Guid.NewGuid().ToString("N");
        }

        if (image.CreatedAt == default)
        {
            image.CreatedAt = fallbackCreatedAt == default ? DateTimeOffset.Now : fallbackCreatedAt;
        }

        image.Status = string.IsNullOrWhiteSpace(image.Status) ? "generated" : image.Status.Trim();
        image.Tags = image.Tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList() ?? [];
        image.Mode = NormalizeImageMode(string.IsNullOrWhiteSpace(image.Mode) ? fallbackMode : image.Mode);
        image.Model = NormalizeImageFamilyModel(image.Model);
        image.ReferenceRole = NormalizeReferenceRole(image.ReferenceRole);
        image.RequestMode = string.Equals(image.RequestMode, "stream", StringComparison.OrdinalIgnoreCase)
            ? "stream"
            : "sync";
        image.RequestCount = Math.Clamp(image.RequestCount, 1, 8);
        image.BatchIndex = Math.Clamp(image.BatchIndex, 1, Math.Max(1, image.RequestCount));
        image.ReferenceCount = Math.Max(0, image.ReferenceCount);
        image.Prompt = image.Prompt?.Trim() ?? string.Empty;
        image.RequestPrompt = image.RequestPrompt?.Trim() ?? string.Empty;
        image.Size = image.Size?.Trim() ?? string.Empty;
        image.AspectRatio = StudioSettings.NormalizeAspectRatio(image.AspectRatio);
        image.ResolutionTier = StudioSettings.NormalizeResolutionTier(image.ResolutionTier);
        image.Quality = string.IsNullOrWhiteSpace(image.Quality) ? "auto" : image.Quality.Trim();
        image.OutputFormat = string.IsNullOrWhiteSpace(image.OutputFormat) ? string.Empty : image.OutputFormat.Trim().ToLowerInvariant();
        image.Background = string.IsNullOrWhiteSpace(image.Background) ? string.Empty : image.Background.Trim();
        image.Moderation = string.IsNullOrWhiteSpace(image.Moderation) ? string.Empty : image.Moderation.Trim();
        image.EstimatedCost = image.EstimatedCost?.Trim() ?? string.Empty;
        image.EstimatedDuration = image.EstimatedDuration?.Trim() ?? string.Empty;
        image.RequestSummary = image.RequestSummary?.Trim() ?? string.Empty;
    }

    public static string NormalizeReferenceRole(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "content" => "content",
            "style" => "style",
            "character" => "character",
            "product" => "product",
            _ => "auto",
        };
    }

    private static string NormalizeImageMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "image" => "image",
            "edit" => "edit",
            "variation" => "variation",
            _ => "generate",
        };
    }

    private static string NormalizeImageFamilyModel(string? value)
    {
        var model = value?.Trim();
        return !string.IsNullOrWhiteSpace(model) &&
            (model.Equals("gpt-image-2", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("gpt-image-2-", StringComparison.OrdinalIgnoreCase))
                ? model
                : "gpt-image-2";
    }
}

public sealed record StudioImageRequest(
    StudioSettings Settings,
    string Prompt,
    IReadOnlyList<string> ImageReferences,
    string? MaskReference,
    IReadOnlyList<ImageReferenceFile> ReferenceFiles,
    int Count,
    string Mode);

public sealed record ImageReferenceFile(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record StudioImageResult(
    IReadOnlyList<GeneratedImage> Images,
    string RawJson);

internal sealed class ImageGenerationRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = "gpt-image-2";
    [JsonPropertyName("prompt")] public string Prompt { get; set; } = string.Empty;
    [JsonPropertyName("n")] public int Count { get; set; } = 1;
    [JsonPropertyName("size")] public string? Size { get; set; }
    [JsonPropertyName("quality")] public string? Quality { get; set; }
    [JsonPropertyName("style")] public string? Style { get; set; }
    [JsonPropertyName("background")] public string? Background { get; set; }
    [JsonPropertyName("output_format")] public string? OutputFormat { get; set; }
    [JsonPropertyName("output_compression")] public int? OutputCompression { get; set; }
    [JsonPropertyName("moderation")] public string? Moderation { get; set; }
    [JsonPropertyName("input_fidelity")] public string? InputFidelity { get; set; }
    [JsonPropertyName("response_format")] public string? ResponseFormat { get; set; }
    [JsonPropertyName("stream")] public bool? Stream { get; set; }
    [JsonPropertyName("partial_images")] public int? PartialImages { get; set; }
    [JsonPropertyName("user")] public string? User { get; set; }
    [JsonPropertyName("images")] public List<ImageReferencePayload>? Images { get; set; }
    [JsonPropertyName("image")] public ImageReferencePayload? Image { get; set; }
    [JsonPropertyName("mask")] public ImageReferencePayload? Mask { get; set; }
    [JsonExtensionData] public Dictionary<string, object?> ExtensionData { get; set; } = new();
}

internal sealed class ImageReferencePayload
{
    [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
    [JsonPropertyName("file_id")] public string? FileId { get; set; }
}

internal sealed class ImageGenerationResponse
{
    [JsonPropertyName("data")] public List<ImageGenerationData>? Data { get; set; }
    [JsonPropertyName("output")] public List<ImageGenerationOutput>? Output { get; set; }
}

internal sealed class ImageGenerationData
{
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("b64_json")] public string? Base64Json { get; set; }
    [JsonPropertyName("revised_prompt")] public string? RevisedPrompt { get; set; }
    [JsonPropertyName("mime_type")] public string? MimeType { get; set; }
}

internal sealed class ImageGenerationOutput
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("result")] public string? Result { get; set; }
    [JsonPropertyName("b64_json")] public string? Base64Json { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("mime_type")] public string? MimeType { get; set; }
    [JsonPropertyName("revised_prompt")] public string? RevisedPrompt { get; set; }
}
