using System.Text.Json.Serialization;

namespace SonnetArt.Models;

public sealed class SiteBranding
{
    public string Name { get; init; } = "SonnetArt";
    public string Description { get; init; } = "AI image studio";
    public string? IconUrl { get; init; }

    public static SiteBranding Default { get; } = new();
}

public sealed class EmbeddedLaunchContext
{
    public long? UserId { get; init; }
    public string? AccessToken { get; init; }
    public string? Theme { get; init; }
    public string? Language { get; init; }
    public string? UiMode { get; init; }
    public string? SourceHost { get; init; }
    public string? SourceUrl { get; init; }

    [JsonIgnore]
    public bool IsEmbedded => string.Equals(UiMode, "embedded", StringComparison.OrdinalIgnoreCase);
}
