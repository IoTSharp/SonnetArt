using SonnetArt.Models;

namespace SonnetArt.Services;

public static class ImageModelCatalog
{
    private static readonly IReadOnlyList<string> GptImage2BaseSizes =
    [
        "1024x1024",
        "1024x1360",
        "1024x1536",
        "1024x1792",
        "1536x1024",
        "1360x1024",
        "1792x1024",
        "1792x768",
    ];

    private static readonly IReadOnlyList<string> GptImage2Sizes =
    [
        ..GptImage2BaseSizes,
        "2048x2048",
        "1536x2048",
        "1360x2048",
        "1152x2048",
        "2048x1360",
        "2048x1536",
        "2048x1152",
        "2048x896",
        "2880x2880",
        "2448x3264",
        "2336x3504",
        "2160x3840",
        "3504x2336",
        "3264x2448",
        "3840x2160",
        "3840x1648",
    ];

    private static readonly IReadOnlyList<string> GptQualities = ["auto", "high", "medium", "low"];
    private static readonly IReadOnlyList<string> ImageFormats = ["png", "jpeg", "webp"];
    private static readonly IReadOnlyList<string> GptImage2Backgrounds = ["auto", "opaque"];

    public static ImageModelCapabilities Get(string? model, string? mode = null)
    {
        var normalizedModel = NormalizeModel(model);

        return new(
            normalizedModel,
            GptImage2Sizes,
            GptQualities,
            ImageFormats,
            GptImage2Backgrounds,
            [],
            [],
            SupportsCustomSize: true,
            SupportsOutputFormat: true,
            SupportsOutputCompression: true,
            SupportsBackground: true,
            SupportsModeration: true,
            SupportsStream: false,
            SupportsPartialImages: false,
            SupportsInputFidelity: false,
            SizeNote: "最高约 8.29MP / 最长边 3840px；不是 7680px 真 8K。");
    }

    public static StudioSettings NormalizeSettings(StudioSettings settings, string? mode = null)
    {
        var capabilities = Get(settings.Model, mode);
        settings.Model = capabilities.Model;
        settings.Size = NormalizeSize(settings.Size, capabilities.Sizes, capabilities.Sizes[0]);
        settings.Quality = NormalizeChoice(settings.Quality, capabilities.Qualities, capabilities.Qualities[0]);
        settings.Format = NormalizeChoice(settings.Format, capabilities.OutputFormats, "png");
        settings.Background = NormalizeChoice(settings.Background, capabilities.Backgrounds, "auto");
        settings.Style = NormalizeChoice(settings.Style, capabilities.Styles, "默认");
        settings.ResponseFormat = NormalizeChoice(settings.ResponseFormat, capabilities.ResponseFormats, "b64_json");

        if (!capabilities.SupportsStream)
        {
            settings.RequestMode = "sync";
            settings.PartialImages = 0;
        }
        else
        {
            settings.RequestMode = string.Equals(settings.RequestMode, "stream", StringComparison.OrdinalIgnoreCase)
                ? "stream"
                : "sync";
            settings.PartialImages = Math.Clamp(settings.PartialImages, 0, 3);
        }

        if (!capabilities.SupportsInputFidelity)
        {
            settings.InputFidelity = "默认";
        }

        return settings;
    }

    public static string NormalizeModel(string? model)
    {
        var normalized = model?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "gpt-image-2";
        }

        return normalized.Equals("gpt-image-2", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("gpt-image-2-", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : "gpt-image-2";
    }

    private static string NormalizeChoice(string? value, IReadOnlyList<string> options, string fallback)
    {
        if (options.Count == 0)
        {
            return fallback;
        }

        var trimmed = value?.Trim();
        return options.FirstOrDefault(option => string.Equals(option, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? fallback;
    }

    public static string NormalizeSize(string? value, IReadOnlyList<string> options, string fallback)
    {
        if (options.Count == 0)
        {
            return fallback;
        }

        var trimmed = value?.Trim();
        var exact = options.FirstOrDefault(option => string.Equals(option, trimmed, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(exact))
        {
            return exact;
        }

        var dimensions = TryParseSize(trimmed);
        if (dimensions is null)
        {
            return fallback;
        }

        var targetRatio = Math.Log((double)dimensions.Value.Width / dimensions.Value.Height);
        return options
            .Select(option => new { Option = option, Dimensions = TryParseSize(option) })
            .Where(item => item.Dimensions is not null)
            .OrderBy(item => Math.Abs(Math.Log((double)item.Dimensions!.Value.Width / item.Dimensions.Value.Height) - targetRatio))
            .ThenBy(item => Math.Abs((item.Dimensions!.Value.Width * item.Dimensions.Value.Height) - (dimensions.Value.Width * dimensions.Value.Height)))
            .Select(item => item.Option)
            .FirstOrDefault()
            ?? fallback;
    }

    private static (int Width, int Height)? TryParseSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var width) ||
            !int.TryParse(parts[1], out var height) ||
            width <= 0 ||
            height <= 0)
        {
            return null;
        }

        return (width, height);
    }
}

public sealed record ImageModelCapabilities(
    string Model,
    IReadOnlyList<string> Sizes,
    IReadOnlyList<string> Qualities,
    IReadOnlyList<string> OutputFormats,
    IReadOnlyList<string> Backgrounds,
    IReadOnlyList<string> Styles,
    IReadOnlyList<string> ResponseFormats,
    bool SupportsCustomSize,
    bool SupportsOutputFormat,
    bool SupportsOutputCompression,
    bool SupportsBackground,
    bool SupportsModeration,
    bool SupportsStream,
    bool SupportsPartialImages,
    bool SupportsInputFidelity,
    string SizeNote);
