using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SonnetHost.Configuration;

namespace SonnetHost.StudioStorage;

public sealed class SonnetArtIdentityResolver
{
    private const string AnonymousCookieName = "sonnetart.sid";
    private const string AuthSessionCookieName = "sonnetart.auth";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SonnetArtDbContext _db;
    private readonly IOptions<SonnetArtHostOptions> _options;
    private readonly ILogger<SonnetArtIdentityResolver> _logger;

    public SonnetArtIdentityResolver(
        IHttpClientFactory httpClientFactory,
        SonnetArtDbContext db,
        IOptions<SonnetArtHostOptions> options,
        ILogger<SonnetArtIdentityResolver> logger)
    {
        _httpClientFactory = httpClientFactory;
        _db = db;
        _options = options;
        _logger = logger;
    }

    public async Task<StudioStorageIdentity?> ResolveAsync(HttpContext context)
    {
        var token = TryGetBearerToken(context.Request.Headers.Authorization.ToString());
        if (!string.IsNullOrWhiteSpace(token))
        {
            var user = await TryResolveUserAsync(token, context.RequestAborted);
            if (user is not null)
            {
                var identity = new StudioStorageIdentity(
                    $"user:{user.Id}",
                    user.Id,
                    HashNullable(user.Email),
                    DeviceKeyHash: null);
                await EnsureAuthSessionAsync(context, identity);
                return identity;
            }
        }

        var sessionIdentity = await TryResolveAuthSessionAsync(context);
        if (sessionIdentity is not null)
        {
            return sessionIdentity;
        }

        var anonymousKey = ResolveAnonymousKey(context);
        var anonymousHash = Hash(anonymousKey);
        return new StudioStorageIdentity(
            $"anonymous:{anonymousHash[..32]}",
            UserId: null,
            EmailHash: null,
            DeviceKeyHash: anonymousHash);
    }

    public async Task ClearAuthSessionAsync(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(AuthSessionCookieName, out var sessionId) &&
            IsValidAnonymousKey(sessionId))
        {
            var sessionHash = Hash(sessionId);
            var session = await _db.AuthSessions
                .SingleOrDefaultAsync(item => item.SessionIdHash == sessionHash, context.RequestAborted);
            if (session is not null)
            {
                _db.AuthSessions.Remove(session);
                await _db.SaveChangesAsync(context.RequestAborted);
            }
        }

        context.Response.Cookies.Delete(AuthSessionCookieName);
    }

    private async Task EnsureAuthSessionAsync(HttpContext context, StudioStorageIdentity identity)
    {
        if (identity.UserId is null)
        {
            return;
        }

        var sessionId = context.Request.Cookies.TryGetValue(AuthSessionCookieName, out var existing) &&
            IsValidAnonymousKey(existing)
                ? existing
                : "sas_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var sessionHash = Hash(sessionId);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expires = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds();

        var session = await _db.AuthSessions
            .SingleOrDefaultAsync(item => item.SessionIdHash == sessionHash, context.RequestAborted);
        if (session is null)
        {
            session = new StudioAuthSessionRecord
            {
                SessionIdHash = sessionHash,
                CreatedAtUnixMs = now,
            };
            _db.AuthSessions.Add(session);
        }

        session.OwnerKey = identity.OwnerKey;
        session.OwnerUserId = identity.UserId.Value;
        session.OwnerEmailHash = identity.EmailHash;
        session.UpdatedAtUnixMs = now;
        session.ExpiresAtUnixMs = expires;
        await _db.SaveChangesAsync(context.RequestAborted);

        context.Response.Cookies.Append(
            AuthSessionCookieName,
            sessionId,
            CreateCookieOptions(DateTimeOffset.FromUnixTimeMilliseconds(expires), context.Request.IsHttps));
    }

    private async Task<StudioStorageIdentity?> TryResolveAuthSessionAsync(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(AuthSessionCookieName, out var sessionId) ||
            !IsValidAnonymousKey(sessionId))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var sessionHash = Hash(sessionId);
        var session = await _db.AuthSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.SessionIdHash == sessionHash, context.RequestAborted);
        if (session is null || session.ExpiresAtUnixMs <= now)
        {
            context.Response.Cookies.Delete(AuthSessionCookieName);
            return null;
        }

        return new StudioStorageIdentity(
            session.OwnerKey,
            session.OwnerUserId,
            session.OwnerEmailHash,
            DeviceKeyHash: null);
    }

    private static string ResolveAnonymousKey(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(AnonymousCookieName, out var existing) &&
            IsValidAnonymousKey(existing))
        {
            return existing;
        }

        var created = "sa_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        context.Response.Cookies.Append(
            AnonymousCookieName,
            created,
            CreateCookieOptions(DateTimeOffset.UtcNow.AddYears(2), context.Request.IsHttps));
        return created;
    }

    private async Task<SonnetUserProjection?> TryResolveUserAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(_options.Value.ResolveAccountUpstreamUri(), "api/v1/user/profile");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var client = _httpClientFactory.CreateClient("sonnet-storage-auth");
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("sub2api profile lookup returned {StatusCode}.", response.StatusCode);
                return null;
            }

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            var data = UnwrapData(JsonNode.Parse(raw));
            if (data is not JsonObject obj ||
                !TryGetLong(obj, "id", out var id) ||
                id <= 0)
            {
                return null;
            }

            return new SonnetUserProjection(id, TryGetString(obj, "email"));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "sub2api profile lookup failed.");
            return null;
        }
    }

    private static JsonNode? UnwrapData(JsonNode? root)
    {
        if (root is not JsonObject obj)
        {
            return root;
        }

        if (obj.TryGetPropertyValue("code", out var code) &&
            code is JsonValue codeValue &&
            codeValue.TryGetValue<int>(out var codeNumber) &&
            codeNumber != 0)
        {
            return null;
        }

        return obj["data"] ?? root;
    }

    private static string? TryGetBearerToken(string authorization)
    {
        const string bearerPrefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authorization[bearerPrefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private static bool IsValidAnonymousKey(string value)
    {
        if (value.Length is < 16 or > 128)
        {
            return false;
        }

        return value.All(static ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');
    }

    private static CookieOptions CreateCookieOptions(DateTimeOffset expires, bool secure) => new()
    {
        HttpOnly = true,
        IsEssential = true,
        SameSite = SameSiteMode.Lax,
        Secure = secure,
        Expires = expires,
    };

    private static bool TryGetLong(JsonObject obj, string name, out long value)
    {
        value = 0;
        return obj.TryGetPropertyValue(name, out var node) &&
            node is JsonValue jsonValue &&
            jsonValue.TryGetValue(out value);
    }

    private static string? TryGetString(JsonObject obj, string name)
    {
        return obj.TryGetPropertyValue(name, out var node) &&
            node is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out var value) &&
            !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null;
    }

    private static string? HashNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Hash(value.Trim().ToLowerInvariant());

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record SonnetUserProjection(long Id, string? Email);
}
