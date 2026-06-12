using Microsoft.AspNetCore.Components;
using SonnetArt.Models;
using System.Collections.Specialized;
using System.Web;

namespace SonnetArt.Services;

public static class EmbeddedLaunchContextParser
{
    private static readonly string[] AccessTokenKeys =
    [
        "token",
        "access_token",
        "auth_token",
        "jwt",
        "bearer_token",
    ];

    public static EmbeddedLaunchContext Parse(NavigationManager navigation)
    {
        var uri = navigation.ToAbsoluteUri(navigation.Uri);
        var query = HttpUtility.ParseQueryString(uri.Query);

        return new EmbeddedLaunchContext
        {
            UserId = TryGetLong(query, "user_id"),
            AccessToken = GetFirst(query, AccessTokenKeys),
            RefreshToken = Get(query, "refresh_token"),
            ExpiresIn = TryGetInt(query, "expires_in"),
            Theme = Get(query, "theme"),
            Language = Get(query, "lang"),
            UiMode = Get(query, "ui_mode"),
            SourceHost = Get(query, "src_host"),
            SourceUrl = Get(query, "src_url"),
        };
    }

    private static string? Get(NameValueCollection query, string name)
    {
        return query[name]?.Trim();
    }

    private static long? TryGetLong(NameValueCollection query, string name)
    {
        return long.TryParse(Get(query, name), out var value) ? value : null;
    }

    private static int? TryGetInt(NameValueCollection query, string name)
    {
        return int.TryParse(Get(query, name), out var value) ? value : null;
    }

    private static string? GetFirst(NameValueCollection query, params string[] names)
    {
        foreach (var name in names)
        {
            var value = Get(query, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return NormalizeBearerToken(value);
            }
        }

        return null;
    }

    private static string NormalizeBearerToken(string value)
    {
        const string bearerPrefix = "Bearer ";
        return value.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? value[bearerPrefix.Length..].Trim()
            : value.Trim();
    }
}
