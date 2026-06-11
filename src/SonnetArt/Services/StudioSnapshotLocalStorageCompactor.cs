using SonnetArt.Models;

namespace SonnetArt.Services;

internal static class StudioSnapshotLocalStorageCompactor
{
    private const int MaxCompactSessions = 15;
    private const int MaxCompactWorkspaces = 20;
    private const int MaxCompactActiveMessages = 80;
    private const int MaxCompactInactiveMessages = 20;
    private const int MaxMinimalMessages = 20;
    private const int MaxTitleChars = 160;
    private const int MaxPromptChars = 32_000;
    private const int MaxMessageChars = 8_000;
    private const int MaxMinimalMessageChars = 2_000;
    private const int MaxReferenceChars = 4_096;
    private const int MaxAdvancedJsonChars = 64_000;

    public static StudioSnapshot CreateCompactSnapshot(StudioSnapshot snapshot)
    {
        var compact = new StudioSnapshot
        {
            Settings = CopySettings(snapshot.Settings),
            ActiveWorkspaceId = snapshot.ActiveWorkspaceId,
            Workspaces = CopyWorkspaces(snapshot.Workspaces, snapshot.ActiveWorkspaceId, minimal: false),
        };
        compact.Normalize();
        return compact;
    }

    public static StudioSnapshot CreateMinimalSnapshot(StudioSnapshot snapshot)
    {
        var activeWorkspace = snapshot.Workspaces.FirstOrDefault(workspace =>
                string.Equals(workspace.Id, snapshot.ActiveWorkspaceId, StringComparison.Ordinal))
            ?? snapshot.Workspaces.OrderByDescending(workspace => workspace.LastOpenedAt).FirstOrDefault()
            ?? StudioWorkspace.Create();

        var minimalWorkspace = CopyWorkspace(activeWorkspace, minimal: true);
        var minimal = new StudioSnapshot
        {
            Settings = CopySettings(snapshot.Settings),
            ActiveWorkspaceId = minimalWorkspace.Id,
            Workspaces = [minimalWorkspace],
        };
        minimal.Normalize();
        return minimal;
    }

    private static List<StudioWorkspace> CopyWorkspaces(
        IReadOnlyList<StudioWorkspace> workspaces,
        string? activeWorkspaceId,
        bool minimal)
    {
        return workspaces
            .OrderByDescending(workspace => string.Equals(workspace.Id, activeWorkspaceId, StringComparison.Ordinal))
            .ThenByDescending(workspace => workspace.LastOpenedAt)
            .Take(minimal ? 1 : MaxCompactWorkspaces)
            .Select(workspace => CopyWorkspace(workspace, minimal))
            .ToList();
    }

    private static StudioWorkspace CopyWorkspace(StudioWorkspace workspace, bool minimal)
    {
        var maxMessages = minimal ? MaxMinimalMessages : MaxCompactInactiveMessages;
        var maxChars = minimal ? MaxMinimalMessageChars : MaxMessageChars;
        var sessions = workspace.Sessions
            .OrderByDescending(session => string.Equals(session.Id, workspace.ActiveSessionId, StringComparison.Ordinal))
            .ThenByDescending(session => session.UpdatedAt)
            .Take(minimal ? 1 : MaxCompactSessions)
            .Select(session =>
            {
                var isActive = string.Equals(session.Id, workspace.ActiveSessionId, StringComparison.Ordinal);
                return CopySession(
                    session,
                    isActive && !minimal ? MaxCompactActiveMessages : maxMessages,
                    maxChars,
                    keepSmallImageUrls: !minimal);
            })
            .ToList();

        return new StudioWorkspace
        {
            Id = workspace.Id,
            Name = LimitText(workspace.Name, MaxTitleChars),
            CreatedAt = workspace.CreatedAt,
            UpdatedAt = workspace.UpdatedAt,
            LastOpenedAt = workspace.LastOpenedAt,
            Sessions = sessions,
            ActiveSessionId = sessions.FirstOrDefault(session =>
                string.Equals(session.Id, workspace.ActiveSessionId, StringComparison.Ordinal))?.Id ?? sessions.FirstOrDefault()?.Id,
        };
    }

    private static StudioSession CopySession(
        StudioSession session,
        int maxMessages,
        int maxMessageChars,
        bool keepSmallImageUrls)
    {
        return new StudioSession
        {
            Id = session.Id,
            Title = LimitText(session.Title, MaxTitleChars),
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
            Mode = session.Mode,
            Prompt = LimitText(session.Prompt, MaxPromptChars),
            ImageReferences = LimitText(session.ImageReferences, MaxReferenceChars),
            ReferenceRole = session.ReferenceRole,
            MaskReference = LimitText(session.MaskReference, MaxReferenceChars),
            Messages = session.Messages
                .OrderByDescending(message => message.CreatedAt)
                .Take(maxMessages)
                .OrderBy(message => message.CreatedAt)
                .Select(message => CopyMessage(message, maxMessageChars, keepSmallImageUrls))
                .ToList(),
        };
    }

    private static StudioMessage CopyMessage(
        StudioMessage message,
        int maxMessageChars,
        bool keepSmallImageUrls)
    {
        return new StudioMessage
        {
            Id = message.Id,
            Role = message.Role,
            Content = LimitText(message.Content, maxMessageChars),
            CreatedAt = message.CreatedAt,
            Images = message.Images
                .Take(12)
                .Select(image => CopyImage(image, keepSmallImageUrls))
                .ToList(),
        };
    }

    private static GeneratedImage CopyImage(GeneratedImage image, bool keepSmallImageUrls)
    {
        var url = image.Url ?? string.Empty;
        if (!keepSmallImageUrls && url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            url = string.Empty;
        }

        if (url.Length > MaxReferenceChars && url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            url = string.Empty;
        }

        return new GeneratedImage
        {
            Id = image.Id,
            Url = LimitText(url, MaxReferenceChars),
            RevisedPrompt = LimitText(image.RevisedPrompt, MaxPromptChars),
            MimeType = LimitText(image.MimeType, 128),
            CreatedAt = image.CreatedAt,
            Status = image.Status,
            IsFavorite = image.IsFavorite,
            Tags = image.Tags.Take(12).Select(tag => LimitText(tag, 64)).ToList(),
            Prompt = LimitText(image.Prompt, MaxPromptChars),
            RequestPrompt = LimitText(image.RequestPrompt, MaxPromptChars),
            Mode = image.Mode,
            Model = image.Model,
            Size = image.Size,
            AspectRatio = image.AspectRatio,
            ResolutionTier = image.ResolutionTier,
            Quality = image.Quality,
            OutputFormat = image.OutputFormat,
            Background = image.Background,
            Moderation = image.Moderation,
            RequestMode = image.RequestMode,
            RequestCount = image.RequestCount,
            BatchIndex = image.BatchIndex,
            ReferenceCount = image.ReferenceCount,
            ReferenceRole = image.ReferenceRole,
            HasMask = image.HasMask,
            DurationMs = image.DurationMs,
            EstimatedCost = image.EstimatedCost,
            EstimatedDuration = image.EstimatedDuration,
            RequestSummary = LimitText(image.RequestSummary, 512),
        };
    }

    private static StudioSettings CopySettings(StudioSettings settings)
    {
        return new StudioSettings
        {
            BaseUrl = settings.BaseUrl,
            ImageApiKey = settings.ImageApiKey,
            SonnetAccessToken = settings.SonnetAccessToken,
            SonnetRefreshToken = settings.SonnetRefreshToken,
            SonnetTokenExpiresAt = settings.SonnetTokenExpiresAt,
            SonnetUser = settings.SonnetUser,
            SonnetApiKeyId = settings.SonnetApiKeyId,
            SonnetApiKeyName = settings.SonnetApiKeyName,
            SonnetGroupId = settings.SonnetGroupId,
            SonnetGroupName = settings.SonnetGroupName,
            OpenAiApiKey = settings.OpenAiApiKey,
            SonnetOpenAiApiKeyId = settings.SonnetOpenAiApiKeyId,
            SonnetOpenAiApiKeyName = settings.SonnetOpenAiApiKeyName,
            SonnetOpenAiGroupId = settings.SonnetOpenAiGroupId,
            SonnetOpenAiGroupName = settings.SonnetOpenAiGroupName,
            SonnetPaymentType = settings.SonnetPaymentType,
            EmbeddedUserId = settings.EmbeddedUserId,
            EmbeddedUiMode = settings.EmbeddedUiMode,
            EmbeddedLanguage = settings.EmbeddedLanguage,
            EmbeddedSourceHost = LimitText(settings.EmbeddedSourceHost, 512),
            EmbeddedSourceUrl = LimitText(settings.EmbeddedSourceUrl, 1024),
            ThemeMode = settings.ThemeMode,
            ChatModel = settings.ChatModel,
            PromptPolishMode = settings.PromptPolishMode,
            Model = settings.Model,
            Size = settings.Size,
            AspectRatio = settings.AspectRatio,
            ResolutionTier = settings.ResolutionTier,
            Quality = settings.Quality,
            Style = settings.Style,
            Background = settings.Background,
            Format = settings.Format,
            Compression = settings.Compression,
            Moderation = settings.Moderation,
            InputFidelity = settings.InputFidelity,
            ResponseFormat = settings.ResponseFormat,
            RequestMode = settings.RequestMode,
            PartialImages = settings.PartialImages,
            User = LimitText(settings.User, 256),
            AdvancedJson = LimitText(settings.AdvancedJson, MaxAdvancedJsonChars),
        };
    }

    private static string LimitText(string? value, int maxLength)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength].TrimEnd();
    }
}
