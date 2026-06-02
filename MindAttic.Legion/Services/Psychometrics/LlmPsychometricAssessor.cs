using System.Text;
using System.Text.Json;
using MindAttic.Legion.Providers;

namespace MindAttic.Legion;

/// <summary>
/// Administers the bundled instruments by having a single trusted LLM answer
/// each item <em>in character</em> as the persona, then scores the responses
/// deterministically. One LLM call per instrument (five per persona), each at
/// temperature 0 for repeatability. The model that answers is fixed for the
/// whole run — by default Claude at the High tier (Opus class), matching the
/// project's preference for architectural/persona work — so every persona is
/// measured on the same yardstick.
/// </summary>
public sealed class LlmPsychometricAssessor : IPsychometricAssessor
{
    private readonly LlmVotingProvider provider;
    private readonly string providerId;
    private readonly string modelId;
    private readonly int maxTokensPerInstrument;

    /// <summary>The provider id administering the assessment (e.g. "claude").</summary>
    public string ProviderId => providerId;

    /// <summary>The concrete model id resolved for the chosen tier (recorded on every profile).</summary>
    public string ModelId => modelId;

    /// <param name="provider">Transport used to call the model; resolves keys/timeout from its configuration.</param>
    /// <param name="providerId">Provider that administers the tests. Defaults to "claude".</param>
    /// <param name="tier">Capability tier; resolves to the concrete model. Defaults to <see cref="ModelTier.High"/> (Opus class).</param>
    /// <param name="maxTokensPerInstrument">Token budget per instrument call.</param>
    public LlmPsychometricAssessor(
        LlmVotingProvider provider,
        string providerId = "claude",
        ModelTier tier = ModelTier.High,
        int maxTokensPerInstrument = 1024)
    {
        this.provider = provider;
        this.providerId = providerId;
        this.maxTokensPerInstrument = maxTokensPerInstrument;
        modelId = LlmProviderCatalog.GetTieredModel(providerId, tier)
            ?? LegionClient.DefaultModels.GetValueOrDefault(providerId, "");
    }

    /// <inheritdoc />
    public async Task<PsychometricAssessment> AssessAsync(Persona persona, DateTime scoredAtUtc, CancellationToken ct = default)
    {
        var system = BuildSystemPrompt(persona);
        var raw = new Dictionary<string, IReadOnlyDictionary<int, int>>();

        // Pin the exact model so the recorded AdministeredByModel matches the call.
        var pin = new VoterProfile { ProviderId = providerId, ModelOverride = modelId };

        foreach (var instrument in PsychometricInstruments.All)
        {
            var reply = await provider.CallAsync(
                providerId,
                system,
                BuildUserPrompt(instrument),
                maxTokensPerInstrument,
                temperature: 0.0,
                voterOverrides: pin,
                ct);
            var parsed = ParseAnswers(reply, instrument);
            // Reject a reply that parsed to (almost) nothing — prose, a refusal,
            // or a truncated payload. Without this, the scorer fills every gap
            // with the scale midpoint and we'd persist a uniform 50/50 profile as
            // though it were a real assessment. Failing here surfaces the persona
            // as a failed slot the caller can retry, instead of silent garbage.
            if (parsed.Count * 2 < instrument.Items.Count)
                throw new InvalidOperationException(
                    $"{instrument.Key}: only {parsed.Count}/{instrument.Items.Count} answers parsed from the model reply.");
            raw[instrument.Key] = parsed;
        }

        var profile = PsychometricScorer.ScoreAll(
            persona.Id, raw, providerId, modelId, scoredAtUtc);

        return new PsychometricAssessment(profile, raw);
    }

    private static string BuildSystemPrompt(Persona persona)
    {
        if (string.IsNullOrWhiteSpace(persona.PersonalityMarkdown))
            return "You are completing a personality questionnaire. Answer honestly and consistently.";
        return persona.PersonalityMarkdown
            + "\n\nYou are now completing a personality questionnaire. Answer every item exactly as this person would.";
    }

    private static string BuildUserPrompt(PsychometricInstrument instrument)
    {
        var sb = new StringBuilder();
        sb.AppendLine(instrument.Instructions);
        sb.AppendLine();
        foreach (var item in instrument.Items)
            sb.Append(item.Id).Append(". ").AppendLine(item.Text);
        return sb.ToString();
    }

    /// <summary>
    /// Parse a model reply into item id → 1–5. Tolerant of the shapes models
    /// actually emit: {"answers":[{"id":1,"value":4}]}, {"answers":{"1":4}},
    /// a bare {"1":4} map, or a bare [4,2,...] array (mapped positionally to the
    /// instrument's item order). Unknown/out-of-range values are dropped, and
    /// the scorer fills any gaps with the scale midpoint.
    /// </summary>
    internal static IReadOnlyDictionary<int, int> ParseAnswers(string reply, PsychometricInstrument instrument)
    {
        var result = new Dictionary<int, int>();

        // Try an object payload first (the requested shape), then a bare array.
        var obj = LegionJson.ExtractObject(reply);
        if (obj != "{}")
        {
            try
            {
                using var doc = JsonDocument.Parse(obj);
                var root = doc.RootElement;
                if (root.TryGetProperty("answers", out var ans))
                    ReadAnswers(ans, result);
                else
                    ReadAnswers(root, result); // bare {id:value} map
                if (result.Count > 0) return result;
            }
            catch (JsonException) { /* fall through to array handling */ }
        }

        var arr = LegionJson.ExtractArray(reply);
        if (arr != "[]")
        {
            try
            {
                using var doc = JsonDocument.Parse(arr);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var items = instrument.Items;
                    var idx = 0;
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        if (idx >= items.Count) break;
                        if (el.ValueKind is JsonValueKind.Object)
                            ReadAnswers(el, result); // [{id,value}, ...]
                        else if (TryReadInt(el, out var v))
                            result[items[idx].Id] = v; // positional [4,2,...]
                        idx++;
                    }
                }
            }
            catch (JsonException) { /* leave result as-is */ }
        }

        return result;
    }

    private static void ReadAnswers(JsonElement element, Dictionary<int, int> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var el in element.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    if (el.TryGetProperty("id", out var idEl) && TryReadInt(idEl, out var id)
                        && el.TryGetProperty("value", out var valEl) && TryReadInt(valEl, out var val))
                        into[id] = val;
                }
                break;
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                    if (int.TryParse(prop.Name, out var id) && TryReadInt(prop.Value, out var val))
                        into[id] = val;
                break;
        }
    }

    private static bool TryReadInt(JsonElement el, out int value)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Number when el.TryGetInt32(out value):
                return true;
            case JsonValueKind.Number when el.TryGetDouble(out var d):
                value = (int)Math.Round(d);
                return true;
            case JsonValueKind.String when int.TryParse(el.GetString(), out value):
                return true;
            default:
                value = 0;
                return false;
        }
    }
}
