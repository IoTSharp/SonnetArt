using AntDesign;
using AntDesign.X;
using AntDesign.X.Components;
using SonnetArt.Models;
using SonnetArt.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using QRCoder;
using System.Reflection;

namespace SonnetArt.Pages;

public partial class Home
{
    private static readonly string AppVersion = FormatAppVersion(
        typeof(Home).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Home).Assembly.GetName().Version?.ToString(3)
        ?? "0.1.0");

    private static string FormatAppVersion(string version)
    {
        var metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            version = version[..metadataIndex];
        }

        version = version.Trim();
        if (string.IsNullOrWhiteSpace(version))
        {
            return "v0.1.0";
        }

        return version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : $"v{version}";
    }

    private static readonly XThemeTokens LightTokens = new()
    {
        PrimaryColor = "#0f8fff",
        BorderRadius = "8px",
        ColorBgChat = "#f8fbff",
        ColorBgBubbleUser = "#1557d7",
        ColorBgBubbleAi = "rgba(255, 255, 255, 0.9)",
        ColorTextBubbleUser = "#ffffff",
        ColorTextBubbleAi = "#132137",
        ColorBorderBubble = "rgba(95, 115, 145, 0.2)",
        FontFamily = "Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif",
    };

    private static readonly XThemeTokens DarkTokens = new()
    {
        PrimaryColor = "#4fb6ff",
        BorderRadius = "8px",
        ColorBgChat = "#08101d",
        ColorBgBubbleUser = "#1557d7",
        ColorBgBubbleAi = "rgba(20, 31, 50, 0.9)",
        ColorTextBubbleUser = "#ecfeff",
        ColorTextBubbleAi = "#edf5ff",
        ColorBorderBubble = "rgba(159, 181, 215, 0.16)",
        FontFamily = "Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif",
    };

    private static readonly JsonSerializerOptions ClipboardJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IReadOnlyList<AspectRatioPreset> _aspectRatioPresets =
    [
        new("1:1"),
        new("3:4"),
        new("2:3"),
        new("9:16"),
        new("3:2"),
        new("4:3"),
        new("16:9", "电影"),
        new("21:9", "电影"),
    ];
    private readonly IReadOnlyList<ResolutionPreset> _resolutionPresets =
    [
        new("1k", "1K", "快速预览，最长边约 1,024px"),
        new("2k", "2K", "标准输出，最长边约 1,792px"),
        new("4k", "2MP", "高清画幅，最长边约 2,048px"),
        new("8mp", "8MP Max", "官方最大画幅，最高约 8.29MP / 3840px"),
    ];
    private readonly IReadOnlyList<string> _modelOptions =
    [
        "gpt-image-2",
        "gpt-image-2-2026-04-21",
    ];
    private readonly IReadOnlyList<string> _moderationOptions = ["auto", "low"];
    private readonly IReadOnlyList<string> _fidelityOptions = ["默认", "high", "low"];
    private readonly IReadOnlyList<PromptPolishOption> _promptPolishOptions =
    [
        new("direct", "直接生成", "不再每次询问，按原提示词生成。"),
        new("ask", "每次询问", "提交作图需求后询问是否先润色。"),
        new("auto", "自动润色", "先润色提示词，再生成图片。"),
    ];
    private readonly IReadOnlyList<ReferenceRoleOption> _referenceRoleOptions =
    [
        new("auto", "自动", "按提示词自动理解参考图用途"),
        new("content", "内容", "保留主体、构图和场景内容"),
        new("style", "风格", "提取色彩、材质、光影和画风"),
        new("character", "人物", "保持人物身份和脸部一致性"),
        new("product", "产品", "保持产品外观、比例和关键细节"),
    ];
    private readonly IReadOnlyList<MatrixResolutionOption> _matrixResolutionOptions =
    [
        new("1k", "1K"),
        new("2k", "2K"),
        new("4k", "2MP"),
        new("8mp", "8MP"),
    ];
    private readonly IReadOnlyList<string> _matrixQualityOptions = ["auto", "high", "medium", "low"];
    private const int PromptLibraryPageSize = 20;
    private readonly IReadOnlyList<string> _promptIdeas =
    [
        "一张极简产品海报，玻璃茶杯悬浮在白色背景中",
        "赛博城市夜景，中文霓虹标牌，电影级构图",
        "毛玻璃质感的 AI 作图应用图标，适合桌面端",
    ];
    private const string PromptConfirmRole = "prompt-confirm";
    private StudioSnapshot _snapshot = CreateInitialSnapshot();
    private string _senderText = string.Empty;
    private PromptLibraryPage _promptLibraryPage = PromptLibraryPage.Empty(PromptLibraryPageSize);
    private bool _promptLibraryLoading;
    private long _promptLibraryRequestId;
    private string? _promptLibraryNotice;
    private int _count = 1;
    private bool _galleryOpen;
    private string _gallerySearch = string.Empty;
    private string _gallerySessionFilter = "all";
    private string _galleryModelFilter = "all";
    private string _gallerySizeFilter = "all";
    private string _galleryStatusFilter = "all";
    private string _galleryDateFilter = "all";
    private string _galleryTagFilter = "all";
    private bool _galleryFavoritesOnly;
    private bool _matrixPanelOpen;
    private readonly HashSet<string> _selectedMatrixResolutionTiers = new(StringComparer.OrdinalIgnoreCase) { "2k", "4k" };
    private readonly HashSet<string> _selectedMatrixQualities = new(StringComparer.OrdinalIgnoreCase) { "auto", "high" };
    private bool _loading;
    private string _loadingLabel = "正在请求图像接口";
    private bool _leftSidebarCollapsed;
    private bool _rightPromptPanelCollapsed;
    private bool _settingsOpen;
    private bool _exitConfirmOpen;
    private bool _workspaceCreateOpen;
    private string _workspaceCreateName = string.Empty;
    private bool _sonnetBusy;
    private bool _sonnetRegisterOpen;
    private string? _error;
    private string? _downloadNotice;
    private string? _lastRawJson;
    private string _sonnetEmail = string.Empty;
    private string _sonnetPassword = string.Empty;
    private string _sonnetPromoCode = string.Empty;
    private string _sonnetInvitationCode = string.Empty;
    private string _sonnetAffiliateCode = string.Empty;
    private string _redeemCode = string.Empty;
    private string? _sonnetMessage;
    private bool _sonnetMessageIsError;
    private decimal _rechargeAmount = 10;
    private SonnetCreateOrderResult? _activePaymentOrder;
    private SonnetPaymentOrder? _latestPaymentStatus;
    private bool _paymentOverlayOpen;
    private bool _paymentPolling;
    private bool _paymentCancelling;
    private string? _paymentQrImageSource;
    private DateTimeOffset? _paymentExpiresAt;
    private PeriodicTimer? _paymentTimer;
    private CancellationTokenSource? _paymentCts;
    private CancellationTokenSource? _cts;
    private PeriodicTimer? _serverStatusTimer;
    private CancellationTokenSource? _serverStatusCts;
    private DotNetObjectReference<Home>? _selfReference;
    private string? _systemThemeWatchId;
    private string? _lastDocumentTheme;
    private bool _systemPrefersDark;
    private SiteBranding _branding = SiteBranding.Default;
    private EmbeddedLaunchContext _launchContext = new();
    private bool _serverOnline;
    private bool _serverStatusChecked;
    private DateTimeOffset? _serverStatusCheckedAt;
    private bool _navigatedToLastWorkspace;
    private PendingPrompt? _pendingPrompt;
    private bool _ratioMenuOpen;
    private bool _resolutionMenuOpen;
    private GeneratedImage? _previewImage;
    private string _previewAlt = string.Empty;
    private bool _previewZoomed;
    private bool _previewEditing;
    private bool _previewMaskAttachPending;
    private int _previewBrushSize = 36;
    private string _previewEditPrompt = string.Empty;
    private string? _previewEditError;
    private ElementReference _previewImageElement;
    private ElementReference _previewMaskCanvas;
    private readonly List<ImageReferenceFile> _referenceFiles = [];
    private readonly List<SenderImageAttachment> _senderImageAttachments = [];
    private IReadOnlyList<XAttachmentItem> _senderAttachments = [];

    private StudioSettings Settings => _snapshot.Settings;

    private StudioWorkspace ActiveGraphicWorkspace => _snapshot.GetActiveWorkspace();

    private StudioSession ActiveSession =>
        ActiveGraphicWorkspace.Sessions.FirstOrDefault(session => session.Id == ActiveGraphicWorkspace.ActiveSessionId)
        ?? ActiveGraphicWorkspace.Sessions.First();

    private bool SonnetLoggedIn => !string.IsNullOrWhiteSpace(Settings.SonnetAccessToken);
    private bool AccountReady => SonnetLoggedIn &&
        !string.IsNullOrWhiteSpace(Settings.ImageApiKey) &&
        !string.IsNullOrWhiteSpace(Settings.OpenAiApiKey);
    private bool RequiresAuthOverlay => !AccountReady && !_sonnetBusy;
    private string EffectiveTheme => Settings.ThemeMode == "dark" || (Settings.ThemeMode == "system" && _systemPrefersDark)
        ? "dark"
        : "light";
    private string ProviderTheme => EffectiveTheme;
    private XThemeTokens ThemeTokens => EffectiveTheme == "dark" ? DarkTokens : LightTokens;
    private string RootThemeClass => $"studio-provider theme-{EffectiveTheme}";
    private string AccountEmail => string.IsNullOrWhiteSpace(Settings.SonnetUser?.Email) ? "未登录" : Settings.SonnetUser.Email;
    private string BalanceLabel => Settings.SonnetUser is null ? "--" : FormatMoney(Settings.SonnetUser.Balance);
    private string SiteTitle => _branding.Name;
    private string SiteDescription => _branding.Description;
    private string? SiteIconUrl => _branding.IconUrl;
    private string HeaderSubtitle => string.IsNullOrWhiteSpace(SiteDescription)
        ? AppVersion
        : $"{SiteDescription} · {AppVersion}";
    private string SonnetMessageClass => _sonnetMessageIsError ? "settings-message error" : "settings-message";
    private string PromptPolishModeLabel =>
        _promptPolishOptions.FirstOrDefault(option => option.Value == Settings.PromptPolishMode)?.Label ?? "直接生成";
    private string ServerStatusClass => _serverOnline
        ? "server-status online"
        : _serverStatusChecked
            ? "server-status offline"
            : "server-status checking";
    private string ServerStatusText => _serverOnline
        ? "服务联机正常"
        : _serverStatusChecked
            ? "服务连接异常"
            : "正在检查服务";
    private string ServerStatusTitle => _serverStatusCheckedAt is null
        ? ServerStatusText
        : $"{ServerStatusText} · {_serverStatusCheckedAt.Value.ToLocalTime():HH:mm:ss}";
    private string BodyClass =>
        $"studio-body{(_leftSidebarCollapsed ? " left-collapsed" : string.Empty)}{(_rightPromptPanelCollapsed ? " right-collapsed" : string.Empty)}";
    private string SidebarClass => _leftSidebarCollapsed ? "studio-sidebar is-collapsed" : "studio-sidebar";
    private string PromptPanelClass => _rightPromptPanelCollapsed ? "prompt-sidebar is-collapsed" : "prompt-sidebar";
    private string CanvasClass => _loading ? "workspace-canvas is-loading" : "workspace-canvas";
    private string WorkspaceStatus => _loading
        ? $"正在生成，请保持窗口打开 · {EstimateDurationLabel(_count, Settings.ResolutionTier, ActiveSession.Mode)}"
        : GalleryImages.Count == 0
            ? "已就绪，可以开始作图"
            : $"作品库 {GalleryImages.Count} 张 · 当前筛选 {FilteredGalleryImages.Count} 张";
    private string AuthButtonText => _sonnetBusy ? "处理中..." : _sonnetRegisterOpen ? "创建并登录" : "登录";
    private string PaymentTitle => PaymentSucceeded
        ? "支付完成"
        : PaymentExpired
            ? "订单已过期"
            : "扫码完成支付";
    private string PaymentStatusLabel => PaymentSucceeded
        ? "已完成"
        : PaymentExpired
            ? "已过期"
            : _paymentPolling
                ? "确认中"
                : "待支付";
    private string PaymentCountdown => RemainingPaymentSeconds <= 0
        ? "00:00"
        : $"{RemainingPaymentSeconds / 60:00}:{RemainingPaymentSeconds % 60:00}";
    private string? PaymentQrImageSource => _paymentQrImageSource;
    private int RemainingPaymentSeconds => _paymentExpiresAt is null
        ? 0
        : Math.Max(0, (int)Math.Ceiling((_paymentExpiresAt.Value - DateTimeOffset.Now).TotalSeconds));
    private bool PaymentSucceeded => IsPaymentSuccess(_latestPaymentStatus?.Status);
    private bool PaymentExpired => RemainingPaymentSeconds <= 0 || IsPaymentExpired(_latestPaymentStatus?.Status);
    private bool HasPendingPaymentOrder => _activePaymentOrder is not null && !PaymentSucceeded && !PaymentExpired;
    private string ModeLabel => ActiveSession.Mode switch
    {
        "image" => "图生图",
        "edit" => "图片编辑",
        "variation" => "变化",
        _ => "文生图",
    };
    private string InputFileLabel => ActiveSession.Mode == "edit" ? "待编辑图片" : "参考图";
    private string InputFileAccept => "image/*";
    private AspectRatioPreset CurrentAspectRatio =>
        _aspectRatioPresets.FirstOrDefault(preset => preset.Ratio == EffectiveAspectRatio)
        ?? _aspectRatioPresets[0];
    private ResolutionPreset CurrentResolution =>
        AvailableResolutionPresets.FirstOrDefault(preset => preset.Tier == Settings.ResolutionTier)
        ?? _resolutionPresets.First(preset => preset.Tier == "2k");
    private string EffectiveAspectRatio => ResolveAspectRatio(Settings.AspectRatio, Settings.Size);
    private ImageModelCapabilities CurrentModelCapabilities => ImageModelCatalog.Get(Settings.Model, ActiveSession.Mode);
    private IReadOnlyList<ResolutionPreset> AvailableResolutionPresets =>
        IsGptImage2Model(CurrentModelCapabilities.Model)
            ? _resolutionPresets
            : _resolutionPresets.Where(preset => preset.Tier is "1k" or "2k").ToArray();
    private IReadOnlyList<string> CurrentQualityOptions => CurrentModelCapabilities.Qualities;
    private IReadOnlyList<string> CurrentFormatOptions => CurrentModelCapabilities.OutputFormats.Count == 0
        ? ["默认"]
        : CurrentModelCapabilities.OutputFormats;
    private IReadOnlyList<string> CurrentBackgroundOptions => CurrentModelCapabilities.Backgrounds.Count == 0
        ? ["默认"]
        : CurrentModelCapabilities.Backgrounds;
    private IReadOnlyList<string> CurrentStyleOptions => CurrentModelCapabilities.Styles.Count == 0
        ? ["默认"]
        : CurrentModelCapabilities.Styles;
    private string QuickSettingsSummary =>
        $"{Settings.Model} · 实际 {EffectiveImageSize} · {CurrentAspectRatio.Label} · {QualityOptionLabel(EffectiveQuality)} · {EffectiveFormatLabel}";
    private string CostAndDurationSummary =>
        $"{EstimateCostLabel(_count, Settings.ResolutionTier, EffectiveQuality)} · {EstimateDurationLabel(_count, Settings.ResolutionTier, ActiveSession.Mode)}";
    private string MatrixPanelButtonClass => _matrixPanelOpen
        ? "studio-button studio-button-light active"
        : "studio-button studio-button-light";
    private bool CanRunMatrix => MatrixRuns.Count > 0 && !_loading;
    private string MatrixSummary => MatrixRuns.Count == 0
        ? "选择至少一个分辨率和一个质量"
        : $"{MatrixRuns.Count} 组 · {EstimateMatrixCostLabel(MatrixRuns)} · {EstimateMatrixDurationLabel(MatrixRuns, ActiveSession.Mode)}";
    private string GalleryToggleText => _galleryOpen ? "会话流" : "作品库";
    private string GalleryToggleIcon => _galleryOpen ? "message" : "appstore";
    private string GalleryEmptyText => GalleryImages.Count == 0
        ? "还没有作品。生成图片后会自动进入作品库。"
        : "没有符合当前筛选的作品。";
    private string EffectiveImageSize => BuildImageSize(EffectiveAspectRatio, Settings.ResolutionTier);
    private string EffectiveQuality => CurrentModelCapabilities.Qualities.Contains(Settings.Quality) ? Settings.Quality : CurrentModelCapabilities.Qualities[0];
    private string EffectiveFormatLabel => CurrentModelCapabilities.SupportsOutputFormat ? Settings.Format.ToUpperInvariant() : "默认格式";
    private string ActualRequestSummary =>
        $"实际参数：model={Settings.Model}，size={EffectiveImageSize}，quality={EffectiveQuality}" +
        (CurrentModelCapabilities.SupportsOutputFormat ? $"，output_format={Settings.Format}" : string.Empty) +
        (CurrentModelCapabilities.SupportsBackground ? $"，background={Settings.Background}" : string.Empty) +
        (CurrentModelCapabilities.SupportsStream ? $"，request={Settings.RequestMode}" : "，request=sync") +
        $"。{CurrentModelCapabilities.SizeNote}";
    private string ModelCapabilityNote =>
        "GPT Image 2 使用 generations/edits 接口；参考图、编辑、遮罩和变化都走 edits。当前不暴露旧 DALL-E variations、流式、partial_images、透明背景、response_format 或 input_fidelity。";
    private string CurrentRatioPreviewClass => RatioPreviewClass(CurrentAspectRatio);
    private string ImagePreviewTitle => string.IsNullOrWhiteSpace(_previewAlt) ? "生成图片" : _previewAlt;
    private string ImagePreviewDialogClass =>
        $"image-preview-dialog{(_previewZoomed ? " is-zoomed" : string.Empty)}{(_previewEditing ? " is-editing" : string.Empty)}";
    private string ImagePreviewStageClass => _previewEditing ? "image-preview-stage is-editing" : "image-preview-stage";
    private string ImagePreviewEditButtonClass => _previewEditing ? "active" : string.Empty;
    private bool PreviewEditSubmitDisabled => _loading || _previewImage is null || string.IsNullOrWhiteSpace(_previewEditPrompt);
    private IReadOnlyList<GeneratedImage> LatestImages =>
        ActiveSession.Messages.LastOrDefault(message => message.Images.Count > 0)?.Images ?? [];

    private IReadOnlyList<GalleryImageItem> GalleryImages =>
        ActiveGraphicWorkspace.Sessions
            .SelectMany(session => session.Messages
                .SelectMany(message => message.Images
                    .Select((image, index) => new GalleryImageItem(session, message, image, index))))
            .Where(item => !string.IsNullOrWhiteSpace(item.Image.Url))
            .OrderByDescending(item => EffectiveImageCreatedAt(item.Image, item.Message))
            .ToArray();

    private IReadOnlyList<GalleryImageItem> FilteredGalleryImages =>
        GalleryImages.Where(MatchesGalleryFilter).ToArray();

    private IReadOnlyList<string> GalleryModelOptions =>
        GalleryImages
            .Select(item => NormalizeGalleryOption(item.Image.Model, Settings.Model))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private IReadOnlyList<string> GallerySizeOptions =>
        GalleryImages
            .Select(item => NormalizeGalleryOption(item.Image.Size, EffectiveImageSize))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private IReadOnlyList<string> GalleryTagOptions =>
        GalleryImages
            .SelectMany(item => item.Image.Tags)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private IReadOnlyList<ImageMatrixRun> MatrixRuns =>
        _selectedMatrixResolutionTiers
            .Select(StudioSettings.NormalizeResolutionTier)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(tier => AvailableResolutionPresets.Any(preset => string.Equals(preset.Tier, tier, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(tier => _selectedMatrixQualities
                .Where(quality => CurrentQualityOptions.Any(option => string.Equals(option, quality, StringComparison.OrdinalIgnoreCase)))
                .Select(quality => new ImageMatrixRun(tier, quality, BuildImageSize(EffectiveAspectRatio, tier))))
            .Take(12)
            .ToArray();

    private IReadOnlyList<XConversationItem> ConversationItems =>
        ActiveGraphicWorkspace.Sessions
            .OrderByDescending(session => session.UpdatedAt)
            .Select(session => new XConversationItem
            {
                Key = session.Id,
                Title = session.Title,
                Description = $"{ModeName(session.Mode)} · {session.UpdatedAt.ToLocalTime():MM-dd HH:mm}",
                Icon = session.Messages.Count > 0 ? "picture" : "message",
                Group = session.UpdatedAt.Date == DateTimeOffset.Now.Date ? "今天" : "更早",
                Count = session.Messages.Count == 0 ? null : session.Messages.Count,
                UpdatedAt = session.UpdatedAt,
            })
            .ToArray();

    private IReadOnlyList<WorkspaceSidebarItem> WorkspaceItems =>
        _snapshot.Workspaces
            .OrderBy(workspace => WorkspaceSortOrder("graphic"))
            .ThenByDescending(workspace => workspace.LastOpenedAt)
            .ThenBy(workspace => workspace.Name, StringComparer.OrdinalIgnoreCase)
            .Select(workspace => new WorkspaceSidebarItem(
                workspace.Id,
                workspace.Name,
                WorkspaceDescription(workspace),
                WorkspaceIcon("graphic"),
                "graphic"))
            .ToArray();

    protected override async Task OnInitializedAsync()
    {
        _snapshot = await Storage.LoadAsync();
        _launchContext = EmbeddedLaunchContextParser.Parse(Navigation);
        ApplyLaunchContext(_launchContext);
        _branding = await SiteConfig.LoadBrandingAsync();
        await LoadPromptLibraryPage(new PromptLibraryQuery(
            1,
            PromptLibraryPageSize,
            ResolvePromptLibraryLanguage(_launchContext.Language),
            null,
            null,
            null));
        EnsureActiveMode();
        StartServerStatusPolling();
        await RestoreAccountAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _systemPrefersDark = await JsRuntime.InvokeAsync<bool>("sonnetArt.prefersDarkTheme");
                _selfReference = DotNetObjectReference.Create(this);
                _systemThemeWatchId = await JsRuntime.InvokeAsync<string>("sonnetArt.watchSystemTheme", _selfReference);
                await JsRuntime.InvokeVoidAsync("sonnetArt.clearLaunchCredentials");
            }
            catch (JSException)
            {
            }
        }

        await SyncDocumentThemeAsync();
        await SyncSiteBrandingAsync();

        if (firstRender)
        {
            StateHasChanged();
        }

        if (firstRender)
        {
            await NavigateToLastWorkspaceIfNeededAsync();
        }

        if (_previewMaskAttachPending)
        {
            _previewMaskAttachPending = false;
            await ResizePreviewMaskAsync();
        }
    }

    private static StudioSnapshot CreateInitialSnapshot()
    {
        var session = new StudioSession();
        var snapshot = new StudioSnapshot
        {
            Settings = new StudioSettings(),
            Sessions = [session],
            ActiveSessionId = session.Id,
        };
        snapshot.Normalize();
        return snapshot;
    }

    private string TabClass(string mode) => ActiveSession.Mode == mode ? "active" : string.Empty;
    private string ThemeOptionClass(string mode) => Settings.ThemeMode == mode ? "active" : string.Empty;

    private void ApplyLaunchContext(EmbeddedLaunchContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.AccessToken))
        {
            Settings.SonnetAccessToken = context.AccessToken.Trim();
            Settings.SonnetRefreshToken = string.Empty;
            Settings.SonnetTokenExpiresAt = null;
        }

        if (context.UserId is > 0)
        {
            Settings.EmbeddedUserId = context.UserId;
            Settings.User = $"sonnet:{context.UserId.Value}";
        }

        if (!string.IsNullOrWhiteSpace(context.UiMode))
        {
            Settings.EmbeddedUiMode = StudioSettings.NormalizeEmbeddedUiMode(context.UiMode);
        }

        if (!string.IsNullOrWhiteSpace(context.Language))
        {
            Settings.EmbeddedLanguage = StudioSettings.NormalizeEmbeddedLanguage(context.Language);
        }

        if (!string.IsNullOrWhiteSpace(context.SourceHost))
        {
            Settings.EmbeddedSourceHost = context.SourceHost.Trim();
        }

        if (!string.IsNullOrWhiteSpace(context.SourceUrl))
        {
            Settings.EmbeddedSourceUrl = context.SourceUrl.Trim();
        }

        if (!string.IsNullOrWhiteSpace(context.Theme))
        {
            Settings.ThemeMode = StudioSettings.NormalizeThemeMode(context.Theme);
        }
    }

    private static PromptLibraryLanguage ResolvePromptLibraryLanguage(string? language)
    {
        return language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true
            ? PromptLibraryLanguage.English
            : PromptLibraryLanguage.Chinese;
    }

    private async Task SyncSiteBrandingAsync()
    {
        try
        {
            await JsRuntime.InvokeVoidAsync(
                "sonnetArt.applySiteBranding",
                new
                {
                    title = SiteTitle,
                    description = SiteDescription,
                    iconUrl = SiteIconUrl,
                });
        }
        catch (JSException)
        {
        }
    }

    private static string ResolveAspectRatio(string? aspectRatio, string? size)
    {
        var normalized = StudioSettings.NormalizeAspectRatio(aspectRatio);
        if (normalized != "auto")
        {
            return normalized;
        }

        return size switch
        {
            "1024x1024" or "2048x2048" or "2880x2880" or "512x512" or "256x256" => "1:1",
            "896x1152" or "1024x1360" or "1536x2048" or "2448x3264" => "3:4",
            "832x1248" or "1024x1536" or "1360x2048" or "2336x3504" => "2:3",
            "768x1360" or "1024x1792" or "1152x2048" or "2160x3840" => "9:16",
            "1248x832" or "1536x1024" or "2048x1360" or "3504x2336" => "3:2",
            "1152x896" or "1360x1024" or "2048x1536" or "3264x2448" => "4:3",
            "1360x768" or "1792x1024" or "2048x1152" or "3840x2160" => "16:9",
            "1536x640" or "1792x768" or "2048x896" or "3840x1648" => "21:9",
            _ => "1:1",
        };
    }

    private static string BuildImageSize(string aspectRatio, string resolutionTier)
    {
        aspectRatio = StudioSettings.NormalizeAspectRatio(aspectRatio);
        resolutionTier = StudioSettings.NormalizeResolutionTier(resolutionTier);

        return (resolutionTier, aspectRatio) switch
        {
            ("1k", "1:1") => "1024x1024",
            ("1k", "3:4") => "896x1152",
            ("1k", "2:3") => "832x1248",
            ("1k", "9:16") => "768x1360",
            ("1k", "3:2") => "1248x832",
            ("1k", "4:3") => "1152x896",
            ("1k", "16:9") => "1360x768",
            ("1k", "21:9") => "1536x640",
            ("4k", "1:1") => "2048x2048",
            ("4k", "3:4") => "1536x2048",
            ("4k", "2:3") => "1360x2048",
            ("4k", "9:16") => "1152x2048",
            ("4k", "3:2") => "2048x1360",
            ("4k", "4:3") => "2048x1536",
            ("4k", "16:9") => "2048x1152",
            ("4k", "21:9") => "2048x896",
            ("8mp", "1:1") => "2880x2880",
            ("8mp", "3:4") => "2448x3264",
            ("8mp", "2:3") => "2336x3504",
            ("8mp", "9:16") => "2160x3840",
            ("8mp", "3:2") => "3504x2336",
            ("8mp", "4:3") => "3264x2448",
            ("8mp", "16:9") => "3840x2160",
            ("8mp", "21:9") => "3840x1648",
            (_, "3:4") => "1024x1360",
            (_, "2:3") => "1024x1536",
            (_, "9:16") => "1024x1792",
            (_, "3:2") => "1536x1024",
            (_, "4:3") => "1360x1024",
            (_, "16:9") => "1792x1024",
            (_, "21:9") => "1792x768",
            _ => "1024x1024",
        };
    }

    private string RatioPreviewClass(AspectRatioPreset preset) =>
        $"quick-ratio-preview ratio-{preset.Ratio.Replace(':', '-')}";

    private string RatioOptionClass(AspectRatioPreset preset) =>
        IsCurrentAspectRatio(preset) ? "active" : string.Empty;

    private string ResolutionOptionClass(ResolutionPreset preset) =>
        IsCurrentResolution(preset) ? "active" : string.Empty;

    private bool IsCurrentAspectRatio(AspectRatioPreset preset) =>
        string.Equals(CurrentAspectRatio.Ratio, preset.Ratio, StringComparison.Ordinal);

    private bool IsCurrentResolution(ResolutionPreset preset) =>
        string.Equals(CurrentResolution.Tier, preset.Tier, StringComparison.Ordinal);

    private static string QualityOptionLabel(string value) => value switch
    {
        "auto" => "自动",
        "high" => "高",
        "medium" => "中",
        "low" => "低",
        "hd" => "HD",
        "standard" => "标准",
        "默认" => "默认",
        _ => value,
    };
    private static string ReferenceRoleLabel(string value) => StudioSnapshot.NormalizeReferenceRole(value) switch
    {
        "content" => "内容参考",
        "style" => "风格参考",
        "character" => "人物一致",
        "product" => "产品一致",
        _ => "自动参考",
    };

    private static string GalleryStatusLabel(string value) => value switch
    {
        "favorite" => "收藏",
        "regenerated" => "已复现",
        "reference" => "已作参考",
        _ => "已生成",
    };

    private static string GalleryDateFilterLabel(string value) => value switch
    {
        "today" => "今天",
        "week" => "7 天",
        "month" => "30 天",
        _ => "全部时间",
    };

    private static string ImageModeLabel(GeneratedImage image) => ModeName(image.Mode);

    private static string GalleryImageStatus(GeneratedImage image)
    {
        return image.IsFavorite
            ? "favorite"
            : string.IsNullOrWhiteSpace(image.Status) ? "generated" : image.Status;
    }

    private static DateTimeOffset EffectiveImageCreatedAt(GeneratedImage image, StudioMessage message)
    {
        return image.CreatedAt == default ? message.CreatedAt : image.CreatedAt;
    }

    private static string NormalizeGalleryOption(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string FormatImageDate(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        return local.Date == DateTimeOffset.Now.Date
            ? local.ToString("HH:mm")
            : local.ToString("MM-dd HH:mm");
    }

    private static string FormatDuration(long durationMs)
    {
        if (durationMs <= 0)
        {
            return "--";
        }

        return durationMs >= 60_000
            ? $"{durationMs / 60_000}m {(durationMs % 60_000) / 1000}s"
            : $"{Math.Max(1, durationMs / 1000)}s";
    }

    private static string EstimateCostLabel(int count, string resolutionTier, string quality)
    {
        var units = Math.Clamp(count, 1, 8) * ResolutionCostWeight(resolutionTier) * QualityCostWeight(quality);
        return $"预估 {units:0.#} 点";
    }

    private static string EstimateDurationLabel(int count, string resolutionTier, string mode)
    {
        var seconds = (8 + ResolutionDurationSeconds(resolutionTier) + ModeDurationSeconds(mode)) * Math.Clamp(count, 1, 8);
        return $"约 {seconds}-{seconds + 18} 秒";
    }

    private static string EstimateMatrixCostLabel(IReadOnlyList<ImageMatrixRun> runs)
    {
        if (runs.Count == 0)
        {
            return "预估 0 点";
        }

        var units = runs.Sum(run => ResolutionCostWeight(run.ResolutionTier) * QualityCostWeight(run.Quality));
        return $"预估 {units:0.#} 点";
    }

    private static string EstimateMatrixDurationLabel(IReadOnlyList<ImageMatrixRun> runs, string mode)
    {
        if (runs.Count == 0)
        {
            return "约 0 秒";
        }

        var seconds = runs.Sum(run => 8 + ResolutionDurationSeconds(run.ResolutionTier) + ModeDurationSeconds(mode));
        return $"约 {seconds}-{seconds + 18} 秒";
    }

    private static decimal ResolutionCostWeight(string resolutionTier)
    {
        return StudioSettings.NormalizeResolutionTier(resolutionTier) switch
        {
            "1k" => 0.6m,
            "4k" => 1.6m,
            "8mp" => 2.4m,
            _ => 1m,
        };
    }

    private static decimal QualityCostWeight(string quality)
    {
        return quality?.Trim().ToLowerInvariant() switch
        {
            "high" => 1.35m,
            "low" => 0.75m,
            _ => 1m,
        };
    }

    private static int ResolutionDurationSeconds(string resolutionTier)
    {
        return StudioSettings.NormalizeResolutionTier(resolutionTier) switch
        {
            "1k" => 8,
            "4k" => 18,
            "8mp" => 30,
            _ => 13,
        };
    }

    private static int ModeDurationSeconds(string mode)
    {
        return NormalizeMode(mode) switch
        {
            "edit" => 8,
            "image" or "variation" => 5,
            _ => 0,
        };
    }
    private static bool IsGptImage2Model(string model) =>
        model.Equals("gpt-image-2", StringComparison.OrdinalIgnoreCase) ||
        model.Equals("gpt-image-2-2026-04-21", StringComparison.OrdinalIgnoreCase);
    private void CoerceImageSettingsForCurrentModel()
    {
        ImageModelCatalog.NormalizeSettings(Settings, ActiveSession.Mode);
        Settings.ResolutionTier = NormalizeResolutionTierForModel(Settings.ResolutionTier, CurrentModelCapabilities);
        Settings.AspectRatio = EffectiveAspectRatio;
        Settings.Size = BuildImageSize(Settings.AspectRatio, Settings.ResolutionTier);
        ImageModelCatalog.NormalizeSettings(Settings, ActiveSession.Mode);
    }

    private static string NormalizeResolutionTierForModel(string? tier, ImageModelCapabilities capabilities)
    {
        var normalized = StudioSettings.NormalizeResolutionTier(tier);
        if (IsGptImage2Model(capabilities.Model))
        {
            return normalized;
        }

        return normalized is "4k" or "8mp" ? "2k" : normalized;
    }
    private static string ThemeModeLabel(string mode) => mode switch
    {
        "light" => "浅色",
        "dark" => "深色",
        _ => "系统",
    };
    private string PromptPolishOptionClass(string mode) =>
        Settings.PromptPolishMode == mode ? "active" : string.Empty;
    private string AuthTabClass(bool register) => _sonnetRegisterOpen == register ? "active" : string.Empty;
    private void SetLeftSidebarCollapsed(bool collapsed) => _leftSidebarCollapsed = collapsed;
    private void ToggleLeftSidebarCollapsed() => _leftSidebarCollapsed = !_leftSidebarCollapsed;
    private void SetRightPromptPanelCollapsed(bool collapsed) => _rightPromptPanelCollapsed = collapsed;
    private static int WorkspaceSortOrder(string type) => 0;
    private static string WorkspaceIcon(string type) => "picture";
    private static string WorkspaceTypeLabel(string type) => "平面设计";
    private static string WorkspaceDescription(StudioWorkspace workspace)
    {
        return $"平面设计 · {workspace.Sessions.Count} 个会话";
    }
    private static string MessageClass(StudioMessage message) => $"thread-message {message.Role}";
    private static string MessageRoleLabel(string role) => role switch
    {
        "user" => "我",
        "assistant" => "图像结果",
        PromptConfirmRole => "提示词",
        "system" => "系统",
        _ => "消息",
    };
    private static bool ShouldShowMessageText(StudioMessage message) =>
        !string.IsNullOrWhiteSpace(message.Content) &&
        (message.Images.Count == 0 || !string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase));

    private async Task GenerateAsync(string? promptOverride = null, bool addUserMessage = true, string? loadingLabel = null)
    {
        if (_loading)
        {
            return;
        }

        if (!AccountReady)
        {
            _error = "请先登录账户。";
            _settingsOpen = false;
            SetSonnetMessage("登录后会自动完成作图配置。", isError: false);
            return;
        }

        var prompt = (promptOverride ?? ActiveSession.Prompt).Trim();
        var requestMode = ResolveRequestMode(prompt, ActiveSession.Mode);
        ApplyResolvedMode(requestMode);

        if (prompt.Length == 0 && requestMode != "variation")
        {
            _error = "请先填写提示词。";
            return;
        }

        _error = null;
        ClearDownloadNotice();
        _loading = true;
        _loadingLabel = string.IsNullOrWhiteSpace(loadingLabel) ? "正在请求图像接口" : loadingLabel;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var completed = false;

        if (addUserMessage && prompt.Length > 0)
        {
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "user",
                Content = prompt,
            });
            TouchActiveSession(prompt);
        }

        StateHasChanged();
        await SaveAsync();

        try
        {
            var requestSettings = CreateRequestSettings();
            var references = ParseReferences();
            var referenceFiles = _referenceFiles.ToArray();
            var maskReference = ActiveSession.MaskReference;
            var stopwatch = Stopwatch.StartNew();
            var result = await ImageClient.GenerateAsync(
                new StudioImageRequest(requestSettings, prompt, references, maskReference, referenceFiles, _count, requestMode),
                _cts.Token);
            stopwatch.Stop();

            _lastRawJson = result.RawJson;
            var metadata = CreateImageMetadata(
                requestSettings,
                prompt,
                requestMode,
                _count,
                references.Count + referenceFiles.Length,
                !string.IsNullOrWhiteSpace(maskReference),
                stopwatch.ElapsedMilliseconds);
            var durableImages = await PersistGeneratedImagesAsync(result.Images, metadata, _cts.Token);
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "assistant",
                Content = durableImages.Count == 0 ? "接口调用成功，但响应里没有解析到图片。" : "图片生成完成。",
                Images = durableImages.ToList(),
            });
            completed = true;
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "system",
                Content = "本次生成已取消。",
            });
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "system",
                Content = $"生成失败：{ex.Message}",
            });
        }
        finally
        {
            _loading = false;
            if (completed)
            {
                ClearSenderAttachments();
            }
            TouchActiveSession();
            await SaveAsync();
            StateHasChanged();
        }
    }

    private async Task SendFromSender(XSenderRequest request)
    {
        var text = (request.Text ?? string.Empty).Trim();
        if (_loading || (text.Length == 0 && !HasImageInputs()))
        {
            return;
        }

        await HandleUserTextAsync(text);
    }

    private async Task HandleUserTextAsync(string text)
    {
        if (!AccountReady)
        {
            _error = "请先登录账户。";
            _settingsOpen = false;
            SetSonnetMessage("登录后会自动完成作图配置。", isError: false);
            return;
        }

        _error = null;
        ClearDownloadNotice();
        _pendingPrompt = null;
        RemovePendingPromptMessages();

        if (TryCreateLatestImageRevision(text, out var revision))
        {
            await GenerateImageRevisionAsync(revision.ImageUrl, revision.Prompt, BuildUserMessageContent(text, "edit"));
            return;
        }

        var requestMode = ResolveRequestMode(text, ActiveSession.Mode);
        ApplyResolvedMode(requestMode);
        _loading = true;
        _loadingLabel = "正在理解需求";
        _senderText = string.Empty;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        ActiveSession.Messages.Add(new StudioMessage
        {
            Role = "user",
            Content = BuildUserMessageContent(text, requestMode),
        });
        TouchActiveSession(text);
        StateHasChanged();
        await SaveAsync();

        try
        {
            var intent = HasImageInputs()
                ? new PromptIntentResult(true, "用户已添加图片附件")
                : await ChatClient.AnalyzeIntentAsync(Settings, text, _cts.Token);
            if (intent.Image)
            {
                ActiveSession.Prompt = text;
                var polishMode = StudioSettings.NormalizePromptPolishMode(Settings.PromptPolishMode);
                if (polishMode == "auto")
                {
                    _pendingPrompt = new PendingPrompt(text, string.Empty);
                    _loading = false;
                    await GenerateWithPolish();
                    return;
                }

                if (polishMode == "ask")
                {
                    var message = new StudioMessage
                    {
                        Role = PromptConfirmRole,
                        Content = "要先帮你润色一下提示词，再生成图片吗？",
                    };
                    ActiveSession.Messages.Add(message);
                    _pendingPrompt = new PendingPrompt(text, message.Id);
                    TouchActiveSession(text);
                    return;
                }

                _pendingPrompt = new PendingPrompt(text, string.Empty);
                _loading = false;
                await GenerateWithoutPolish();
                return;
            }

            var reply = await ChatClient.ReplyAsync(Settings, ActiveSession.Messages.SkipLast(1).ToArray(), text, _cts.Token);
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "assistant",
                Content = string.IsNullOrWhiteSpace(reply) ? "我在。你可以直接告诉我想生成什么画面。" : reply,
            });
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "system",
                Content = "本次请求已取消。",
            });
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "system",
                Content = $"会话失败：{ex.Message}",
            });
        }
        finally
        {
            _loading = false;
            _loadingLabel = "正在请求图像接口";
            TouchActiveSession();
            await SaveAsync();
            StateHasChanged();
        }
    }

    private async Task GenerateWithPolish()
    {
        if (_pendingPrompt is null || _loading)
        {
            return;
        }

        var original = _pendingPrompt.Text;
        _loading = true;
        _loadingLabel = "正在润色提示词";
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        StateHasChanged();

        try
        {
            var polished = await ChatClient.PolishPromptAsync(Settings, original, _cts.Token);
            RemovePendingPromptMessages();
            _pendingPrompt = null;
            ActiveSession.Prompt = polished;
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "assistant",
                Content = $"已润色提示词：{polished}",
            });
            await SaveAsync();
            _loading = false;
            await GenerateAsync(polished, addUserMessage: false);
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
            _loading = false;
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "system",
                Content = "本次润色已取消。",
            });
        }
        catch (Exception ex)
        {
            _loading = false;
            _error = ex.Message;
            ActiveSession.Messages.Add(new StudioMessage
            {
                Role = "system",
                Content = $"润色失败：{ex.Message}",
            });
        }
        finally
        {
            _loadingLabel = "正在请求图像接口";
            TouchActiveSession();
            await SaveAsync();
            StateHasChanged();
        }
    }

    private async Task GenerateWithPolishAndRemember()
    {
        Settings.PromptPolishMode = "auto";
        await SaveAsync();
        await GenerateWithPolish();
    }

    private async Task GenerateWithoutPolish()
    {
        if (_pendingPrompt is null || _loading)
        {
            return;
        }

        var prompt = _pendingPrompt.Text;
        RemovePendingPromptMessages();
        _pendingPrompt = null;
        ActiveSession.Prompt = prompt;
        await GenerateAsync(prompt, addUserMessage: false);
    }

    private async Task GenerateWithoutPolishAndRemember()
    {
        Settings.PromptPolishMode = "direct";
        await SaveAsync();
        await GenerateWithoutPolish();
    }

    private void RemovePendingPromptMessages()
    {
        ActiveSession.Messages.RemoveAll(message => message.Role == PromptConfirmRole);
    }

    private void Cancel()
    {
        _cts?.Cancel();
    }

    private async Task DownloadImage(GeneratedImage image)
    {
        if (string.IsNullOrWhiteSpace(image.Url))
        {
            ClearDownloadNotice();
            _error = "没有可下载的图片地址。";
            return;
        }

        var fileName = BuildDownloadFileName(image);
        try
        {
            var result = await JsRuntime.InvokeAsync<ImageDownloadResult>("sonnetArt.download", image.Url, fileName);
            _downloadNotice = BuildDownloadNotice(result);
            _error = null;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException)
        {
            ClearDownloadNotice();
            _error = $"下载失败：{TrimJsError(ex.Message)}";
        }
    }

    private Task DownloadPreviewImage()
    {
        return _previewImage is null ? Task.CompletedTask : DownloadImage(_previewImage);
    }

    private void OpenImagePreview(GeneratedImage image, string alt)
    {
        if (string.IsNullOrWhiteSpace(image.Url))
        {
            return;
        }

        _previewImage = image;
        _previewAlt = alt;
        _previewZoomed = false;
        ResetImagePreviewEditState();
        ClearDownloadNotice();
    }

    private void CloseImagePreview()
    {
        _previewImage = null;
        _previewAlt = string.Empty;
        _previewZoomed = false;
        ResetImagePreviewEditState();
    }

    private void ToggleImagePreviewZoom()
    {
        _previewZoomed = !_previewZoomed;
        if (_previewEditing)
        {
            _previewMaskAttachPending = true;
            StateHasChanged();
        }
    }

    private async Task GenerateMatrixAsync()
    {
        if (_loading)
        {
            return;
        }

        var prompt = (ActiveSession.Prompt ?? string.Empty).Trim();
        var requestMode = ResolveRequestMode(prompt, ActiveSession.Mode);
        if (prompt.Length == 0 && requestMode != "variation")
        {
            _error = "请先填写提示词。";
            return;
        }

        var runs = MatrixRuns.ToArray();
        if (runs.Length == 0)
        {
            _error = "请选择至少一个分辨率和一个质量。";
            return;
        }

        var originalResolutionTier = Settings.ResolutionTier;
        var originalQuality = Settings.Quality;
        var originalCount = _count;
        var originalSize = Settings.Size;
        var originalImageReferences = ActiveSession.ImageReferences;
        var originalMaskReference = ActiveSession.MaskReference;
        var originalReferenceFiles = _referenceFiles.ToArray();
        var originalSenderImageAttachments = _senderImageAttachments.ToArray();

        _error = null;
        _matrixPanelOpen = false;
        _count = 1;
        for (var index = 0; index < runs.Length; index++)
        {
            var run = runs[index];
            if (_loading)
            {
                break;
            }

            Settings.ResolutionTier = run.ResolutionTier;
            Settings.Quality = run.Quality;
            Settings.Size = run.Size;
            ActiveSession.ImageReferences = originalImageReferences;
            ActiveSession.MaskReference = originalMaskReference;
            _referenceFiles.Clear();
            _referenceFiles.AddRange(originalReferenceFiles);
            _senderImageAttachments.Clear();
            _senderImageAttachments.AddRange(originalSenderImageAttachments);
            SyncSenderAttachments();
            await SaveAsync();
            await GenerateAsync(
                prompt,
                addUserMessage: index == 0,
                loadingLabel: $"矩阵对比 {index + 1}/{runs.Length} · {run.Size} · {QualityOptionLabel(run.Quality)}");
            if (_cts?.IsCancellationRequested == true || !string.IsNullOrWhiteSpace(_error))
            {
                break;
            }
        }

        Settings.ResolutionTier = originalResolutionTier;
        Settings.Quality = originalQuality;
        Settings.Size = originalSize;
        _count = originalCount;
        ActiveSession.ImageReferences = originalImageReferences;
        ActiveSession.MaskReference = originalMaskReference;
        _referenceFiles.Clear();
        _referenceFiles.AddRange(originalReferenceFiles);
        _senderImageAttachments.Clear();
        _senderImageAttachments.AddRange(originalSenderImageAttachments);
        SyncSenderAttachments();
        CoerceImageSettingsForCurrentModel();
        TouchActiveSession();
        await SaveAsync();
    }

    private async Task ToggleImagePreviewEdit()
    {
        if (_previewImage is null)
        {
            return;
        }

        _previewEditing = !_previewEditing;
        _previewZoomed = false;
        _previewEditError = null;

        if (_previewEditing)
        {
            _previewMaskAttachPending = true;
            StateHasChanged();
        }
    }

    private async Task OnPreviewImageLoaded()
    {
        if (_previewEditing)
        {
            await ResizePreviewMaskAsync();
        }
    }

    private async Task UpdatePreviewBrushSize(ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString(), out var value))
        {
            _previewBrushSize = Math.Clamp(value, 12, 96);
            if (_previewEditing)
            {
                await InvokePreviewCanvasVoidAsync("setBrushSize", _previewBrushSize);
            }
        }
    }

    private void UpdatePreviewEditPrompt(ChangeEventArgs args)
    {
        _previewEditPrompt = args.Value?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(_previewEditPrompt))
        {
            _previewEditError = null;
        }
    }

    private async Task ClearPreviewMask()
    {
        _previewEditError = null;
        await InvokePreviewCanvasVoidAsync("clearMask");
    }

    private async Task SubmitPreviewEdit()
    {
        if (_previewImage is null || _loading)
        {
            return;
        }

        var prompt = _previewEditPrompt.Trim();
        if (prompt.Length == 0)
        {
            _previewEditError = "先写要修改什么。";
            return;
        }

        string? maskDataUrl = null;
        try
        {
            maskDataUrl = await JsRuntime.InvokeAsync<string?>("sonnetArt.previewEditor.exportMask", _previewMaskCanvas);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException)
        {
            _previewEditError = $"无法读取标注区域：{TrimJsError(ex.Message)}";
            return;
        }

        if (string.IsNullOrWhiteSpace(maskDataUrl))
        {
            _previewEditError = "先在图片上标注要修改的区域。";
            return;
        }

        var imageUrl = _previewImage.Url;
        var editPrompt = ImageEditPromptBuilder.BuildMaskedRevisionPrompt(prompt, ActiveSession.Prompt);
        var userContent = ImageEditPromptBuilder.BuildMaskedRevisionUserMessage(prompt);

        CloseImagePreview();
        await GenerateImageRevisionAsync(imageUrl, editPrompt, userContent, maskDataUrl);
    }

    private async Task GenerateImageRevisionAsync(string imageUrl, string prompt, string userContent, string? maskDataUrl = null)
    {
        if (string.IsNullOrWhiteSpace(imageUrl) || _loading)
        {
            return;
        }

        ClearAllReferenceInputs();
        ActiveSession.ImageReferences = imageUrl;
        ActiveSession.MaskReference = maskDataUrl ?? string.Empty;
        ActiveSession.Prompt = prompt;
        ApplyResolvedMode("edit");
        var revisionMode = ResolveRequestMode(prompt, ActiveSession.Mode);
        ApplyResolvedMode(revisionMode);
        _senderText = string.Empty;
        ActiveSession.Messages.Add(new StudioMessage
        {
            Role = "user",
            Content = userContent,
        });
        TouchActiveSession(userContent);
        StateHasChanged();
        await SaveAsync();

        try
        {
            await GenerateAsync(prompt, addUserMessage: false);
        }
        finally
        {
            if (string.Equals(ActiveSession.ImageReferences, imageUrl, StringComparison.Ordinal) &&
                string.Equals(ActiveSession.MaskReference, maskDataUrl ?? string.Empty, StringComparison.Ordinal))
            {
                ActiveSession.ImageReferences = string.Empty;
                ActiveSession.MaskReference = string.Empty;
                ApplyResolvedMode("generate");
                TouchActiveSession();
                await SaveAsync();
                StateHasChanged();
            }
        }
    }

    private async Task ResizePreviewMaskAsync()
    {
        if (!_previewEditing)
        {
            return;
        }

        await InvokePreviewCanvasVoidAsync("attach", _previewMaskCanvas, _previewImageElement, _previewBrushSize);
    }

    private async Task InvokePreviewCanvasVoidAsync(string command, params object?[] args)
    {
        try
        {
            var jsArgs = string.Equals(command, "attach", StringComparison.Ordinal)
                ? args
                : new object?[] { _previewMaskCanvas }.Concat(args).ToArray();
            await JsRuntime.InvokeVoidAsync($"sonnetArt.previewEditor.{command}", jsArgs);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException)
        {
            _previewEditError = $"标注工具不可用：{TrimJsError(ex.Message)}";
        }
    }

    private void ResetImagePreviewEditState()
    {
        _previewEditing = false;
        _previewMaskAttachPending = false;
        _previewEditPrompt = string.Empty;
        _previewEditError = null;
    }

    private string BuildDownloadFileName(GeneratedImage image)
    {
        var extension = ExtensionFromImage(image);
        return $"sonnetart-image-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.{extension}";
    }

    private string ExtensionFromImage(GeneratedImage image)
    {
        var contentType = image.MimeType;
        if (string.IsNullOrWhiteSpace(contentType) &&
            image.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var metadataEnd = image.Url.IndexOfAny([';', ',']);
            if (metadataEnd > 5)
            {
                contentType = image.Url[5..metadataEnd];
            }
        }

        var extension = contentType?.Trim().ToLowerInvariant() switch
        {
            "image/jpeg" => "jpg",
            "image/webp" => "webp",
            "image/gif" => "gif",
            "image/svg+xml" => "svg",
            "image/bmp" => "bmp",
            "image/png" => "png",
            _ => Settings.Format,
        };

        extension = extension.Trim().TrimStart('.').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(extension) ? "png" : extension;
    }

    private static string? BuildDownloadNotice(ImageDownloadResult? result)
    {
        if (result?.SavedLocally == true)
        {
            return string.IsNullOrWhiteSpace(result.FilePath)
                ? "图片已保存到下载目录。"
                : $"图片已保存：{result.FilePath}";
        }

        return "已开始下载图片。";
    }

    private void ClearDownloadNotice()
    {
        _downloadNotice = null;
    }

    private static string TrimJsError(string message)
    {
        const string errorPrefix = "Error: ";
        return message.StartsWith(errorPrefix, StringComparison.Ordinal)
            ? message[errorPrefix.Length..]
            : message;
    }

    private void CloseWindowAsync()
    {
        _exitConfirmOpen = true;
    }

    private void CancelExit()
    {
        _exitConfirmOpen = false;
    }

    private async Task ConfirmExitAsync()
    {
        _exitConfirmOpen = false;
        await InvokeWindowCommandAsync("exit");
    }

    private async Task InvokeWindowCommandAsync(string command)
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("sonnetArt.window.invoke", command);
        }
        catch (JSException)
        {
        }
    }

    private ValueTask<bool> BeforeSenderUpload(IBrowserFile file)
    {
        var accepted = IsBrowserImageFile(file);
        if (!accepted)
        {
            _error = "目前只支持图片附件。";
        }

        return ValueTask.FromResult(accepted);
    }

    private async Task AddSenderFiles(IReadOnlyList<IBrowserFile> files)
    {
        const long maxFileSize = 12 * 1024 * 1024;
        _error = null;

        foreach (var file in files.Where(IsBrowserImageFile).Take(Math.Max(0, 16 - _senderImageAttachments.Count)))
        {
            await using var stream = file.OpenReadStream(maxFileSize);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var content = memory.ToArray();
            var reference = new ImageReferenceFile(file.Name, file.ContentType, content);
            var attachment = new XAttachmentItem
            {
                Name = file.Name,
                Description = "图片附件",
                ContentType = file.ContentType,
                Size = file.Size,
                ImageUrl = ToDataUrl(reference),
                Status = XFileCardStatus.Done,
            };

            _senderImageAttachments.Add(new SenderImageAttachment(attachment.Id, reference, attachment));
        }

        SyncSenderAttachments();
        SyncReferenceFilesFromSenderAttachments();
        ApplyResolvedMode(ResolveRequestMode(_senderText ?? string.Empty, ActiveSession.Mode));
        TouchActiveSession();
        await SaveAsync();
    }

    private async Task RemoveSenderAttachment(string id)
    {
        _senderImageAttachments.RemoveAll(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        SyncSenderAttachments();
        SyncReferenceFilesFromSenderAttachments();
        ApplyResolvedMode(ResolveRequestMode(_senderText ?? string.Empty, ActiveSession.Mode));
        TouchActiveSession();
        await SaveAsync();
    }

    private void SyncSenderAttachments()
    {
        _senderAttachments = _senderImageAttachments.Select(item => item.Attachment).ToArray();
    }

    private void SyncReferenceFilesFromSenderAttachments()
    {
        if (_senderImageAttachments.Count == 0)
        {
            if (_referenceFiles.Count > 0 && ActiveSession.ImageReferences.Contains("local:", StringComparison.OrdinalIgnoreCase))
            {
                _referenceFiles.Clear();
                ActiveSession.ImageReferences = string.Empty;
            }

            return;
        }

        _referenceFiles.Clear();
        _referenceFiles.AddRange(_senderImageAttachments.Select(item => item.ReferenceFile));
        ActiveSession.ImageReferences = string.Join('\n', _referenceFiles.Select(file => $"local:{file.FileName}"));
    }

    private void ClearSenderAttachments()
    {
        _senderImageAttachments.Clear();
        SyncSenderAttachments();
        if (_referenceFiles.Count > 0 && ActiveSession.ImageReferences.Contains("local:", StringComparison.OrdinalIgnoreCase))
        {
            _referenceFiles.Clear();
            ActiveSession.ImageReferences = string.Empty;
        }
    }

    private void ClearAllReferenceInputs()
    {
        _referenceFiles.Clear();
        _senderImageAttachments.Clear();
        SyncSenderAttachments();
    }

    private static string ToDataUrl(ImageReferenceFile file)
    {
        var mimeType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType.Trim();
        return $"data:{mimeType};base64,{Convert.ToBase64String(file.Content)}";
    }

    private async Task<IReadOnlyList<GeneratedImage>> PersistGeneratedImagesAsync(
        IReadOnlyList<GeneratedImage> images,
        ImageGenerationMetadata metadata,
        CancellationToken cancellationToken)
    {
        var persisted = new List<GeneratedImage>(images.Count);
        for (var index = 0; index < images.Count; index++)
        {
            var image = images[index];
            persisted.Add(new GeneratedImage
            {
                Id = Guid.NewGuid().ToString("N"),
                Url = await PersistImageUrlAsync(image.Url, cancellationToken),
                RevisedPrompt = image.RevisedPrompt,
                MimeType = image.MimeType,
                CreatedAt = metadata.CreatedAt,
                Status = "generated",
                Prompt = metadata.Prompt,
                RequestPrompt = metadata.Prompt,
                Mode = metadata.Mode,
                Model = metadata.Model,
                Size = metadata.Size,
                AspectRatio = metadata.AspectRatio,
                ResolutionTier = metadata.ResolutionTier,
                Quality = metadata.Quality,
                OutputFormat = metadata.OutputFormat,
                Background = metadata.Background,
                Moderation = metadata.Moderation,
                RequestMode = metadata.RequestMode,
                RequestCount = metadata.RequestCount,
                BatchIndex = index + 1,
                ReferenceCount = metadata.ReferenceCount,
                ReferenceRole = metadata.ReferenceRole,
                HasMask = metadata.HasMask,
                DurationMs = metadata.DurationMs,
                EstimatedCost = metadata.EstimatedCost,
                EstimatedDuration = metadata.EstimatedDuration,
                RequestSummary = metadata.RequestSummary,
            });
        }

        return persisted;
    }

    private ImageGenerationMetadata CreateImageMetadata(
        StudioSettings settings,
        string prompt,
        string requestMode,
        int count,
        int referenceCount,
        bool hasMask,
        long durationMs)
    {
        var estimatedCost = EstimateCostLabel(count, settings.ResolutionTier, settings.Quality);
        var estimatedDuration = EstimateDurationLabel(count, settings.ResolutionTier, requestMode);
        var capabilities = ImageModelCatalog.Get(settings.Model, requestMode);
        return new ImageGenerationMetadata(
            DateTimeOffset.Now,
            prompt,
            NormalizeMode(requestMode),
            settings.Model,
            settings.Size,
            StudioSettings.NormalizeAspectRatio(settings.AspectRatio),
            StudioSettings.NormalizeResolutionTier(settings.ResolutionTier),
            settings.Quality,
            capabilities.SupportsOutputFormat ? settings.Format : string.Empty,
            capabilities.SupportsBackground ? settings.Background : string.Empty,
            capabilities.SupportsModeration ? settings.Moderation : string.Empty,
            settings.RequestMode,
            Math.Clamp(count, 1, 8),
            referenceCount,
            StudioSnapshot.NormalizeReferenceRole(ActiveSession.ReferenceRole),
            hasMask,
            durationMs,
            estimatedCost,
            estimatedDuration,
            BuildRequestSummary(settings, requestMode, count, referenceCount, hasMask, estimatedCost, estimatedDuration));
    }

    private static string BuildRequestSummary(
        StudioSettings settings,
        string requestMode,
        int count,
        int referenceCount,
        bool hasMask,
        string estimatedCost,
        string estimatedDuration)
    {
        var parts = new List<string>
        {
            $"model={settings.Model}",
            $"mode={NormalizeMode(requestMode)}",
            $"size={settings.Size}",
            $"quality={settings.Quality}",
            $"n={Math.Clamp(count, 1, 8)}",
        };

        if (!string.IsNullOrWhiteSpace(settings.Format))
        {
            parts.Add($"format={settings.Format}");
        }

        if (referenceCount > 0)
        {
            parts.Add($"references={referenceCount}");
        }

        if (hasMask)
        {
            parts.Add("mask=true");
        }

        parts.Add(estimatedCost);
        parts.Add(estimatedDuration);
        return string.Join(" · ", parts);
    }

    private async Task<string> PersistImageUrlAsync(string url, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        return url;
    }

    private async Task NewSession()
    {
        var workspace = ActiveGraphicWorkspace;
        var session = new StudioSession
        {
            Title = $"新建作图 {workspace.Sessions.Count + 1}",
        };
        workspace.Sessions.Insert(0, session);
        workspace.ActiveSessionId = session.Id;
        TouchWorkspace(workspace);
        ClearDownloadNotice();
        ClearAllReferenceInputs();
        await SaveAsync();
    }

    private async Task ActivateSession(string key)
    {
        _cts?.Cancel();
        ClearAllReferenceInputs();
        var workspace = ActiveGraphicWorkspace;
        workspace.ActiveSessionId = key;
        TouchWorkspace(workspace);
        _pendingPrompt = null;
        EnsureActiveMode();
        _error = null;
        ClearDownloadNotice();
        await SaveAsync();
    }

    private async Task RenameSession(XConversationRenameRequest request)
    {
        var workspace = ActiveGraphicWorkspace;
        var session = workspace.Sessions.FirstOrDefault(item => item.Id == request.Key);
        if (session is null)
        {
            return;
        }

        session.Title = request.Title.Trim();
        TouchSession(session);
        TouchWorkspace(workspace);
        await SaveAsync();
    }

    private async Task DeleteSession(XConversationItem item)
    {
        var workspace = ActiveGraphicWorkspace;
        if (workspace.Sessions.Count <= 1)
        {
            ResetActive();
            await SaveAsync();
            return;
        }

        workspace.Sessions.RemoveAll(session => session.Id == item.Key);
        if (workspace.ActiveSessionId == item.Key)
        {
            workspace.ActiveSessionId = workspace.Sessions.OrderByDescending(session => session.UpdatedAt).First().Id;
        }

        TouchWorkspace(workspace);
        await SaveAsync();
    }

    private async Task ClearHistory()
    {
        var session = new StudioSession();
        var workspace = ActiveGraphicWorkspace;
        workspace.Sessions = [session];
        workspace.ActiveSessionId = session.Id;
        TouchWorkspace(workspace);
        _pendingPrompt = null;
        ClearDownloadNotice();
        ClearAllReferenceInputs();
        await SaveAsync();
    }

    private async Task ClearResults()
    {
        ActiveSession.Messages.RemoveAll(message => message.Images.Count > 0);
        ClearDownloadNotice();
        CloseImagePreview();
        TouchActiveSession();
        await SaveAsync();
    }

    private void ResetActive()
    {
        ActiveSession.Prompt = string.Empty;
        ActiveSession.ImageReferences = string.Empty;
        ActiveSession.Messages.Clear();
        _pendingPrompt = null;
        ClearDownloadNotice();
        CloseImagePreview();
        ClearAllReferenceInputs();
        TouchActiveSession("新建作图");
    }

    private async Task SelectWorkspace(string workspaceId)
    {
        var workspace = _snapshot.SetActiveWorkspace(workspaceId);
        if (workspace is null)
        {
            return;
        }

        ClearAllReferenceInputs();
        _pendingPrompt = null;
        _error = null;
        ClearDownloadNotice();
        EnsureActiveMode();
        await SaveAsync();
    }

    private async Task OpenWorkspace(string workspaceId)
    {
        var workspace = _snapshot.SetActiveWorkspace(workspaceId);
        if (workspace is null)
        {
            return;
        }

        await SaveAsync();
    }

    private Task NewWorkspace()
    {
        _workspaceCreateName = BuildDefaultWorkspaceName();
        _workspaceCreateOpen = true;
        return Task.CompletedTask;
    }

    private Task SetWorkspaceCreateName(string name)
    {
        _workspaceCreateName = name;
        return Task.CompletedTask;
    }

    private async Task CreateWorkspace()
    {
        var name = string.IsNullOrWhiteSpace(_workspaceCreateName)
            ? BuildDefaultWorkspaceName()
            : _workspaceCreateName.Trim();
        _snapshot.AddWorkspace(name);
        _workspaceCreateOpen = false;
        await SaveAsync();
    }

    private Task CloseWorkspaceCreate()
    {
        _workspaceCreateOpen = false;
        return Task.CompletedTask;
    }

    private string BuildDefaultWorkspaceName()
    {
        const string baseName = "平面设计";
        var count = _snapshot.Workspaces.Count;
        return count == 0 ? baseName : $"{baseName} {count + 1}";
    }

    private async Task UsePrompt(string prompt)
    {
        ActiveSession.Prompt = prompt;
        _senderText = prompt;
        _promptLibraryNotice = "已填入输入框";
        TouchActiveSession(prompt);
        await SaveAsync();
    }

    private Task UsePrompt(PromptLibrarySelection selection)
    {
        return UsePrompt(selection.Prompt);
    }

    private async Task LoadPromptLibraryPage(PromptLibraryQuery query)
    {
        var requestId = Interlocked.Increment(ref _promptLibraryRequestId);
        _promptLibraryLoading = true;
        try
        {
            var page = await PromptLibrary.LoadPageAsync(query);
            if (requestId == _promptLibraryRequestId)
            {
                _promptLibraryPage = page;
                _promptLibraryNotice = null;
            }
        }
        catch (Exception ex)
        {
            if (requestId == _promptLibraryRequestId)
            {
                _promptLibraryPage = PromptLibraryPage.Empty(query.PageSize);
                _promptLibraryNotice = $"提示词加载失败：{ex.Message}";
            }
        }
        finally
        {
            if (requestId == _promptLibraryRequestId)
            {
                _promptLibraryLoading = false;
            }
        }
    }

    private async Task CopyPrompt(PromptLibrarySelection selection)
    {
        await JsRuntime.InvokeVoidAsync("sonnetArt.copyText", selection.Prompt);
        _promptLibraryNotice = "已复制";
    }

    private bool MatchesGalleryFilter(GalleryImageItem item)
    {
        var image = item.Image;
        if (_galleryFavoritesOnly && !image.IsFavorite)
        {
            return false;
        }

        if (_gallerySessionFilter != "all" &&
            !string.Equals(item.Session.Id, _gallerySessionFilter, StringComparison.Ordinal))
        {
            return false;
        }

        if (_galleryModelFilter != "all" &&
            !string.Equals(NormalizeGalleryOption(image.Model, Settings.Model), _galleryModelFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_gallerySizeFilter != "all" &&
            !string.Equals(NormalizeGalleryOption(image.Size, EffectiveImageSize), _gallerySizeFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_galleryStatusFilter != "all" &&
            !string.Equals(GalleryImageStatus(image), _galleryStatusFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_galleryTagFilter != "all" &&
            !image.Tags.Any(tag => string.Equals(tag, _galleryTagFilter, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!MatchesGalleryDate(EffectiveImageCreatedAt(image, item.Message)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_gallerySearch))
        {
            var query = _gallerySearch.Trim();
            return ContainsIgnoreCase(item.Session.Title, query) ||
                ContainsIgnoreCase(item.Message.Content, query) ||
                ContainsIgnoreCase(image.Prompt, query) ||
                ContainsIgnoreCase(image.RevisedPrompt, query) ||
                ContainsIgnoreCase(image.RequestSummary, query) ||
                image.Tags.Any(tag => ContainsIgnoreCase(tag, query));
        }

        return true;
    }

    private bool MatchesGalleryDate(DateTimeOffset createdAt)
    {
        var localDate = createdAt.ToLocalTime().Date;
        var today = DateTimeOffset.Now.Date;
        return _galleryDateFilter switch
        {
            "today" => localDate == today,
            "week" => localDate >= today.AddDays(-6),
            "month" => localDate >= today.AddDays(-29),
            _ => true,
        };
    }

    private static bool ContainsIgnoreCase(string? value, string query)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void ToggleGallery()
    {
        _galleryOpen = !_galleryOpen;
    }

    private async Task UpdateGallerySearch(ChangeEventArgs args)
    {
        _gallerySearch = args.Value?.ToString() ?? string.Empty;
        await SaveAsync();
    }

    private async Task UpdateGallerySessionFilter(ChangeEventArgs args)
    {
        _gallerySessionFilter = args.Value?.ToString() ?? "all";
        await SaveAsync();
    }

    private async Task UpdateGalleryModelFilter(ChangeEventArgs args)
    {
        _galleryModelFilter = args.Value?.ToString() ?? "all";
        await SaveAsync();
    }

    private async Task UpdateGallerySizeFilter(ChangeEventArgs args)
    {
        _gallerySizeFilter = args.Value?.ToString() ?? "all";
        await SaveAsync();
    }

    private async Task UpdateGalleryStatusFilter(ChangeEventArgs args)
    {
        _galleryStatusFilter = args.Value?.ToString() ?? "all";
        await SaveAsync();
    }

    private async Task UpdateGalleryDateFilter(ChangeEventArgs args)
    {
        _galleryDateFilter = args.Value?.ToString() ?? "all";
        await SaveAsync();
    }

    private async Task UpdateGalleryTagFilter(ChangeEventArgs args)
    {
        _galleryTagFilter = args.Value?.ToString() ?? "all";
        await SaveAsync();
    }

    private async Task ToggleGalleryFavoritesOnly()
    {
        _galleryFavoritesOnly = !_galleryFavoritesOnly;
        await SaveAsync();
    }

    private void ToggleMatrixPanel()
    {
        _matrixPanelOpen = !_matrixPanelOpen;
    }

    private async Task ToggleMatrixResolution(string resolutionTier)
    {
        var normalized = StudioSettings.NormalizeResolutionTier(resolutionTier);
        if (!_selectedMatrixResolutionTiers.Remove(normalized))
        {
            _selectedMatrixResolutionTiers.Add(normalized);
        }

        await SaveAsync();
    }

    private async Task ToggleMatrixQuality(string quality)
    {
        var normalized = CurrentQualityOptions.FirstOrDefault(option => string.Equals(option, quality, StringComparison.OrdinalIgnoreCase))
            ?? "auto";
        if (!_selectedMatrixQualities.Remove(normalized))
        {
            _selectedMatrixQualities.Add(normalized);
        }

        await SaveAsync();
    }

    private string MatrixResolutionOptionClass(string resolutionTier)
    {
        return _selectedMatrixResolutionTiers.Contains(StudioSettings.NormalizeResolutionTier(resolutionTier))
            ? "matrix-chip active"
            : "matrix-chip";
    }

    private string MatrixQualityOptionClass(string quality)
    {
        return _selectedMatrixQualities.Contains(quality)
            ? "matrix-chip active"
            : "matrix-chip";
    }

    private async Task ToggleFavorite(GeneratedImage image)
    {
        image.IsFavorite = !image.IsFavorite;
        image.Status = image.IsFavorite ? "favorite" : "generated";
        TouchActiveSession();
        await SaveAsync();
    }

    private async Task SetImageTags(GeneratedImage image, ChangeEventArgs args)
    {
        image.Tags = (args.Value?.ToString() ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        TouchActiveSession();
        await SaveAsync();
    }

    private async Task UseImageAsReference(GeneratedImage? image, string role = "auto")
    {
        if (image is null || string.IsNullOrWhiteSpace(image.Url))
        {
            return;
        }

        ClearAllReferenceInputs();
        ActiveSession.ImageReferences = image.Url;
        ActiveSession.MaskReference = string.Empty;
        ActiveSession.ReferenceRole = StudioSnapshot.NormalizeReferenceRole(role);
        _senderText = BuildReferenceRolePromptPrefix(ActiveSession.ReferenceRole, image.Prompt);
        ApplyResolvedMode("image");
        image.Status = "reference";
        CloseImagePreview();
        TouchActiveSession(_senderText);
        await SaveAsync();
    }

    private async Task StartImageEdit(GeneratedImage? image)
    {
        if (image is null || string.IsNullOrWhiteSpace(image.Url))
        {
            return;
        }

        ClearAllReferenceInputs();
        ActiveSession.ImageReferences = image.Url;
        ActiveSession.MaskReference = string.Empty;
        ActiveSession.ReferenceRole = "content";
        _senderText = "编辑这张图：";
        ApplyResolvedMode("edit");
        image.Status = "reference";
        CloseImagePreview();
        TouchActiveSession(_senderText);
        await SaveAsync();
    }

    private async Task GenerateVariationFromImage(GeneratedImage? image)
    {
        if (image is null || string.IsNullOrWhiteSpace(image.Url) || _loading)
        {
            return;
        }

        ClearAllReferenceInputs();
        ActiveSession.ImageReferences = image.Url;
        ActiveSession.MaskReference = string.Empty;
        ActiveSession.ReferenceRole = "content";
        ActiveSession.Prompt = string.Empty;
        ApplyResolvedMode("variation");
        image.Status = "reference";
        CloseImagePreview();
        TouchActiveSession("变化版本");
        await SaveAsync();
        await GenerateAsync(string.Empty, addUserMessage: true);
    }

    private async Task RegenerateFromImage(GeneratedImage? image)
    {
        if (image is null)
        {
            return;
        }

        var prompt = !string.IsNullOrWhiteSpace(image.RequestPrompt) ? image.RequestPrompt : image.Prompt;
        if (string.IsNullOrWhiteSpace(prompt) || _loading)
        {
            return;
        }

        ApplyImageSettings(image);
        ActiveSession.Prompt = prompt;
        ApplyResolvedMode(string.IsNullOrWhiteSpace(image.Mode) ? "generate" : image.Mode);
        image.Status = "regenerated";
        CloseImagePreview();
        TouchActiveSession(prompt);
        await SaveAsync();
        await GenerateAsync(prompt, addUserMessage: true);
    }

    private async Task OutpaintImage(GeneratedImage? image, string direction)
    {
        if (image is null || string.IsNullOrWhiteSpace(image.Url) || _loading)
        {
            return;
        }

        var previousPrompt = !string.IsNullOrWhiteSpace(image.RequestPrompt) ? image.RequestPrompt : image.Prompt;
        var prompt = ImageEditPromptBuilder.BuildOutpaintPrompt(direction, previousPrompt);
        var userContent = ImageEditPromptBuilder.BuildOutpaintUserMessage(direction, ResolveOutpaintTargetSize(image, direction));

        ApplyImageSettings(image);
        ApplyOutpaintAspectRatio(direction);
        image.Status = "reference";
        CloseImagePreview();
        await GenerateImageRevisionAsync(image.Url, prompt, userContent);
    }

    private async Task CopyImagePrompt(GeneratedImage? image)
    {
        if (image is null)
        {
            return;
        }

        var prompt = !string.IsNullOrWhiteSpace(image.RequestPrompt) ? image.RequestPrompt : image.Prompt;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = image.RevisedPrompt ?? string.Empty;
        }

        await JsRuntime.InvokeVoidAsync("sonnetArt.copyText", prompt);
        _downloadNotice = "提示词已复制。";
    }

    private async Task CopyImageParameters(GeneratedImage? image)
    {
        if (image is null)
        {
            return;
        }

        var payload = new
        {
            prompt = !string.IsNullOrWhiteSpace(image.RequestPrompt) ? image.RequestPrompt : image.Prompt,
            revisedPrompt = image.RevisedPrompt,
            model = image.Model,
            mode = image.Mode,
            size = image.Size,
            aspectRatio = image.AspectRatio,
            resolutionTier = image.ResolutionTier,
            quality = image.Quality,
            outputFormat = image.OutputFormat,
            background = image.Background,
            moderation = image.Moderation,
            requestMode = image.RequestMode,
            referenceCount = image.ReferenceCount,
            referenceRole = image.ReferenceRole,
            hasMask = image.HasMask,
            estimatedCost = image.EstimatedCost,
            durationMs = image.DurationMs,
        };
        await JsRuntime.InvokeVoidAsync("sonnetArt.copyText", JsonSerializer.Serialize(payload, ClipboardJsonOptions));
        _downloadNotice = "参数已复制。";
    }

    private void ApplyImageSettings(GeneratedImage image)
    {
        Settings.Model = ImageModelCatalog.NormalizeModel(image.Model);
        Settings.AspectRatio = StudioSettings.NormalizeAspectRatio(image.AspectRatio);
        Settings.ResolutionTier = StudioSettings.NormalizeResolutionTier(image.ResolutionTier);
        Settings.Size = string.IsNullOrWhiteSpace(image.Size)
            ? BuildImageSize(Settings.AspectRatio, Settings.ResolutionTier)
            : image.Size;
        if (!string.IsNullOrWhiteSpace(image.Quality))
        {
            Settings.Quality = image.Quality;
        }

        if (!string.IsNullOrWhiteSpace(image.OutputFormat))
        {
            Settings.Format = image.OutputFormat;
        }

        if (!string.IsNullOrWhiteSpace(image.Background))
        {
            Settings.Background = image.Background;
        }

        if (!string.IsNullOrWhiteSpace(image.Moderation))
        {
            Settings.Moderation = image.Moderation;
        }

        CoerceImageSettingsForCurrentModel();
    }

    private void ApplyOutpaintAspectRatio(string direction)
    {
        Settings.AspectRatio = ResolveOutpaintAspectRatio(EffectiveAspectRatio, direction);
        Settings.ResolutionTier = StudioSettings.NormalizeResolutionTier(Settings.ResolutionTier);
        Settings.Size = BuildImageSize(Settings.AspectRatio, Settings.ResolutionTier);
        CoerceImageSettingsForCurrentModel();
    }

    private static string ResolveOutpaintTargetSize(GeneratedImage image, string direction)
    {
        var sourceRatio = ResolveAspectRatio(image.AspectRatio, image.Size);
        var sourceTier = StudioSettings.NormalizeResolutionTier(image.ResolutionTier);
        var targetRatio = ResolveOutpaintAspectRatio(sourceRatio, direction);
        return BuildImageSize(targetRatio, sourceTier);
    }

    private static string ResolveOutpaintAspectRatio(string sourceRatio, string direction)
    {
        var normalizedDirection = direction?.Trim().ToLowerInvariant();
        var horizontal = normalizedDirection is "left" or "right" or "continue";
        var vertical = normalizedDirection is "up" or "down";
        return (StudioSettings.NormalizeAspectRatio(sourceRatio), horizontal, vertical) switch
        {
            ("1:1", false, false) => "16:9",
            ("3:4", false, false) => "4:3",
            ("2:3", false, false) => "3:2",
            ("9:16", false, false) => "16:9",
            ("3:2", false, false) => "21:9",
            ("4:3", false, false) => "16:9",
            ("16:9", false, false) => "21:9",
            ("21:9", false, false) => "16:9",
            ("1:1", true, _) => "16:9",
            ("1:1", _, true) => "9:16",
            ("3:4", true, _) => "4:3",
            ("2:3", true, _) => "3:2",
            ("9:16", true, _) => "16:9",
            ("3:2", _, true) => "2:3",
            ("4:3", _, true) => "3:4",
            ("16:9", _, true) => "9:16",
            ("21:9", _, true) => "16:9",
            ("3:4", _, true) => "9:16",
            ("2:3", _, true) => "9:16",
            ("3:2", true, _) => "21:9",
            ("4:3", true, _) => "16:9",
            ("16:9", true, _) => "21:9",
            _ => horizontal ? "16:9" : vertical ? "9:16" : sourceRatio,
        };
    }

    private static string BuildReferenceRolePromptPrefix(string role, string previousPrompt)
    {
        var prefix = StudioSnapshot.NormalizeReferenceRole(role) switch
        {
            "content" => "参考这张图的主体、构图和场景，生成：",
            "style" => "参考这张图的色彩、光影、材质和风格，生成：",
            "character" => "保持这张图里人物身份、脸部特征和服装一致，生成：",
            "product" => "保持这张图里产品外观、比例、材质和关键细节一致，生成：",
            _ => "参考这张图，生成：",
        };

        return string.IsNullOrWhiteSpace(previousPrompt) ? prefix : $"{prefix}{previousPrompt.Trim()}";
    }

    private IReadOnlyList<string> ParseReferences()
    {
        return ActiveSession.ImageReferences
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(reference => !reference.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private string ResolveRequestMode(string prompt, string currentMode)
    {
        var normalizedMode = NormalizeMode(currentMode);
        if (!HasImageInputs())
        {
            return normalizedMode;
        }

        if (LooksLikeEditIntent(prompt) || !string.IsNullOrWhiteSpace(ActiveSession.MaskReference))
        {
            return "edit";
        }

        if (normalizedMode == "variation")
        {
            return string.IsNullOrWhiteSpace(prompt) ? "variation" : "image";
        }

        return normalizedMode == "edit" ? "edit" : "image";
    }

    private void ApplyResolvedMode(string mode)
    {
        mode = NormalizeMode(mode);
        ActiveSession.Mode = mode;
        if (mode == "variation")
        {
            Settings.Model = "gpt-image-2";
            Settings.RequestMode = "sync";
            if (_referenceFiles.Count > 1)
            {
                var first = _referenceFiles[0];
                _referenceFiles.Clear();
                _referenceFiles.Add(first);
                ActiveSession.ImageReferences = $"local:{first.FileName}";
            }

            if (_senderImageAttachments.Count > 1)
            {
                var firstAttachment = _senderImageAttachments[0];
                _senderImageAttachments.Clear();
                _senderImageAttachments.Add(firstAttachment);
                SyncSenderAttachments();
            }
        }

        CoerceImageSettingsForCurrentModel();
    }

    private bool HasImageInputs()
    {
        return _referenceFiles.Count > 0 ||
            ParseReferences().Count > 0;
    }

    private static string NormalizeMode(string? mode)
    {
        return mode switch
        {
            "image" => "image",
            "edit" => "edit",
            "variation" => "variation",
            _ => "generate",
        };
    }

    private static bool LooksLikeEditIntent(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return ContainsAny(text,
            "编辑", "修改", "改一下", "改成", "替换", "换成", "换背景", "去掉", "移除", "擦除",
            "修复", "不对劲", "不自然", "有问题", "变形", "畸形", "补全", "扩图", "局部", "遮罩",
            "mask", "edit", "remove", "replace", "inpaint", "outpaint");
    }

    private bool TryCreateLatestImageRevision(string text, out ImageRevisionRequest revision)
    {
        revision = default!;
        if (!LooksLikeImageRevisionFeedback(text))
        {
            return false;
        }

        var image = ActiveSession.Messages
            .LastOrDefault(message => message.Images.Count > 0)?
            .Images
            .LastOrDefault();
        if (image is null || string.IsNullOrWhiteSpace(image.Url))
        {
            return false;
        }

        revision = new ImageRevisionRequest(image.Url, BuildImageRevisionPrompt(text, ActiveSession.Prompt));
        return true;
    }

    private static bool LooksLikeImageRevisionFeedback(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (ContainsAny(text,
            "不对劲", "明显不对", "不对", "有问题", "不自然", "怪", "很怪", "奇怪", "错误", "错了",
            "畸形", "变形", "扭曲", "崩", "糊了", "模糊", "穿帮", "多指", "少指", "断指", "粘连"))
        {
            return true;
        }

        return ContainsAny(text, "手", "手指", "手掌", "胳膊", "手臂", "脸", "眼睛", "腿", "脚", "身体", "姿势") &&
            ContainsAny(text, "修", "修复", "改", "改一下", "调整", "重新", "再来", "重画", "优化", "不好", "不行");
    }

    private static string BuildImageRevisionPrompt(string feedback, string previousPrompt)
    {
        var trimmedFeedback = feedback.Trim();
        var priorPromptSection = string.IsNullOrWhiteSpace(previousPrompt)
            ? string.Empty
            : $"""

            上一轮提示词：{previousPrompt.Trim()}
            """;

        return $"""
            请以上一张图作为参考，重新生成整张图。保留原图的主体身份、脸部特征、服装、姿势、背景、灯光、构图、镜头语言和整体风格，只修正用户指出的问题。

            用户反馈：{trimmedFeedback}
            {priorPromptSection}

            正向提示词：重点修复画面中不自然或错误的部位，尤其是手部结构；手指数量正确，手掌比例合理，关节清晰，手腕与手臂连接自然，皮肤纹理、肤色和光影与原图一致。保持人物完整、边缘干净、细节清晰、画面自然真实。

            负面提示词：畸形手，多指，少指，断指，粘连手指，扭曲手掌，错误关节，手指过长，手指过短，手腕断裂，不自然姿势，模糊手部，脸部变形，五官错位，身体比例错误，肢体扭曲，改变人物身份，改变服装，改变背景，过度修饰，低质量，变形，失真。
            """;
    }

    private static bool ContainsAny(string text, params string[] tokens)
    {
        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBrowserImageFile(IBrowserFile file)
    {
        if (!string.IsNullOrWhiteSpace(file.ContentType) &&
            file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = file.Name;
        return name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildUserMessageContent(string text, string requestMode)
    {
        var content = string.IsNullOrWhiteSpace(text) ? ModeName(requestMode) : text;
        var inputCount = _referenceFiles.Count + ParseReferences().Count;
        return HasImageInputs()
            ? $"{content}\n\n已添加 {inputCount} 张图片附件，模式：{ModeName(requestMode)}。"
            : content;
    }

    private StudioSettings CreateRequestSettings()
    {
        CoerceImageSettingsForCurrentModel();
        Settings.AspectRatio = EffectiveAspectRatio;
        Settings.ResolutionTier = StudioSettings.NormalizeResolutionTier(Settings.ResolutionTier);
        Settings.Size = BuildImageSize(Settings.AspectRatio, Settings.ResolutionTier);
        ImageModelCatalog.NormalizeSettings(Settings, ActiveSession.Mode);
        return Settings;
    }

    private void TouchActiveSession(string? titleSeed = null)
    {
        TouchSession(ActiveSession, titleSeed);
    }

    private static void TouchSession(StudioSession session, string? titleSeed = null)
    {
        session.UpdatedAt = DateTimeOffset.Now;
        if (!string.IsNullOrWhiteSpace(titleSeed))
        {
            session.Title = titleSeed.Length > 24 ? titleSeed[..24] + "..." : titleSeed;
        }
    }

    private static void TouchWorkspace(StudioWorkspace workspace)
    {
        var now = DateTimeOffset.Now;
        workspace.UpdatedAt = now;
        workspace.LastOpenedAt = now;
    }

    private async Task SaveAsync()
    {
        _snapshot.Normalize();
        await Storage.SaveAsync(_snapshot);
    }

    private async Task SyncDocumentThemeAsync()
    {
        var effectiveTheme = EffectiveTheme;
        if (string.Equals(_lastDocumentTheme, effectiveTheme, StringComparison.Ordinal))
        {
            return;
        }

        _lastDocumentTheme = effectiveTheme;
        try
        {
            await JsRuntime.InvokeVoidAsync("sonnetArt.setDocumentTheme", effectiveTheme);
        }
        catch (JSException)
        {
        }
    }

    private void OpenSettings() => _settingsOpen = true;
    private void CloseSettings() => _settingsOpen = false;

    private void ToggleSonnetRegister()
    {
        _sonnetRegisterOpen = !_sonnetRegisterOpen;
    }

    private void SetAuthMode(bool register)
    {
        _sonnetRegisterOpen = register;
        _sonnetMessage = null;
        _sonnetMessageIsError = false;
    }

    private async Task LoginSonnet()
    {
        await RunSonnetAction(async () =>
        {
            var response = await SonnetClient.LoginAsync(Settings, _sonnetEmail, _sonnetPassword);
            await CompleteAccountSetupAsync();
            await SaveAsync();
            _sonnetPassword = string.Empty;
            _settingsOpen = false;
            SetSonnetMessage($"已登录 {response.User?.Email ?? _sonnetEmail}。");
            await NavigateToLastWorkspaceIfNeededAsync();
        });
    }

    private async Task RegisterSonnet()
    {
        await RunSonnetAction(async () =>
        {
            var response = await SonnetClient.RegisterAsync(
                Settings,
                _sonnetEmail,
                _sonnetPassword,
                _sonnetPromoCode,
                _sonnetInvitationCode,
                _sonnetAffiliateCode);
            await CompleteAccountSetupAsync();
            await SaveAsync();
            _sonnetPassword = string.Empty;
            _sonnetRegisterOpen = false;
            _settingsOpen = false;
            SetSonnetMessage($"已注册并登录 {response.User?.Email ?? _sonnetEmail}。");
            await NavigateToLastWorkspaceIfNeededAsync();
        });
    }

    private Task SubmitAuth()
    {
        return _sonnetRegisterOpen ? RegisterSonnet() : LoginSonnet();
    }

    private async Task RefreshSonnetProfile()
    {
        await RunSonnetAction(async () =>
        {
            var user = await SonnetClient.RefreshProfileAsync(Settings);
            await SaveAsync();
            SetSonnetMessage($"账户已刷新，余额 {user.Balance:0.####}。");
        });
    }

    private async Task EnsureSonnetArtKey()
    {
        await RunSonnetAction(async () =>
        {
            await SonnetClient.EnsureSonnetArtApiKeyAsync(Settings);
            await SonnetClient.EnsureOpenAiApiKeyAsync(Settings);
            await SaveAsync();
            SetSonnetMessage("账户已准备好。");
        });
    }

    private async Task CreateRechargeOrder()
    {
        await RunSonnetAction(async () =>
        {
            var order = await SonnetClient.CreateRechargeOrderAsync(
                Settings,
                _rechargeAmount,
                "alipay");
            await SaveAsync();
            OpenPaymentOverlay(order);
            SetSonnetMessage("充值订单已创建，请扫码完成支付。");
        });
    }

    private async Task RedeemAccountCode()
    {
        var code = _redeemCode.Trim();
        await RunSonnetAction(async () =>
        {
            var result = await SonnetClient.RedeemCodeAsync(Settings, code);
            var user = await SonnetClient.RefreshProfileAsync(Settings);
            await SaveAsync();
            _redeemCode = string.Empty;
            SetSonnetMessage(BuildRedeemSuccessMessage(result, user));
        });
    }

    private async Task SignOutSonnet()
    {
        await StopPaymentPollingAsync();
        _paymentOverlayOpen = false;
        _activePaymentOrder = null;
        _latestPaymentStatus = null;
        _paymentQrImageSource = null;
        SonnetClient.SignOut(Settings);
        await SaveAsync();
        _settingsOpen = false;
        SetSonnetMessage("已退出登录。");
    }

    private async Task RunSonnetAction(Func<Task> action)
    {
        _sonnetBusy = true;
        _sonnetMessage = null;
        _sonnetMessageIsError = false;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            SetSonnetMessage(ex.Message, isError: true);
        }
        finally
        {
            _sonnetBusy = false;
            StateHasChanged();
        }
    }

    private void SetSonnetMessage(string message, bool isError = false)
    {
        _sonnetMessage = message;
        _sonnetMessageIsError = isError;
    }

    private void StartServerStatusPolling()
    {
        _serverStatusCts?.Cancel();
        _serverStatusTimer?.Dispose();
        _serverStatusCts?.Dispose();

        _serverStatusCts = new CancellationTokenSource();
        _serverStatusTimer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        _ = PollServerStatusLoopAsync(_serverStatusCts.Token);
    }

    private async Task PollServerStatusLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CheckServerStatusAsync(cancellationToken);
            await InvokeAsync(StateHasChanged);

            while (_serverStatusTimer is not null &&
                await _serverStatusTimer.WaitForNextTickAsync(cancellationToken))
            {
                await CheckServerStatusAsync(cancellationToken);
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CheckServerStatusAsync(CancellationToken cancellationToken)
    {
        _serverStatusChecked = true;
        _serverStatusCheckedAt = DateTimeOffset.Now;

        try
        {
            using var response = await Http.GetAsync("api/sonnet/settings/public", cancellationToken);
            _serverOnline = response.IsSuccessStatusCode;
        }
        catch
        {
            _serverOnline = false;
        }
    }

    private async Task RestoreAccountAsync()
    {
        if (!SonnetLoggedIn)
        {
            var hadStoredKeys =
                !string.IsNullOrWhiteSpace(Settings.ImageApiKey) ||
                !string.IsNullOrWhiteSpace(Settings.OpenAiApiKey);
            Settings.ImageApiKey = string.Empty;
            Settings.OpenAiApiKey = string.Empty;
            if (hadStoredKeys)
            {
                await SaveAsync();
            }
            return;
        }

        _sonnetBusy = true;
        _sonnetMessage = null;
        _sonnetMessageIsError = false;
        try
        {
            await CompleteAccountSetupAsync();
            await SaveAsync();
            SetSonnetMessage("账户已恢复。");
        }
        catch (Exception ex)
        {
            SonnetClient.SignOut(Settings);
            await SaveAsync();
            SetSonnetMessage($"登录状态已失效，请重新登录。{ex.Message}", isError: true);
        }
        finally
        {
            _sonnetBusy = false;
        }
    }

    private async Task CompleteAccountSetupAsync()
    {
        await SonnetClient.RefreshProfileAsync(Settings);
        await SonnetClient.EnsureSonnetArtApiKeyAsync(Settings);
        await SonnetClient.EnsureOpenAiApiKeyAsync(Settings);
    }

    private void OpenPaymentOverlay(SonnetCreateOrderResult order)
    {
        _activePaymentOrder = order;
        _latestPaymentStatus = null;
        _paymentExpiresAt = order.ExpiresAt == default
            ? DateTimeOffset.Now.AddMinutes(30)
            : order.ExpiresAt;
        _paymentQrImageSource = BuildPaymentQrImageSource(order);
        _paymentOverlayOpen = true;
        _settingsOpen = false;
        StartPaymentPolling();
    }

    private void ShowPendingPaymentOrder()
    {
        _paymentOverlayOpen = true;
        _settingsOpen = false;
        if (_paymentTimer is null)
        {
            StartPaymentPolling();
        }
    }

    private void StartPaymentPolling()
    {
        _ = StopPaymentPollingAsync();
        if (_activePaymentOrder is null)
        {
            return;
        }

        _paymentCts = new CancellationTokenSource();
        _paymentTimer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        _ = PollPaymentLoopAsync(_paymentCts.Token);
    }

    private async Task PollPaymentLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshPaymentStatusAsync();

            while (_paymentTimer is not null &&
                await _paymentTimer.WaitForNextTickAsync(cancellationToken))
            {
                if (_activePaymentOrder is null || PaymentSucceeded || PaymentExpired)
                {
                    break;
                }

                await RefreshPaymentStatusAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task VerifyPaymentNow()
    {
        await UpdatePaymentStatusAsync(activeVerify: true, silent: false);
        StateHasChanged();
    }

    private async Task RefreshPaymentStatusAsync()
    {
        await UpdatePaymentStatusAsync(activeVerify: false, silent: true);
    }

    private async Task UpdatePaymentStatusAsync(bool activeVerify, bool silent)
    {
        if (_activePaymentOrder is null || _paymentPolling)
        {
            return;
        }

        _paymentPolling = true;
        try
        {
            _latestPaymentStatus = activeVerify && !string.IsNullOrWhiteSpace(_activePaymentOrder.OutTradeNo)
                ? await SonnetClient.VerifyPaymentOrderAsync(Settings, _activePaymentOrder.OutTradeNo)
                : await SonnetClient.GetPaymentOrderAsync(Settings, _activePaymentOrder.OrderId);

            if (PaymentSucceeded)
            {
                await SonnetClient.RefreshProfileAsync(Settings);
                await SaveAsync();
                SetSonnetMessage("充值已完成，余额已刷新。");
                await StopPaymentPollingAsync();
            }
            else if (PaymentExpired)
            {
                SetSonnetMessage("订单已过期，请重新生成二维码。", isError: true);
                await StopPaymentPollingAsync();
            }
            else if (!silent)
            {
                SetSonnetMessage("还没有确认到账，请稍后再试。");
            }
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                SetSonnetMessage(ex.Message, isError: true);
            }
        }
        finally
        {
            _paymentPolling = false;
        }
    }

    private async Task CancelPaymentOrder()
    {
        if (_activePaymentOrder is null || _paymentCancelling)
        {
            return;
        }

        _paymentCancelling = true;
        try
        {
            await SonnetClient.CancelPaymentOrderAsync(Settings, _activePaymentOrder.OrderId);
            await StopPaymentPollingAsync();
            _paymentOverlayOpen = false;
            _activePaymentOrder = null;
            _latestPaymentStatus = null;
            _paymentQrImageSource = null;
            SetSonnetMessage("充值订单已取消。");
        }
        catch (Exception ex)
        {
            SetSonnetMessage(ex.Message, isError: true);
        }
        finally
        {
            _paymentCancelling = false;
        }
    }

    private async Task ClosePaymentOverlay()
    {
        if (PaymentSucceeded || PaymentExpired)
        {
            await StopPaymentPollingAsync();
        }

        _paymentOverlayOpen = false;
    }

    private async Task StopPaymentPollingAsync()
    {
        _paymentCts?.Cancel();
        _paymentTimer?.Dispose();
        _paymentTimer = null;
        _paymentCts?.Dispose();
        _paymentCts = null;
        await Task.CompletedTask;
    }

    private static string? BuildPaymentQrImageSource(SonnetCreateOrderResult order)
    {
        var qrValue = !string.IsNullOrWhiteSpace(order.QrCode)
            ? order.QrCode.Trim()
            : order.PayUrl?.Trim();
        if (string.IsNullOrWhiteSpace(qrValue))
        {
            return null;
        }

        if (qrValue.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return qrValue;
        }

        if (IsImageUrl(qrValue))
        {
            return qrValue;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(qrValue, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(12);
        return $"data:image/png;base64,{Convert.ToBase64String(png)}";
    }

    private static bool IsImageUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = uri.AbsolutePath;
        return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPaymentSuccess(string? status)
    {
        return status is "COMPLETED" or "PAID" or "RECHARGING";
    }

    private static bool IsPaymentExpired(string? status)
    {
        return status is "EXPIRED" or "CANCELLED" or "FAILED";
    }

    private async Task NavigateToLastWorkspaceIfNeededAsync()
    {
        if (_navigatedToLastWorkspace || !AccountReady)
        {
            return;
        }

        _navigatedToLastWorkspace = true;
        await Task.CompletedTask;
    }

    private static string BuildRedeemSuccessMessage(SonnetRedeemResult result, SonnetUser user)
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            return $"{result.Message} 当前余额 {FormatMoney(user.Balance)}。";
        }

        var detail = result.Type switch
        {
            "balance" or "admin_balance" => $"余额 {FormatSignedMoney(result.Value)}",
            "concurrency" or "admin_concurrency" => $"并发额度 {FormatSignedNumber(result.Value)}",
            "subscription" => BuildSubscriptionRedeemMessage(result),
            _ => "兑换码",
        };

        return $"兑换成功，{detail}。当前余额 {FormatMoney(user.Balance)}。";
    }

    private static string BuildSubscriptionRedeemMessage(SonnetRedeemResult result)
    {
        var groupName = !string.IsNullOrWhiteSpace(result.GroupName)
            ? result.GroupName
            : result.Group?.Name;
        var days = result.ValidityDays ?? (int)Math.Round(result.Value);
        var daysLabel = days == 0 ? "订阅权益" : $"{days} 天订阅权益";
        return string.IsNullOrWhiteSpace(groupName)
            ? daysLabel
            : $"{groupName} {daysLabel}";
    }

    private static string FormatSignedMoney(decimal value)
    {
        return value >= 0 ? $"+{FormatMoney(value)}" : FormatMoney(value);
    }

    private static string FormatSignedNumber(decimal value)
    {
        return value >= 0 ? $"+{FormatMoney(value)}" : FormatMoney(value);
    }

    private sealed record AspectRatioPreset(string Ratio, string? Badge = null)
    {
        public string Label => Ratio;
    }

    private sealed record ResolutionPreset(string Tier, string Label, string Description);
    private sealed record PendingPrompt(string Text, string MessageId);
    private sealed record ImageRevisionRequest(string ImageUrl, string Prompt);
    private sealed record PromptPolishOption(string Value, string Label, string Description);
    private sealed record ReferenceRoleOption(string Value, string Label, string Description);
    private sealed record MatrixResolutionOption(string Tier, string Label);
    private sealed record ImageMatrixRun(string ResolutionTier, string Quality, string Size);
    private sealed record SenderImageAttachment(string Id, ImageReferenceFile ReferenceFile, XAttachmentItem Attachment);
    private sealed record GalleryImageItem(
        StudioSession Session,
        StudioMessage Message,
        GeneratedImage Image,
        int ImageIndex);

    private sealed record ImageGenerationMetadata(
        DateTimeOffset CreatedAt,
        string Prompt,
        string Mode,
        string Model,
        string Size,
        string AspectRatio,
        string ResolutionTier,
        string Quality,
        string OutputFormat,
        string Background,
        string Moderation,
        string RequestMode,
        int RequestCount,
        int ReferenceCount,
        string ReferenceRole,
        bool HasMask,
        long DurationMs,
        string EstimatedCost,
        string EstimatedDuration,
        string RequestSummary);

    private sealed class ImageDownloadResult
    {
        public bool SavedLocally { get; set; }
        public string? FilePath { get; set; }
        public string? FileName { get; set; }
    }

    private static string FormatMoney(decimal value)
    {
        return value == decimal.Truncate(value)
            ? value.ToString("0")
            : value.ToString("0.####");
    }

    private async Task UpdateMode(string mode)
    {
        ApplyResolvedMode(ResolveRequestMode(_senderText ?? string.Empty, mode));

        TouchActiveSession();
        await SaveAsync();
    }

    private async Task UpdateReferences(ChangeEventArgs args)
    {
        ActiveSession.ImageReferences = args.Value?.ToString() ?? string.Empty;
        TouchActiveSession();
        await SaveAsync();
    }

    private async Task UpdateMaskReference(ChangeEventArgs args)
    {
        ActiveSession.MaskReference = args.Value?.ToString() ?? string.Empty;
        TouchActiveSession();
        await SaveAsync();
    }

    private async Task UpdateReferenceRole(ChangeEventArgs args)
    {
        ActiveSession.ReferenceRole = StudioSnapshot.NormalizeReferenceRole(args.Value?.ToString());
        TouchActiveSession();
        await SaveAsync();
    }

    private async Task AddReferenceFiles(InputFileChangeEventArgs args)
    {
        const long maxFileSize = 12 * 1024 * 1024;
        ClearAllReferenceInputs();
        var maxFiles = ActiveSession.Mode == "variation" ? 1 : 16;
        foreach (var file in args.GetMultipleFiles(maxFiles))
        {
            await using var stream = file.OpenReadStream(maxFileSize);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var reference = new ImageReferenceFile(file.Name, file.ContentType, memory.ToArray());
            _referenceFiles.Add(reference);
            var attachment = new XAttachmentItem
            {
                Name = file.Name,
                Description = "图片附件",
                ContentType = file.ContentType,
                Size = file.Size,
                ImageUrl = ToDataUrl(reference),
                Status = XFileCardStatus.Done,
            };
            _senderImageAttachments.Add(new SenderImageAttachment(attachment.Id, reference, attachment));
        }

        ActiveSession.ImageReferences = string.Join('\n', _referenceFiles.Select(file => $"local:{file.FileName}"));
        SyncSenderAttachments();
        ApplyResolvedMode(ResolveRequestMode(_senderText ?? string.Empty, ActiveSession.Mode));
        TouchActiveSession();
        await SaveAsync();
    }

    private void UpdateSonnetEmail(ChangeEventArgs args) { _sonnetEmail = args.Value?.ToString() ?? string.Empty; }
    private void UpdateSonnetPassword(ChangeEventArgs args) { _sonnetPassword = args.Value?.ToString() ?? string.Empty; }
    private void UpdateSonnetPromoCode(ChangeEventArgs args) { _sonnetPromoCode = args.Value?.ToString() ?? string.Empty; }
    private void UpdateSonnetInvitationCode(ChangeEventArgs args) { _sonnetInvitationCode = args.Value?.ToString() ?? string.Empty; }
    private void UpdateRedeemCode(ChangeEventArgs args) { _redeemCode = args.Value?.ToString() ?? string.Empty; }
    private void ToggleRatioMenu()
    {
        _ratioMenuOpen = !_ratioMenuOpen;
        if (_ratioMenuOpen)
        {
            _resolutionMenuOpen = false;
        }
    }

    private void ToggleResolutionMenu()
    {
        _resolutionMenuOpen = !_resolutionMenuOpen;
        if (_resolutionMenuOpen)
        {
            _ratioMenuOpen = false;
        }
    }

    private async Task SelectAspectRatio(string aspectRatio)
    {
        Settings.AspectRatio = StudioSettings.NormalizeAspectRatio(aspectRatio);
        Settings.Size = BuildImageSize(Settings.AspectRatio, Settings.ResolutionTier);
        _ratioMenuOpen = false;
        await SaveAsync();
    }

    private async Task SelectResolution(string resolutionTier)
    {
        Settings.ResolutionTier = NormalizeResolutionTierForModel(resolutionTier, CurrentModelCapabilities);
        Settings.Size = BuildImageSize(EffectiveAspectRatio, Settings.ResolutionTier);
        CoerceImageSettingsForCurrentModel();
        _resolutionMenuOpen = false;
        await SaveAsync();
    }

    private async Task UpdateQuickQuality(ChangeEventArgs args)
    {
        Settings.Quality = args.Value?.ToString() ?? "auto";
        CoerceImageSettingsForCurrentModel();
        await SaveAsync();
    }

    private async Task UpdateQuickFormat(ChangeEventArgs args)
    {
        Settings.Format = args.Value?.ToString() ?? "png";
        CoerceImageSettingsForCurrentModel();
        await SaveAsync();
    }

    private async Task UpdateQuickCount(ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString(), out var value))
        {
            _count = Math.Clamp(value, 1, 10);
            await SaveAsync();
        }
    }

    private async Task UpdateThemeMode(string mode)
    {
        Settings.ThemeMode = StudioSettings.NormalizeThemeMode(mode);
        await SyncDocumentThemeAsync();
        await SaveAsync();
    }

    private async Task UpdatePromptPolishMode(string mode)
    {
        Settings.PromptPolishMode = StudioSettings.NormalizePromptPolishMode(mode);
        if (Settings.PromptPolishMode != "ask")
        {
            _pendingPrompt = null;
            RemovePendingPromptMessages();
        }

        await SaveAsync();
    }

    [JSInvokable]
    public Task OnSystemThemeChanged(bool prefersDark)
    {
        if (_systemPrefersDark == prefersDark)
        {
            return Task.CompletedTask;
        }

        _systemPrefersDark = prefersDark;
        return InvokeAsync(StateHasChanged);
    }

    private void UpdateRechargeAmount(ChangeEventArgs args)
    {
        if (decimal.TryParse(args.Value?.ToString(), out var amount))
        {
            _rechargeAmount = Math.Max(1, amount);
        }
    }

    private async Task UpdateModel(string value)
    {
        Settings.Model = ImageModelCatalog.NormalizeModel(value);
        CoerceImageSettingsForCurrentModel();
        await SaveAsync();
    }
    private async Task UpdateCount(int value) { _count = Math.Clamp(value, 1, 10); await SaveAsync(); }
    private async Task UpdateSize(string value)
    {
        Settings.Size = string.IsNullOrWhiteSpace(value)
            ? BuildImageSize(EffectiveAspectRatio, Settings.ResolutionTier)
            : value;
        Settings.AspectRatio = ResolveAspectRatio(Settings.AspectRatio, Settings.Size);
        await SaveAsync();
    }
    private async Task UpdateQuality(string value) { Settings.Quality = value; CoerceImageSettingsForCurrentModel(); await SaveAsync(); }
    private async Task UpdateStyle(string value) { Settings.Style = value; CoerceImageSettingsForCurrentModel(); await SaveAsync(); }
    private async Task UpdateBackground(string value) { Settings.Background = value; CoerceImageSettingsForCurrentModel(); await SaveAsync(); }
    private async Task UpdateFormat(string value) { Settings.Format = value; CoerceImageSettingsForCurrentModel(); await SaveAsync(); }
    private async Task UpdateCompression(int value) { Settings.Compression = Math.Clamp(value, 0, 100); await SaveAsync(); }
    private async Task UpdateModeration(string value) { Settings.Moderation = value; await SaveAsync(); }
    private async Task UpdateFidelity(string value) { Settings.InputFidelity = value; CoerceImageSettingsForCurrentModel(); await SaveAsync(); }
    private async Task UpdateUser(ChangeEventArgs args) { Settings.User = args.Value?.ToString() ?? string.Empty; await SaveAsync(); }
    private async Task UpdateRequestMode(ChangeEventArgs args) { Settings.RequestMode = args.Value?.ToString() ?? "stream"; CoerceImageSettingsForCurrentModel(); await SaveAsync(); }
    private async Task UpdatePartial(int value) { Settings.PartialImages = Math.Clamp(value, 0, 3); CoerceImageSettingsForCurrentModel(); await SaveAsync(); }
    private async Task UpdateAdvancedJson(ChangeEventArgs args) { Settings.AdvancedJson = args.Value?.ToString() ?? string.Empty; await SaveAsync(); }

    private void EnsureActiveMode()
    {
        if (string.IsNullOrWhiteSpace(ActiveSession.Mode) || ActiveSession.Mode == "text")
        {
            ActiveSession.Mode = "generate";
        }
    }

    private static string ModeName(string? mode)
    {
        return mode switch
        {
            "image" => "图生图",
            "edit" => "图片编辑",
            "variation" => "变化",
            _ => "文生图",
        };
    }

    public void Dispose()
    {
        if (!string.IsNullOrWhiteSpace(_systemThemeWatchId))
        {
            _ = JsRuntime.InvokeVoidAsync("sonnetArt.unwatchSystemTheme", _systemThemeWatchId);
        }

        _selfReference?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
        _paymentCts?.Cancel();
        _paymentTimer?.Dispose();
        _paymentCts?.Dispose();
        _serverStatusCts?.Cancel();
        _serverStatusTimer?.Dispose();
        _serverStatusCts?.Dispose();
    }

}
