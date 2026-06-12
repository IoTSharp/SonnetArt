using System.Text.RegularExpressions;

namespace SonnetArt.Models;

public sealed class PromptTemplate
{
    public PromptTemplate(string prompt, IReadOnlyList<PromptTemplateSlot> slots)
    {
        Prompt = prompt;
        Slots = slots;
    }

    public string Prompt { get; }
    public IReadOnlyList<PromptTemplateSlot> Slots { get; }
    public bool HasSlots => Slots.Count > 0;

    public string Compile(IReadOnlyDictionary<string, string> values)
    {
        var compiled = Prompt;
        foreach (var slot in Slots)
        {
            var value = values.TryGetValue(slot.Name, out var current)
                ? current
                : slot.DefaultValue;

            foreach (var token in slot.Tokens)
            {
                compiled = compiled.Replace(token, value, StringComparison.Ordinal);
            }
        }

        return compiled.Trim();
    }
}

public sealed class PromptTemplateSlot
{
    public string Name { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Type { get; init; } = "string";
    public string DefaultValue { get; set; } = string.Empty;
    public bool Required { get; init; } = true;
    public List<string> Tokens { get; } = [];
    public IReadOnlyList<string> Options { get; init; } = [];

    public bool IsBoolean => string.Equals(Type, "boolean", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "bool", StringComparison.OrdinalIgnoreCase);

    public bool IsNumber => string.Equals(Type, "number", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "integer", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "int", StringComparison.OrdinalIgnoreCase);

    public bool IsLongText => string.Equals(Type, "longText", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "textarea", StringComparison.OrdinalIgnoreCase) ||
        DefaultValue.Contains('\n') ||
        DefaultValue.Length > 72;
}

public static class PromptTemplateParser
{
    private static readonly Regex MustachePattern = new(
        @"\{\{\s*(?<name>[A-Za-z][A-Za-z0-9_. -]{0,80})\s*\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ArgumentPattern = new(
        @"\{argument\s+(?<attrs>[^{}]+)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex AttributePattern = new(
        @"(?<key>[A-Za-z][A-Za-z0-9_-]*)\s*=\s*(?:""(?<dq>(?:\\.|[^""])*)""|'(?<sq>(?:\\.|[^'])*)'|(?<bare>[^\s]+))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static PromptTemplate Parse(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new PromptTemplate(string.Empty, []);
        }

        var slots = new Dictionary<string, PromptTemplateSlot>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in MustachePattern.Matches(prompt))
        {
            var name = match.Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            AddSlot(slots, name, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), match.Value);
        }

        foreach (Match match in ArgumentPattern.Matches(prompt))
        {
            var attributes = ParseAttributes(match.Groups["attrs"].Value);
            if (!attributes.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            AddSlot(slots, name.Trim(), attributes, match.Value);
        }

        return new PromptTemplate(prompt, slots.Values.ToArray());
    }

    private static void AddSlot(
        Dictionary<string, PromptTemplateSlot> slots,
        string name,
        IReadOnlyDictionary<string, string> attributes,
        string token)
    {
        if (!slots.TryGetValue(name, out var slot))
        {
            var defaultValue = attributes.TryGetValue("default", out var defaultText)
                ? defaultText
                : string.Empty;
            slot = new PromptTemplateSlot
            {
                Name = name,
                Label = ResolveLabel(name, attributes),
                Type = ResolveType(name, defaultValue, attributes),
                DefaultValue = defaultValue,
                Required = !attributes.TryGetValue("required", out var required) ||
                    !string.Equals(required, "false", StringComparison.OrdinalIgnoreCase),
                Options = ResolveOptions(attributes),
            };
            slots.Add(name, slot);
        }
        else if (string.IsNullOrWhiteSpace(slot.DefaultValue) &&
            attributes.TryGetValue("default", out var defaultText))
        {
            slot.DefaultValue = defaultText;
        }

        if (!slot.Tokens.Contains(token, StringComparer.Ordinal))
        {
            slot.Tokens.Add(token);
        }
    }

    private static Dictionary<string, string> ParseAttributes(string value)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributePattern.Matches(value))
        {
            var key = match.Groups["key"].Value.Trim();
            var attributeValue =
                match.Groups["dq"].Success ? match.Groups["dq"].Value :
                match.Groups["sq"].Success ? match.Groups["sq"].Value :
                match.Groups["bare"].Value;
            attributes[key] = Regex.Unescape(attributeValue.Trim());
        }

        return attributes;
    }

    private static string ResolveLabel(string name, IReadOnlyDictionary<string, string> attributes)
    {
        if (attributes.TryGetValue("label", out var label) && !string.IsNullOrWhiteSpace(label))
        {
            return label.Trim();
        }

        return name.Replace('_', ' ').Replace('-', ' ').Trim();
    }

    private static string ResolveType(
        string name,
        string defaultValue,
        IReadOnlyDictionary<string, string> attributes)
    {
        if (attributes.TryGetValue("type", out var type) && !string.IsNullOrWhiteSpace(type))
        {
            return type.Trim();
        }

        if (defaultValue.Contains('\n') ||
            name.Contains("prompt", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("description", StringComparison.OrdinalIgnoreCase))
        {
            return "longText";
        }

        return "string";
    }

    private static IReadOnlyList<string> ResolveOptions(IReadOnlyDictionary<string, string> attributes)
    {
        if (!attributes.TryGetValue("options", out var options) || string.IsNullOrWhiteSpace(options))
        {
            return [];
        }

        return options
            .Split(['|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
