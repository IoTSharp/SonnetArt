namespace SonnetHost.Configuration;

public sealed class SonnetArtHostOptions
{
    public const string SectionName = "SonnetArt";

    public string? PublicOrigin { get; set; }

    public string? AiUpstreamUrl { get; set; }

    public string? AccountUpstreamUrl { get; set; }

    public string? SonnetDbConnection { get; set; }

    public bool PromptImageWarmup { get; set; } = true;

    public Uri ResolveAiUpstreamUri() => ResolveAbsoluteUri(AiUpstreamUrl, nameof(AiUpstreamUrl));

    public Uri ResolveAccountUpstreamUri() => ResolveAbsoluteUri(AccountUpstreamUrl, nameof(AccountUpstreamUrl));

    public string ResolveListenUrl()
    {
        var origin = RequireConfigured(PublicOrigin, nameof(PublicOrigin));
        return origin.StartsWith(":", StringComparison.Ordinal)
            ? $"http://+{origin}"
            : origin;
    }

    public string ResolveSonnetDbConnectionString()
    {
        return RequireConfigured(SonnetDbConnection, nameof(SonnetDbConnection));
    }

    public void Validate()
    {
        _ = ResolveListenUrl();
        _ = ResolveAiUpstreamUri();
        _ = ResolveAccountUpstreamUri();
        _ = ResolveSonnetDbConnectionString();
    }

    private static Uri ResolveAbsoluteUri(string? value, string optionName)
    {
        var root = RequireConfigured(value, optionName);
        if (!root.EndsWith('/'))
        {
            root += "/";
        }

        if (!Uri.TryCreate(root, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"{SectionName}:{optionName} must be an absolute http or https URL.");
        }

        return uri;
    }

    private static string RequireConfigured(string? value, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{SectionName}:{optionName} must be configured.");
        }

        return value.Trim();
    }
}
