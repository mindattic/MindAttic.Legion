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

        var providers = NormalizeProviders(availableProviderIds);

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

    /// <summary>
    /// Like <see cref="GenerateUniqueVoters"/>, but chooses personas to maximize
    /// <em>psychometric diversity</em> rather than sampling at random — so a small
    /// panel spans the trait space (Openness, Extraversion, DISC style, …) instead
    /// of clustering. Only personas present in <paramref name="profiles"/> are
    /// eligible; each chosen voter carries its <see cref="VoterProfile.Psychometrics"/>.
    ///
    /// Selection is greedy farthest-point over the normalized OCEAN+HEXACO+DISC
    /// vector: a random seed persona, then repeatedly the persona furthest from
    /// everyone already chosen. When no profiles are supplied it degrades
    /// gracefully to <see cref="GenerateUniqueVoters"/>.
    /// </summary>
    public static IReadOnlyList<VoterProfile> GenerateDiverseVoters(
        int count,
        IEnumerable<string> availableProviderIds,
        IReadOnlyDictionary<string, PsychometricProfile> profiles,
        string fallbackProviderId = "claude",
        Random? rng = null)
    {
        if (count <= 0) return Array.Empty<VoterProfile>();
        rng ??= Random.Shared;
        if (profiles is null || profiles.Count == 0)
            return GenerateUniqueVoters(count, availableProviderIds, fallbackProviderId, rng);

        var providers = NormalizeProviders(availableProviderIds);
        if (string.IsNullOrWhiteSpace(fallbackProviderId)) fallbackProviderId = "claude";

        // Eligible candidates: library personas that have a profile, in stable order.
        var candidates = PersonaLibrary.All
            .Where(p => profiles.ContainsKey(p.Id))
            .Select(p => (persona: p, vec: TraitVector(profiles[p.Id])))
            .ToList();

        var take = Math.Min(count, candidates.Count);
        var selected = new List<int>(take);
        var chosen = new HashSet<int>();

        // Seed deterministically with the most extreme persona — the one
        // farthest from the panel centroid — so a small panel always anchors on
        // an edge of the trait space rather than its crowded middle. (Ties → the
        // lowest index, keeping diverse panels reproducible.)
        var first = FarthestFromCentroid(candidates.Select(c => c.vec).ToList());
        selected.Add(first);
        chosen.Add(first);

        // minDist[i] = distance from candidate i to the nearest already-selected.
        var minDist = new double[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
            minDist[i] = Distance(candidates[i].vec, candidates[first].vec);

        while (selected.Count < take)
        {
            var best = -1;
            var bestD = double.NegativeInfinity;
            for (var i = 0; i < candidates.Count; i++)
            {
                if (chosen.Contains(i)) continue;
                if (minDist[i] > bestD) { bestD = minDist[i]; best = i; }
            }
            if (best < 0) break;
            selected.Add(best);
            chosen.Add(best);
            for (var i = 0; i < candidates.Count; i++)
            {
                var d = Distance(candidates[i].vec, candidates[best].vec);
                if (d < minDist[i]) minDist[i] = d;
            }
        }

        var voters = new VoterProfile[selected.Count];
        for (var k = 0; k < selected.Count; k++)
        {
            var c = candidates[selected[k]];
            voters[k] = new VoterProfile
            {
                VoterId = c.persona.Id + "-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Name = c.persona.Name,
                ProviderId = k < providers.Count ? providers[k] : fallbackProviderId,
                PersonalityMarkdown = c.persona.PersonalityMarkdown,
                Psychometrics = profiles[c.persona.Id],
            };
        }
        return voters;
    }

    /// <summary>Normalize a provider-id list: trim, drop blanks, de-dup case-insensitively.</summary>
    private static List<string> NormalizeProviders(IEnumerable<string> availableProviderIds) =>
        (availableProviderIds ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The 15-D trait vector (OCEAN + HEXACO + DISC) used for diversity spacing.</summary>
    private static double[] TraitVector(PsychometricProfile p) =>
    [
        p.Ocean.Openness, p.Ocean.Conscientiousness, p.Ocean.Extraversion, p.Ocean.Agreeableness, p.Ocean.Neuroticism,
        p.Hexaco.HonestyHumility, p.Hexaco.Emotionality, p.Hexaco.Extraversion, p.Hexaco.Agreeableness, p.Hexaco.Conscientiousness, p.Hexaco.Openness,
        p.Disc.Dominance, p.Disc.Influence, p.Disc.Steadiness, p.Disc.Conscientiousness,
    ];

    private static double Distance(double[] a, double[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++) { var d = a[i] - b[i]; sum += d * d; }
        return Math.Sqrt(sum);
    }

    /// <summary>Index of the vector farthest from the set's centroid; ties → lowest index.</summary>
    private static int FarthestFromCentroid(IReadOnlyList<double[]> vectors)
    {
        var dims = vectors[0].Length;
        var centroid = new double[dims];
        foreach (var v in vectors)
            for (var i = 0; i < dims; i++) centroid[i] += v[i];
        for (var i = 0; i < dims; i++) centroid[i] /= vectors.Count;

        var best = 0;
        var bestD = Distance(vectors[0], centroid);
        for (var i = 1; i < vectors.Count; i++)
        {
            var d = Distance(vectors[i], centroid);
            if (d > bestD) { bestD = d; best = i; }
        }
        return best;
    }
}
