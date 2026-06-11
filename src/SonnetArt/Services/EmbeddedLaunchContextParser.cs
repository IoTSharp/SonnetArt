using Microsoft.AspNetCore.Components;
using SonnetArt.Models;
using System.Collections.Specialized;
using System.Web;

namespace SonnetArt.Services;

public static class EmbeddedLaunchContextParser
{
    public static EmbeddedLaunchContext Parse(NavigationManager navigation)
    {
        var uri = navigation.ToAbsoluteUri(navigation.Uri);
        var query = HttpUtility.ParseQueryString(uri.Query);

        return new EmbeddedLaunchContext
        {
            UserId = TryGetLong(query, "user_id"),
            AccessToken = Get(query, "token"),
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
}
