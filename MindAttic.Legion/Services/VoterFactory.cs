namespace MindAttic.Legion;

/// <summary>
/// Builds <see cref="VoterProfile"/> panels by spreading personas across LLM providers.
/// This is the standard entry point when an application asks Legion for "N voters" and
/// doesn't want to hand-craft each one.
/// </summary>
public static class VoterFactory
{
    /// <summary>
    /// Generates <paramref name="count"/> voters whose personas are all distinct.
    ///
    /// Provider-spread strategy: every entry in <paramref name="availableProviderIds"/>
    /// is used at least once before any provider gets a second voter. When
    /// <paramref name="count"/> exceeds the number of available providers, the
    /// remaining slots are filled with <paramref name="fallbackProviderId"/>
    /// (default <c>"claude"</c>) so the panel always reaches the requested size.
    /// If no provider IDs are supplied, every voter uses the fallback.
    ///
    /// Persona-spread strategy: personas are sampled WITHOUT replacement from
    /// <see cref="PersonaLibrary"/>, so no two voters in the same batch share a
    /// persona. Pass a seeded <see cref="Random"/> for deterministic panels in tests.
    /// </summary>
    public static IReadOnlyList<VoterProfile> GenerateUniqueVoters(
        int count,
        IEnumerable<string> availableProviderIds,
        string fallbackProviderId = "claude",
        Random? rng = null)
    {
        if (count <= 0) return Array.Empty<VoterProfile>();
        rng ??= Random.Shared;

        var providers = (availableProviderIds ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(fallbackProviderId))
            fallbackProviderId = "claude";

        var personas = PersonaLibrary.Sample(count, rng);
        var voters = new VoterProfile[Math.Min(count, personas.Count)];
        for (int i = 0; i < voters.Length; i++)
        {
            var providerId = i < providers.Count ? providers[i] : fallbackProviderId;
            voters[i] = new VoterProfile
            {
                VoterId = personas[i].Id + "-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Name = personas[i].Name,
                ProviderId = providerId,
                PersonalityMarkdown = personas[i].PersonalityMarkdown,
            };
        }
        return voters;
    }
}
