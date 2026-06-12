using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SonnetArt.Models;

namespace SonnetArt.Services;

public sealed class SonnetAccountClient
{
    private const string LocalProxyRoot = "/api/sonnet/";
    private const string LocalProxyHeader = "X-SonnetArt-Proxy";
    private const string SonnetArtKeyName = "SonnetArt Image";
    private const string SonnetArtKeySearch = "SonnetArt";
    private const string OpenAiKeyName = "SonnetArt OpenAI";
    private const string OpenAiKeySearch = "OpenAI";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly string[] PreferredPaymentTypes =
    [
        "alipay",
        "alipay_direct",
        "wxpay",
        "wxpay_direct",
        "stripe",
        "airwallex",
    ];

    private readonly HttpClient _httpClient;

    public SonnetAccountClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<SonnetAuthResponse> LoginAsync(
        StudioSettings settings,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<SonnetAuthResponse>(
            settings,
            HttpMethod.Post,
            "auth/login",
            new
            {
                email = email.Trim(),
                password,
            },
            accessToken: null,
            cancellationToken);

        ApplyAuth(settings, response);
        return response;
    }

    public async Task<SonnetAuthResponse> RegisterAsync(
        StudioSettings settings,
        string email,
        string password,
        string? promoCode,
        string? invitationCode,
        string? affiliateCode,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<SonnetAuthResponse>(
            settings,
            HttpMethod.Post,
            "auth/register",
            new
            {
                email = email.Trim(),
                password,
                promo_code = EmptyToNull(promoCode),
                invitation_code = EmptyToNull(invitationCode),
                aff_code = EmptyToNull(affiliateCode),
            },
            accessToken: null,
            cancellationToken);

        ApplyAuth(settings, response);
        return response;
    }

    public async Task<SonnetUser> RefreshProfileAsync(
        StudioSettings settings,
        CancellationToken cancellationToken = default)
    {
        await EnsureTokenAsync(settings, cancellationToken);
        var user = await SendAsync<SonnetUser>(
            settings,
            HttpMethod.Get,
            "user/profile",
            body: null,
            settings.SonnetAccessToken,
            cancellationToken);

        settings.SonnetUser = user;
        return user;
    }

    public async Task<IReadOnlyList<SonnetGroup>> GetAvailableGroupsAsync(
        StudioSettings settings,
        CancellationToken cancellationToken = default)
    {
        await EnsureTokenAsync(settings, cancellationToken);
        return await SendAsync<List<SonnetGroup>>(
            settings,
            HttpMethod.Get,
            "groups/available",
            body: null,
            settings.SonnetAccessToken,
            cancellationToken);
    }

    public async Task<SonnetPaginatedResponse<SonnetApiKey>> ListApiKeysAsync(
        StudioSettings settings,
        string? search = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        await EnsureTokenAsync(settings, cancellationToken);
        var path = $"keys?page=1&page_size={Math.Clamp(pageSize, 1, 1000)}&sort_by=created_at&sort_order=desc";
        if (!string.IsNullOrWhiteSpace(search))
        {
            path += $"&search={Uri.EscapeDataString(search.Trim())}";
        }

        return await SendAsync<SonnetPaginatedResponse<SonnetApiKey>>(
            settings,
            HttpMethod.Get,
            path,
            body: null,
            settings.SonnetAccessToken,
            cancellationToken);
    }

    public async Task<SonnetEnsureApiKeyResult> EnsureSonnetArtApiKeyAsync(
        StudioSettings settings,
        CancellationToken cancellationToken = default)
    {
        await EnsureTokenAsync(settings, cancellationToken);
        var groups = await GetAvailableGroupsAsync(settings, cancellationToken);
        var group = ResolveSonnetArtGroup(groups);
        if (group is null)
        {
            ClearImageApiKey(settings);
            throw new InvalidOperationException(
                "没有找到已启用的图像生成分组。请在 sub2api 后台创建或启用包含 gpt-image/image/图片 标识的分组，并为该分组开启图像生成权限。");
        }

        var keys = await ListApiKeysAsync(settings, SonnetArtKeySearch, cancellationToken: cancellationToken);
        var existing = keys.Items
            .Where(IsUsableKey)
            .Where(key => IsImageKey(key, group))
            .OrderByDescending(key => key.GroupId == group?.Id)
            .ThenByDescending(key => key.Name.Equals(SonnetArtKeyName, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (existing is not null)
        {
            ApplyApiKey(settings, existing, existing.Group ?? group);
            return new SonnetEnsureApiKeyResult
            {
                Created = false,
                ApiKey = existing.Key,
                ApiKeyId = existing.Id,
                ApiKeyName = existing.Name,
                GroupId = existing.GroupId,
                GroupName = existing.Group?.Name ?? group?.Name ?? string.Empty,
            };
        }

        var created = await CreateApiKeyAsync(settings, SonnetArtKeyName, group?.Id, cancellationToken);
        ApplyApiKey(settings, created, created.Group ?? group);
        return new SonnetEnsureApiKeyResult
        {
            Created = true,
            UsedFallbackGroup = group is not null &&
                !IsImageGroup(group) &&
                !group.Name.Equals(SonnetArtKeyName, StringComparison.OrdinalIgnoreCase),
            ApiKey = created.Key,
            ApiKeyId = created.Id,
            ApiKeyName = created.Name,
            GroupId = created.GroupId,
            GroupName = created.Group?.Name ?? group?.Name ?? string.Empty,
        };
    }

    public async Task<SonnetEnsureApiKeyResult> EnsureOpenAiApiKeyAsync(
        StudioSettings settings,
        CancellationToken cancellationToken = default)
    {
        await EnsureTokenAsync(settings, cancellationToken);
        var groups = await GetAvailableGroupsAsync(settings, cancellationToken);
        var group = ResolveOpenAiGroup(groups);
        var keys = await ListApiKeysAsync(settings, OpenAiKeySearch, cancellationToken: cancellationToken);
        var existing = keys.Items
            .Where(IsUsableKey)
            .Where(key => IsOpenAiKey(key, group))
            .OrderByDescending(key => key.GroupId == group?.Id)
            .ThenByDescending(key => key.Name.Equals(OpenAiKeyName, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (existing is not null)
        {
            ApplyOpenAiApiKey(settings, existing, existing.Group ?? group);
            return new SonnetEnsureApiKeyResult
            {
                Created = false,
                ApiKey = existing.Key,
                ApiKeyId = existing.Id,
                ApiKeyName = existing.Name,
                GroupId = existing.GroupId,
                GroupName = existing.Group?.Name ?? group?.Name ?? string.Empty,
            };
        }

        var created = await CreateApiKeyAsync(settings, OpenAiKeyName, group?.Id, cancellationToken);
        ApplyOpenAiApiKey(settings, created, created.Group ?? group);
        return new SonnetEnsureApiKeyResult
        {
            Created = true,
            UsedFallbackGroup = group is not null && !group.Name.Equals("OpenAi", StringComparison.OrdinalIgnoreCase),
            ApiKey = created.Key,
            ApiKeyId = created.Id,
            ApiKeyName = created.Name,
            GroupId = created.GroupId,
            GroupName = created.Group?.Name ?? group?.Name ?? string.Empty,
        };
    }

    public async Task<SonnetApiKey> CreateApiKeyAsync(
        StudioSettings settings,
        string name,
        long? groupId,
        CancellationToken cancellationToken = default)
    {
        await EnsureTokenAsync(settings, cancellationToken);
        return await SendAsync<SonnetApiKey>(
            settings,
            HttpMethod.Post,
            "keys",
            new
            {
                name = string.IsNullOrWhiteSpace(name) ? SonnetArtKeyName : name.Trim(),
                group_id = groupId,
            },
            settings.SonnetAccessToken,
            cancellationToken);
    }

    public async Task<SonnetCheckoutInfo> GetCheckoutInfoAsync(
        StudioSettings settings,
        CancellationToken cancellationToken = default)
    {
        await EnsureTokenAsync(settings, cancellationToken);
        return await SendAsync<SonnetCheckoutInfo>(
            settings,
            HttpMethod.Get,
            "payment/checkout-info",
            body: null,
            settings.SonnetAccessToken,
            cancellationToken);
    }

    public async Task<SonnetCreateOrderResult> CreateRechargeOrderAsync(
        StudioSettings settings,
        decimal amount,
        string? paymentType,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("充值金额必须大于 0。");
        }

        await EnsureTokenAsync(settings, cancellationToken);
        var checkout = await GetCheckoutInfoAsync(settings, cancellationToken);
        var resolvedPaymentType = ResolvePaymentType(checkout, paymentType);
        settings.SonnetPaymentType = resolvedPaymentType;

        return await SendAsync<SonnetCreateOrderResult>(
            settings,
            HttpMethod.Post,
            "payment/orders",
            new
            {
                amount,
                payment_type = resolvedPaymentType,
                order_type = "balance",
                return_url = "/payment/result",
                is_mobile = false,
            },
            settings.SonnetAccessToken,
            cancellationToken);
    }

    public async Task<SonnetPaymentOrder> GetPaymentOrderAsync(
        StudioSettings settings,
        long orderId,
        CancellationToken cancellationToken = default)
    {
        if (orderId <= 0)
        {
            throw new InvalidOperationException("订单编号无效。");
        }

        await EnsureTokenAsync(settings, cancellationToken);
        return await SendAsync<SonnetPaymentOrder>(
            settings,
            HttpMethod.Get,
            $"payment/orders/{orderId}",
            body: null,
            settings.SonnetAccessToken,
            cancellationToken);
    }

    public async Task<SonnetPaymentOrder> VerifyPaymentOrderAsync(
        StudioSettings settings,
        string outTradeNo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outTradeNo))
        {
            throw new InvalidOperationException("订单号无效。");
        }

        await EnsureTokenAsync(settings, cancellationToken);
        return await SendAsync<SonnetPaymentOrder>(
            settings,
            HttpMethod.Post,
            "payment/orders/verify",
            new
            {
                out_trade_no = outTradeNo.Trim(),
            },
            settings.SonnetAccessToken,
            cancellationToken);
    }

    public async Task CancelPaymentOrderAsync(
        StudioSettings settings,
        long orderId,
        CancellationToken cancellationToken = default)
    {
        if (orderId <= 0)
        {
            throw new InvalidOperationException("订单编号无效。");
        }

        await EnsureTokenAsync(settings, cancellationToken);
        await SendAsync<JsonElement>(
            settings,
            HttpMethod.Post,
            $"payment/orders/{orderId}/cancel",
            body: null,
            settings.SonnetAccessToken,
            cancellationToken);
    }

    public async Task<SonnetRedeemResult> RedeemCodeAsync(
        StudioSettings settings,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("请先填写兑换码。");
        }

        await EnsureTokenAsync(settings, cancellationToken);
        return await SendAsync<SonnetRedeemResult>(
            settings,
            HttpMethod.Post,
            "redeem",
            new
            {
                code = code.Trim(),
            },
            settings.SonnetAccessToken,
            cancellationToken);
    }

    public void SignOut(StudioSettings settings)
    {
        settings.SonnetAccessToken = string.Empty;
        settings.SonnetRefreshToken = string.Empty;
        settings.SonnetTokenExpiresAt = null;
        settings.SonnetUser = null;
        settings.ImageApiKey = string.Empty;
        settings.SonnetApiKeyId = null;
        settings.SonnetApiKeyName = string.Empty;
        settings.SonnetGroupId = null;
        settings.SonnetGroupName = string.Empty;
        settings.OpenAiApiKey = string.Empty;
        settings.SonnetOpenAiApiKeyId = null;
        settings.SonnetOpenAiApiKeyName = string.Empty;
        settings.SonnetOpenAiGroupId = null;
        settings.SonnetOpenAiGroupName = string.Empty;
    }

    private async Task EnsureTokenAsync(StudioSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.SonnetAccessToken))
        {
            throw new InvalidOperationException("请先登录账户。");
        }

        if (string.IsNullOrWhiteSpace(settings.SonnetRefreshToken) ||
            settings.SonnetTokenExpiresAt is null ||
            settings.SonnetTokenExpiresAt > DateTimeOffset.Now.AddMinutes(2))
        {
            return;
        }

        var response = await SendAsync<SonnetRefreshTokenResponse>(
            settings,
            HttpMethod.Post,
            "auth/refresh",
            new
            {
                refresh_token = settings.SonnetRefreshToken,
            },
            accessToken: null,
            cancellationToken);

        settings.SonnetAccessToken = response.AccessToken;
        if (!string.IsNullOrWhiteSpace(response.RefreshToken))
        {
            settings.SonnetRefreshToken = response.RefreshToken;
        }

        if (response.ExpiresIn > 0)
        {
            settings.SonnetTokenExpiresAt = DateTimeOffset.Now.AddSeconds(response.ExpiresIn);
        }
    }

    private async Task<T> SendAsync<T>(
        StudioSettings settings,
        HttpMethod method,
        string path,
        object? body,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendOnceAsync<T>(
                settings,
                method,
                path,
                body,
                accessToken,
                useLocalProxy: true,
                cancellationToken);
        }
        catch (SonnetProxyUnavailableException)
        {
            throw new HttpRequestException("账户代理不可用，请检查 SonnetArt 服务配置。");
        }
    }

    private async Task<T> SendOnceAsync<T>(
        StudioSettings settings,
        HttpMethod method,
        string path,
        object? body,
        string? accessToken,
        bool useLocalProxy,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            method,
            BuildLocalProxyEndpoint(path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.AcceptLanguage.ParseAdd("zh-CN");

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (useLocalProxy && !IsLocalProxyResponse(response))
        {
            throw new SonnetProxyUnavailableException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"账户服务返回 {(int)response.StatusCode} {response.ReasonPhrase}: {ExtractErrorMessage(raw)}");
        }

        try
        {
            var data = UnwrapResponse(raw);
            var value = data.Deserialize<T>(JsonOptions);
            return value ?? throw new InvalidOperationException("账户服务响应为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"无法解析账户服务响应：{ex.Message}", ex);
        }
    }

    private static bool IsLocalProxyResponse(HttpResponseMessage response)
    {
        return response.Headers.Contains(LocalProxyHeader);
    }

    private static JsonElement UnwrapResponse(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("code", out var code))
        {
            if (code.GetInt32() != 0)
            {
                var message = root.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : "账户服务请求失败。";
                throw new InvalidOperationException(message);
            }

            if (!root.TryGetProperty("data", out var data) || data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                throw new InvalidOperationException("账户服务响应缺少 data。");
            }

            return data.Clone();
        }

        return root.Clone();
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
            var message = root?["message"]?.GetValue<string>();
            var detail = root?["detail"]?.GetValue<string>();
            var reason = root?["reason"]?.GetValue<string>();
            var text = string.IsNullOrWhiteSpace(detail) ? message : detail;
            return string.IsNullOrWhiteSpace(reason) ? text ?? raw : $"{text ?? "账户服务请求失败"} ({reason})";
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static void ApplyAuth(StudioSettings settings, SonnetAuthResponse response)
    {
        settings.SonnetAccessToken = response.AccessToken;
        settings.SonnetRefreshToken = response.RefreshToken ?? string.Empty;
        settings.SonnetTokenExpiresAt = response.ExpiresIn is > 0
            ? DateTimeOffset.Now.AddSeconds(response.ExpiresIn.Value)
            : null;
        settings.SonnetUser = response.User;
    }

    private static void ApplyApiKey(StudioSettings settings, SonnetApiKey key, SonnetGroup? group)
    {
        settings.ImageApiKey = key.Key;
        settings.SonnetApiKeyId = key.Id;
        settings.SonnetApiKeyName = key.Name;
        settings.SonnetGroupId = key.GroupId ?? group?.Id;
        settings.SonnetGroupName = group?.Name ?? key.Group?.Name ?? string.Empty;
        settings.BaseUrl = StudioSettings.DefaultBaseUrl;
    }

    private static void ClearImageApiKey(StudioSettings settings)
    {
        settings.ImageApiKey = string.Empty;
        settings.SonnetApiKeyId = null;
        settings.SonnetApiKeyName = string.Empty;
        settings.SonnetGroupId = null;
        settings.SonnetGroupName = string.Empty;
    }

    private static void ApplyOpenAiApiKey(StudioSettings settings, SonnetApiKey key, SonnetGroup? group)
    {
        settings.OpenAiApiKey = key.Key;
        settings.SonnetOpenAiApiKeyId = key.Id;
        settings.SonnetOpenAiApiKeyName = key.Name;
        settings.SonnetOpenAiGroupId = key.GroupId ?? group?.Id;
        settings.SonnetOpenAiGroupName = group?.Name ?? key.Group?.Name ?? string.Empty;
        settings.BaseUrl = StudioSettings.DefaultBaseUrl;
    }

    private static bool IsUsableKey(SonnetApiKey key)
    {
        return !string.IsNullOrWhiteSpace(key.Key) &&
            string.Equals(key.Status, "active", StringComparison.OrdinalIgnoreCase) &&
            (key.ExpiresAt is null || key.ExpiresAt > DateTimeOffset.Now);
    }

    private static bool IsImageKey(SonnetApiKey key, SonnetGroup? group)
    {
        if (IsOpenAiKeyName(key.Name))
        {
            return false;
        }

        if (group is not null && IsImageGroup(group))
        {
            return key.GroupId == group.Id ||
                IsImageGroup(key.Group) ||
                (key.GroupId is null && IsImageKeyName(key.Name));
        }

        return IsImageKeyName(key.Name) ||
            IsImageGroup(key.Group) ||
            (group is not null && key.GroupId == group.Id);
    }

    private static bool IsOpenAiKey(SonnetApiKey key, SonnetGroup? group)
    {
        return IsOpenAiKeyName(key.Name) ||
            IsOpenAiGroup(key.Group) ||
            (group is not null && key.GroupId == group.Id);
    }

    private static SonnetGroup? ResolveSonnetArtGroup(IReadOnlyList<SonnetGroup> groups)
    {
        return groups.FirstOrDefault(group =>
                IsActiveGroup(group) &&
                HasGptImage2Capability(group))
            ?? groups.FirstOrDefault(group =>
                IsActiveGroup(group) &&
                IsImageGroup(group));
    }

    private static SonnetGroup? ResolveOpenAiGroup(IReadOnlyList<SonnetGroup> groups)
    {
        return groups.FirstOrDefault(group =>
                group.Name.Equals("OpenAi", StringComparison.OrdinalIgnoreCase) &&
                IsActiveGroup(group))
            ?? groups.FirstOrDefault(group =>
                group.Name.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) &&
                IsActiveGroup(group))
            ?? groups.FirstOrDefault(group =>
                string.Equals(group.Platform, "openai", StringComparison.OrdinalIgnoreCase) &&
                !IsImageGroup(group) &&
                !group.Name.Contains(SonnetArtKeyName, StringComparison.OrdinalIgnoreCase) &&
                IsActiveGroup(group))
            ?? groups.FirstOrDefault(group =>
                string.Equals(group.Platform, "openai", StringComparison.OrdinalIgnoreCase) &&
                !IsImageGroup(group) &&
                IsActiveGroup(group))
            ?? groups.FirstOrDefault(group =>
                IsActiveGroup(group));
    }

    private static bool IsActiveGroup(SonnetGroup group)
    {
        return string.Equals(group.Status, "active", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImageKeyName(string name)
    {
        return name.Equals(SonnetArtKeyName, StringComparison.OrdinalIgnoreCase) ||
            ContainsAny(name, "image", "gpt-image", "图片", "图像", "作图");
    }

    private static bool IsOpenAiKeyName(string name)
    {
        return name.Contains("openai", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImageGroup(SonnetGroup? group)
    {
        return group is not null &&
            (HasImageGenerationCapability(group) ||
            IsImageLabel(group.Name) ||
            IsImageLabel(group.Platform) ||
            IsImageLabel(group.Description));
    }

    private static bool HasGptImage2Capability(SonnetGroup? group)
    {
        return group is not null &&
            HasImageGenerationCapability(group) &&
            SupportsGptImage2(group);
    }

    private static bool HasImageGenerationCapability(SonnetGroup group)
    {
        return group.AllowImageGeneration == true ||
            JsonArrayContains(group.SupportedModelScopes, "gpt_image") ||
            JsonArrayContains(group.SupportedModelScopes, "openai_image") ||
            JsonArrayContains(group.SupportedModelScopes, "image_generation");
    }

    private static bool SupportsGptImage2(SonnetGroup group)
    {
        if (ContainsAny(group.DefaultMappedModel, "gpt-image-2"))
        {
            return true;
        }

        if (ModelListContains(group.ModelsListConfig, "gpt-image-2"))
        {
            return true;
        }

        if (ModelListIsUnrestricted(group.ModelsListConfig))
        {
            return true;
        }

        return JsonArrayContains(group.SupportedModelScopes, "gpt_image") ||
            JsonArrayContains(group.SupportedModelScopes, "openai_image") ||
            JsonArrayContains(group.SupportedModelScopes, "image_generation");
    }

    private static bool IsOpenAiGroup(SonnetGroup? group)
    {
        return group is not null &&
            !IsImageGroup(group) &&
            (group.Name.Equals("OpenAi", StringComparison.OrdinalIgnoreCase) ||
            group.Name.Contains("openai", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(group.Platform, "openai", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsImageLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var label = value.Trim();
        return label.Equals("image", StringComparison.OrdinalIgnoreCase) ||
            label.Equals("images", StringComparison.OrdinalIgnoreCase) ||
            ContainsAny(label, "gpt-image", "gpt image", "image", "图片", "图像", "作图");
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool JsonArrayContains(JsonElement element, string expected)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String &&
                string.Equals(item.GetString(), expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ModelListContains(JsonElement config, string expected)
    {
        if (TryGetModelsArray(config, out var models))
        {
            foreach (var model in models.EnumerateArray())
            {
                if (model.ValueKind == JsonValueKind.String &&
                    IsGptImage2Model(model.GetString()))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ModelListIsUnrestricted(JsonElement config)
    {
        if (config.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return true;
        }

        if (config.ValueKind == JsonValueKind.Object)
        {
            if (config.TryGetProperty("enabled", out var enabled) &&
                enabled.ValueKind is JsonValueKind.False)
            {
                return true;
            }

            return !TryGetModelsArray(config, out var models) ||
                models.ValueKind != JsonValueKind.Array ||
                !models.EnumerateArray().Any();
        }

        return false;
    }

    private static bool TryGetModelsArray(JsonElement config, out JsonElement models)
    {
        if (config.ValueKind == JsonValueKind.Object &&
            config.TryGetProperty("models", out models) &&
            models.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        models = default;
        return false;
    }

    private static bool IsGptImage2Model(string? model)
    {
        return !string.IsNullOrWhiteSpace(model) &&
            (model.Equals("gpt-image-2", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("gpt-image-2-", StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolvePaymentType(SonnetCheckoutInfo checkout, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested) &&
            checkout.Methods.TryGetValue(requested.Trim(), out var requestedLimit) &&
            requestedLimit.Available)
        {
            return requested.Trim();
        }

        foreach (var paymentType in PreferredPaymentTypes)
        {
            if (checkout.Methods.TryGetValue(paymentType, out var limit) && limit.Available)
            {
                return paymentType;
            }
        }

        var first = checkout.Methods.FirstOrDefault(pair => pair.Value.Available).Key;
        if (!string.IsNullOrWhiteSpace(first))
        {
            return first;
        }

        throw new InvalidOperationException("当前没有可用的充值方式。");
    }

    private static Uri BuildLocalProxyEndpoint(string path)
    {
        return new Uri(LocalProxyRoot + path.TrimStart('/'), UriKind.Relative);
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed class SonnetProxyUnavailableException : Exception
    {
    }
}
