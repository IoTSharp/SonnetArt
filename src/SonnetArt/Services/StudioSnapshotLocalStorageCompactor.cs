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
    private const int MaxEmbeddedReferenceChars = 1_000_000;
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
            Type = workspace.Type,
            Name = LimitText(workspace.Name, MaxTitleChars),
            CreatedAt = workspace.CreatedAt,
            UpdatedAt = workspace.UpdatedAt,
            LastOpenedAt = workspace.LastOpenedAt,
            Sessions = sessions,
            ActiveSessionId = sessions.FirstOrDefault(session =>
                string.Equals(session.Id, workspace.ActiveSessionId, StringComparison.Ordinal))?.Id ?? sessions.FirstOrDefault()?.Id,
            CommerceWorkspace = CopyCommerceWorkspace(workspace.CommerceWorkspace, minimal),
        };
    }

    private static CommerceWorkspaceState CopyCommerceWorkspace(CommerceWorkspaceState? commerceWorkspace, bool minimal)
    {
        if (commerceWorkspace is null)
        {
            return new CommerceWorkspaceState();
        }

        var productLimit = minimal ? 3 : 20;
        var planLimit = minimal ? 3 : 20;
        return new CommerceWorkspaceState
        {
            ActiveProductId = commerceWorkspace.ActiveProductId,
            ActiveImagePlanId = commerceWorkspace.ActiveImagePlanId,
            UpdatedAt = commerceWorkspace.UpdatedAt,
            Products = commerceWorkspace.Products
                .Take(productLimit)
                .Select(CopyCommerceProduct)
                .ToList(),
            ImagePlans = commerceWorkspace.ImagePlans
                .Take(planLimit)
                .Select(CopyCommerceImagePlan)
                .ToList(),
        };
    }

    private static CommerceProduct CopyCommerceProduct(CommerceProduct product)
    {
        return new CommerceProduct
        {
            Id = product.Id,
            Name = LimitText(product.Name, MaxTitleChars),
            Description = LimitText(product.Description, MaxPromptChars),
            Specifications = LimitText(product.Specifications, MaxPromptChars),
            TargetAudience = LimitText(product.TargetAudience, 512),
            SellingPoints = product.SellingPoints
                .Take(20)
                .Select(point => LimitText(point, 256))
                .ToList(),
            ReferenceImages = product.ReferenceImages
                .Take(16)
                .Select(LimitReferenceImage)
                .Where(image => !string.IsNullOrWhiteSpace(image))
                .ToList(),
            ReferenceRole = product.ReferenceRole,
            SkuVariants = product.SkuVariants
                .Take(40)
                .Select(CopyCommerceSkuVariant)
                .ToList(),
            Analysis = CopyCommerceProductAnalysis(product.Analysis),
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt,
        };
    }

    private static CommerceProductAnalysis CopyCommerceProductAnalysis(CommerceProductAnalysis? analysis)
    {
        if (analysis is null)
        {
            return new CommerceProductAnalysis();
        }

        return new CommerceProductAnalysis
        {
            ProductType = LimitText(analysis.ProductType, 128),
            CoreSellingPoints = analysis.CoreSellingPoints.Take(12).Select(point => LimitText(point, 128)).ToList(),
            UseScenarios = analysis.UseScenarios.Take(12).Select(scenario => LimitText(scenario, 128)).ToList(),
            ColorVariants = analysis.ColorVariants.Take(20).Select(color => LimitText(color, 96)).ToList(),
            MaterialFeatures = analysis.MaterialFeatures.Take(12).Select(feature => LimitText(feature, 128)).ToList(),
            TargetAudiences = analysis.TargetAudiences.Take(12).Select(audience => LimitText(audience, 128)).ToList(),
            Summary = LimitText(analysis.Summary, 512),
            AnalyzedAt = analysis.AnalyzedAt,
        };
    }

    private static CommerceSkuVariant CopyCommerceSkuVariant(CommerceSkuVariant variant)
    {
        return new CommerceSkuVariant
        {
            Id = variant.Id,
            Name = LimitText(variant.Name, 128),
            Color = LimitText(variant.Color, 128),
            Sku = LimitText(variant.Sku, 128),
        };
    }

    private static CommerceImagePlan CopyCommerceImagePlan(CommerceImagePlan plan)
    {
        return new CommerceImagePlan
        {
            Id = plan.Id,
            ProductId = plan.ProductId,
            Title = LimitText(plan.Title, MaxTitleChars),
            Nodes = plan.Nodes
                .Take(40)
                .Select(CopyCommerceImageNode)
                .ToList(),
            CreatedAt = plan.CreatedAt,
            UpdatedAt = plan.UpdatedAt,
        };
    }

    private static CommerceImageNode CopyCommerceImageNode(CommerceImageNode node)
    {
        return new CommerceImageNode
        {
            Id = node.Id,
            Type = LimitText(node.Type, 64),
            Title = LimitText(node.Title, 128),
            Goal = LimitText(node.Goal, 512),
            AspectRatio = node.AspectRatio,
            Prompt = LimitText(node.Prompt, MaxPromptChars),
            NegativePrompt = LimitText(node.NegativePrompt, MaxPromptChars),
            ReferenceRole = node.ReferenceRole,
            Enabled = node.Enabled,
            Status = LimitText(node.Status, 64),
            PlannedCount = node.PlannedCount,
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

    private static string LimitReferenceImage(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return text.Length <= MaxEmbeddedReferenceChars ? text : string.Empty;
        }

        return LimitText(text, MaxReferenceChars);
    }
}
