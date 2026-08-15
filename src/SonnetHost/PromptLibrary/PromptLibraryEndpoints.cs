using SonnetArt.Models;

namespace SonnetHost.PromptLibrary;

public static class PromptLibraryEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 60;
    private const string ImageProxyPrefix = "/api/prompt-library/images/";

    public static void MapPromptLibraryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/prompt-library", (RequestDelegate)(async context =>
        {
            var queryValues = context.Request.Query;
            var query = new PromptLibraryQuery(
                Math.Max(1, ParseNullableInt(queryValues["page"].ToString()) ?? 1),
                Math.Clamp(ParseNullableInt(queryValues["pageSize"].ToString()) ?? DefaultPageSize, 1, MaxPageSize),
                ParseLanguage(queryValues["language"].ToString()),
                Normalize(queryValues["source"].ToString()),
                Normalize(queryValues["category"].ToString()),
                Normalize(queryValues["search"].ToString()));

            var store = context.RequestServices.GetRequiredService<PromptLibraryStore>();
            var result = await store.QueryAsync(query, context.RequestAborted);
            await Results.Json(result).ExecuteAsync(context);
        }));

        app.MapGet($"{ImageProxyPrefix}{{id}}/{{index:int}}", (RequestDelegate)(async context =>
        {
            var id = Convert.ToString(context.Request.RouteValues["id"], System.Globalization.CultureInfo.InvariantCulture);
            var index = ParseNullableInt(Convert.ToString(context.Request.RouteValues["index"], System.Globalization.CultureInfo.InvariantCulture)) ?? -1;
            if (string.IsNullOrWhiteSpace(id) || index < 0)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var store = context.RequestServices.GetRequiredService<PromptLibraryStore>();
            var image = await store.OpenImageAsync(id, index, context.RequestAborted);
            if (image is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = image.ContentType;
            context.Response.Headers.CacheControl = "public, max-age=86400";
            await using var stream = image.Stream;
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
        }));
    }

    private static int? ParseNullableInt(string? value)
    {
        return int.TryParse(
            value,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var result)
            ? result
            : null;
    }

    private static PromptLibraryLanguage ParseLanguage(string? language)
    {
        return Enum.TryParse<PromptLibraryLanguage>(language, ignoreCase: true, out var value)
            ? value
            : PromptLibraryLanguage.Chinese;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
