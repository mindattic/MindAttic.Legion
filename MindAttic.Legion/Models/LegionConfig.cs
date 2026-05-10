using System.Text.Json;
using System.Text.Json.Serialization;

namespace MindAttic.Legion;

/// <summary>
/// Per-project voting configuration. Loaded from a <c>legion.json</c> file at
/// the project root so each app declares its own voter panel without code
/// changes. Examples:
///
/// <code>
/// // ThinkTank (every provider):
/// { "voters": ["claude","openai","gemini","deepseek","mistral","xai",
///              "groq","together","openrouter","fireworks","cohere"] }
///
/// // Tutor (two-vendor panel):
/// { "voters": ["openai","claude"], "judge": "claude" }
/// </code>
///
/// When <c>legion.json</c> is missing, all defaults from
/// <see cref="VotingConfiguration"/> apply.
/// </summary>
public class LegionConfig
{
    public const string FileName = "legion.json";

    /// <summary>Provider IDs eligible to vote in this project.</summary>
    [JsonPropertyName("voters")]
    public List<string> Voters { get; set; } = new();

    /// <summary>
    /// Provider that synthesizes consensus from free-form votes.
    /// Falls back to first voter when not set.
    /// </summary>
    [JsonPropertyName("judge")]
    public string? Judge { get; set; }

    /// <summary>Per-provider model overrides (e.g., "claude" → "claude-opus-4-7").</summary>
    [JsonPropertyName("models")]
    public Dictionary<string, string> Models { get; set; } = new();

    /// <summary>
    /// Optional per-provider API key map. Useful for projects that ship their
    /// own keys (e.g., a public demo using a sandbox key). Empty = resolve from
    /// the shared MindAttic credential store.
    /// </summary>
    [JsonPropertyName("apiKeys")]
    public Dictionary<string, string> ApiKeys { get; set; } = new();

    /// <summary>
    /// Walks up from <paramref name="startDir"/> looking for legion.json.
    /// Returns null when not found (caller should fall back to defaults).
    /// </summary>
    public static LegionConfig? LoadFromDirectory(string? startDir = null)
    {
        var dir = startDir ?? Environment.CurrentDirectory;
        if (string.IsNullOrWhiteSpace(dir)) return null;

        try
        {
            var current = new DirectoryInfo(dir);
            for (int depth = 0; depth < 12 && current != null; depth++)
            {
                var path = Path.Combine(current.FullName, FileName);
                if (File.Exists(path)) return LoadFromFile(path);
                current = current.Parent;
            }
        }
        catch { /* malformed path or permission issue — fall through to null */ }
        return null;
    }

    public static LegionConfig? LoadFromFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LegionConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch { return null; }
    }

    /// <summary>
    /// Apply this config on top of an existing <see cref="VotingConfiguration"/>.
    /// Voters list replaces the default whitelist; judge and model overrides are
    /// merged in. Per-project API keys win over shared-store keys.
    /// </summary>
    public void ApplyTo(VotingConfiguration cfg)
    {
        if (Voters is { Count: > 0 })
        {
            cfg.AllowedProviderIds = new HashSet<string>(Voters, StringComparer.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(Judge))
        {
            cfg.JudgeProviderId = Judge;
        }

        foreach (var kv in Models)
        {
            if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                cfg.ModelOverrides[kv.Key] = kv.Value;
        }

        foreach (var kv in ApiKeys)
        {
            if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                cfg.ApiKeys[kv.Key] = kv.Value;
        }
    }
}
