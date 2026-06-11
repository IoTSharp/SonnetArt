using System.Text.Json.Serialization;

namespace SonnetArt.ImageStudio.Models;

public sealed class SonnetAuthResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    [JsonPropertyName("expires_in")] public int? ExpiresIn { get; set; }
    [JsonPropertyName("token_type")] public string TokenType { get; set; } = "Bearer";
    [JsonPropertyName("user")] public SonnetUser? User { get; set; }
}

public sealed class SonnetRefreshTokenResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("token_type")] public string TokenType { get; set; } = "Bearer";
}

public sealed class SonnetUser
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("username")] public string UserName { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
    [JsonPropertyName("balance")] public decimal Balance { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
}

public sealed class SonnetGroup
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("platform")] public string Platform { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("subscription_type")] public string SubscriptionType { get; set; } = string.Empty;
}

public sealed class SonnetApiKey
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("group_id")] public long? GroupId { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }
    [JsonPropertyName("group")] public SonnetGroup? Group { get; set; }
}

public sealed class SonnetPaginatedResponse<T>
{
    [JsonPropertyName("items")] public List<T> Items { get; set; } = [];
    [JsonPropertyName("total")] public long Total { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("page_size")] public int PageSize { get; set; }
    [JsonPropertyName("pages")] public int Pages { get; set; }
}

public sealed class SonnetMethodLimit
{
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("daily_limit")] public decimal DailyLimit { get; set; }
    [JsonPropertyName("daily_used")] public decimal DailyUsed { get; set; }
    [JsonPropertyName("daily_remaining")] public decimal DailyRemaining { get; set; }
    [JsonPropertyName("single_min")] public decimal SingleMin { get; set; }
    [JsonPropertyName("single_max")] public decimal SingleMax { get; set; }
    [JsonPropertyName("fee_rate")] public decimal FeeRate { get; set; }
    [JsonPropertyName("available")] public bool Available { get; set; } = true;
}

public sealed class SonnetCheckoutInfo
{
    [JsonPropertyName("methods")] public Dictionary<string, SonnetMethodLimit> Methods { get; set; } = [];
    [JsonPropertyName("global_min")] public decimal GlobalMin { get; set; }
    [JsonPropertyName("global_max")] public decimal GlobalMax { get; set; }
    [JsonPropertyName("balance_disabled")] public bool BalanceDisabled { get; set; }
    [JsonPropertyName("balance_recharge_multiplier")] public decimal BalanceRechargeMultiplier { get; set; } = 1;
    [JsonPropertyName("recharge_fee_rate")] public decimal RechargeFeeRate { get; set; }
}

public sealed class SonnetCreateOrderResult
{
    [JsonPropertyName("order_id")] public long OrderId { get; set; }
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("pay_amount")] public decimal PayAmount { get; set; }
    [JsonPropertyName("fee_rate")] public decimal FeeRate { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("result_type")] public string? ResultType { get; set; }
    [JsonPropertyName("payment_type")] public string PaymentType { get; set; } = string.Empty;
    [JsonPropertyName("out_trade_no")] public string? OutTradeNo { get; set; }
    [JsonPropertyName("pay_url")] public string? PayUrl { get; set; }
    [JsonPropertyName("qr_code")] public string? QrCode { get; set; }
    [JsonPropertyName("client_secret")] public string? ClientSecret { get; set; }
    [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
    [JsonPropertyName("payment_mode")] public string? PaymentMode { get; set; }
    [JsonPropertyName("resume_token")] public string? ResumeToken { get; set; }
}

public sealed class SonnetPaymentOrder
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("pay_amount")] public decimal PayAmount { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("fee_rate")] public decimal FeeRate { get; set; }
    [JsonPropertyName("payment_type")] public string PaymentType { get; set; } = string.Empty;
    [JsonPropertyName("out_trade_no")] public string? OutTradeNo { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("order_type")] public string OrderType { get; set; } = string.Empty;
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
    [JsonPropertyName("paid_at")] public DateTimeOffset? PaidAt { get; set; }
    [JsonPropertyName("completed_at")] public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class SonnetRedeemResult
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("value")] public decimal Value { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("new_balance")] public decimal? NewBalance { get; set; }
    [JsonPropertyName("new_concurrency")] public int? NewConcurrency { get; set; }
    [JsonPropertyName("group_name")] public string? GroupName { get; set; }
    [JsonPropertyName("group")] public SonnetGroup? Group { get; set; }
    [JsonPropertyName("validity_days")] public int? ValidityDays { get; set; }
}

public sealed class SonnetEnsureApiKeyResult
{
    public bool Created { get; init; }
    public bool UsedFallbackGroup { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public long ApiKeyId { get; init; }
    public string ApiKeyName { get; init; } = string.Empty;
    public long? GroupId { get; init; }
    public string GroupName { get; init; } = string.Empty;
}
