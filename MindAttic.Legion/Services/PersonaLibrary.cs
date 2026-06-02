using System.Reflection;
using System.Text.Json;

namespace MindAttic.Legion;

/// <summary>
/// Library of baked-in voter personas: exactly 1024 personas built from the
/// cross-product of 16 archetypes × 8 worldviews × 8 cultural backgrounds, each
/// further enriched with a deterministic age, pronouns, and a signature quirk so
/// even neighbouring entries feel like distinct people. There are no per-provider
/// "default" entries — a bare LLM has no persona, so an instruction-free model is
/// simply a <see cref="VoterProfile"/> with an empty personality, not a library
/// member.
///
/// Every persona has a unique id, name, and personality prompt.
/// <see cref="Sample(int, Random?)"/> draws WITHOUT replacement, so any panel
/// built via <see cref="VoterFactory"/> never repeats a persona inside a single
/// batch. The list is materialized lazily on first use and cached for the
/// process lifetime.
/// </summary>
public static class PersonaLibrary
{
    /// <summary>The 40 vocational archetypes — first axis of the 40×16×16 persona space.</summary>
    private static readonly string[] Archetypes =
    {
        "retired schoolteacher",
        "emergency-room nurse",
        "trial lawyer",
        "independent farmer",
        "software engineer",
        "small-business owner",
        "parish priest",
        "long-haul truck driver",
        "graduate student",
        "former military officer",
        "union machinist",
        "hospice social worker",
        "investigative journalist",
        "commercial airline pilot",
        "restaurant line cook",
        "field research biologist",
        "construction foreman",
        "pediatric surgeon",
        "high-school football coach",
        "tenured philosophy professor",
        "freelance graphic designer",
        "county sheriff's deputy",
        "commercial fisherman",
        "jazz musician",
        "911 dispatcher",
        "wind-turbine technician",
        "corporate accountant",
        "midwife",
        "museum curator",
        "auto mechanic",
        "venture capitalist",
        "daycare provider",
        "oil-rig roughneck",
        "data scientist",
        "funeral director",
        "vineyard owner",
        "air-traffic controller",
        "social-media influencer",
        "stay-at-home parent",
        "retired firefighter",
    };

    /// <summary>The 16 worldviews — second axis; shapes how the persona reasons.</summary>
    private static readonly string[] Worldviews =
    {
        "cautious traditionalist",
        "impatient pragmatist",
        "dry-witted skeptic",
        "soft-spoken idealist",
        "data-driven empiricist",
        "religious moralist",
        "blunt populist",
        "quietly anxious worrier",
        "relentless optimist",
        "contrarian gadfly",
        "stoic fatalist",
        "starry-eyed dreamer",
        "cynical realist",
        "principled libertarian",
        "communitarian collectivist",
        "restless reformer",
    };

    /// <summary>The 16 cultural backgrounds — third axis; shapes voice and references.</summary>
    private static readonly string[] Backgrounds =
    {
        "rural Midwestern",
        "coastal urban",
        "first-generation immigrant",
        "Southern small-town",
        "New England Yankee",
        "Texan",
        "multi-generational Californian",
        "Appalachian",
        "Pacific Northwest",
        "Mid-Atlantic suburban",
        "Gulf Coast bayou",
        "Rust Belt industrial town",
        "Mountain West ranching",
        "Great Plains prairie",
        "inner-city Northeast",
        "desert Southwest borderlands",
    };

    /// <summary>The pronoun sets cycled through personas (one per persona, deterministic by index) — the female and male perspectives.</summary>
    private static readonly string[] PronounSets =
    {
        "she/her", "he/him",
    };

    /// <summary>
    /// 50 signature quirks rotated through the 1024 personas (~20 personas per
    /// quirk). Each persona's prompt names exactly one quirk so even adjacent
    /// entries in the catalog read like distinct people.
    /// </summary>
    private static readonly string[] Quirks =
    {
        "Refuses to use first names with people they don't trust.",
        "Carries a hand-written notebook everywhere and quotes from it.",
        "Has strong opinions about coffee that they always announce.",
        "Insists on calling things by their original technical names.",
        "Tells the same three stories at every gathering.",
        "Won't engage with arguments shorter than a paragraph.",
        "Makes every decision after a full night's sleep — no exceptions.",
        "Loves analogies involving farm animals and rarely picks the obvious one.",
        "Quotes their grandmother on every other point.",
        "Refuses to give a yes/no answer until they've heard the cost.",
        "Treats every conversation like a deposition.",
        "Has a personal calendar of saints' days and references them.",
        "Reads every fine print and assumes you didn't.",
        "Believes most problems are scheduling problems.",
        "Quietly judges people's posture.",
        "Argues by sketching diagrams in the air.",
        "Always asks 'who pays for it?' before any other question.",
        "Lives by 'measure twice, cut once' — applied to everything.",
        "Refuses to use rideshares because they like to map their own routes.",
        "Will mention a relevant historical battle whenever possible.",
        "Treats every pause in conversation like an invitation to elaborate.",
        "Has preserved their father's vocabulary, accent and all.",
        "Insists on the Oxford comma and won't let it go.",
        "Believes every meeting could have been an email.",
        "Keeps a running tally of broken promises.",
        "Makes lists. Always. Even mid-conversation.",
        "Has switched political parties twice and tells you why every time.",
        "Won't trust a number that wasn't sourced.",
        "Mentions their service in the war (whichever one) within five minutes.",
        "Has named every plant in their house.",
        "Reads obituaries first thing every morning and notes patterns.",
        "Frames every decision in terms of weather.",
        "Refers to their spouse exclusively by an old nickname.",
        "Believes in 'sleeping on it' for any sum over $200.",
        "Quotes Shakespeare just often enough to seem accidental.",
        "Treats waiters and CEOs identically — for better or worse.",
        "Knows the average rainfall of every county they've lived in.",
        "Insists they 'never get sick' but always has a remedy ready.",
        "Has memorized obscure baseball stats and deploys them tactically.",
        "Reads three newspapers before breakfast on principle.",
        "Refuses to enter rooms they haven't been invited into.",
        "Carries a multitool and finds an excuse to use it.",
        "Has a pet theory about why everything's a supply-chain problem.",
        "Believes the answer is usually in the original-language version.",
        "Whispers when they're delivering the most important point.",
        "Uses fishing metaphors when arguing about anything indoors.",
        "Has strong feelings about the proper way to fold a flag.",
        "Refuses to write in cursive and looks down on those who can't.",
        "Believes the room temperature is always 2 degrees off.",
        "Recites poetry they wrote in high school as if it's still their best work.",
    };

    /// <summary>The fixed persona count: 1024 unique combinations sampled from the 40×16×16 space.</summary>
    public const int EnrichedCount = 1024;

    /// <summary>Fixed seed for the deterministic combination sample — change it and every persona changes.</summary>
    private const uint SampleSeed = 0x9E3779B1;

    private static readonly Lazy<(IReadOnlyList<Persona> personas, IReadOnlyList<PersonaDetail> details)> enrichedData = new(BuildEnriched);

    /// <summary>The 1024 enriched personas built from the diversity skeleton.</summary>
    public static IReadOnlyList<Persona> Enriched => enrichedData.Value.personas;

    /// <summary>
    /// The full persona library — exactly the 1024 <see cref="Enriched"/> personas.
    /// A bare LLM has no persona, so there are no per-provider "default" entries.
    /// </summary>
    public static IReadOnlyList<Persona> All => enrichedData.Value.personas;

    /// <summary>
    /// Structured metadata for every persona in <see cref="All"/>, aligned by
    /// index and id: the cube axes and enrichments behind each prompt. Lets
    /// persistence and analytics query by archetype/worldview/background without
    /// re-parsing the prompt string.
    /// </summary>
    public static IReadOnlyList<PersonaDetail> AllDetails => enrichedData.Value.details;

    private static readonly Lazy<IReadOnlyDictionary<string, PsychometricProfile>> profiles = new(LoadProfiles);

    /// <summary>
    /// The latest psychometric profile per persona, keyed by <see cref="Persona.Id"/>
    /// (OCEAN/HEXACO/MBTI/Enneagram/DISC + provenance). Embedded in the package so
    /// consumers get profile-carrying personas with no external data source —
    /// pair with <see cref="VoterFactory.GenerateDiverseVoters"/> to build
    /// trait-diverse panels. Lazily deserialized on first access. A persona not
    /// present here simply hasn't been scored.
    /// </summary>
    public static IReadOnlyDictionary<string, PsychometricProfile> Profiles => profiles.Value;

    /// <summary>The psychometric profile for a persona id, or null if it hasn't been scored.</summary>
    public static PsychometricProfile? GetProfile(string id) =>
        Profiles.TryGetValue(id, out var p) ? p : null;

    /// <summary>Deserialize the embedded <c>psychometric-profiles.json</c> (id → profile).</summary>
    private static IReadOnlyDictionary<string, PsychometricProfile> LoadProfiles()
    {
        var asm = typeof(PersonaLibrary).Assembly;
        var resource = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("psychometric-profiles.json", StringComparison.OrdinalIgnoreCase));
        if (resource is null) return new Dictionary<string, PsychometricProfile>();

        using var stream = asm.GetManifestResourceStream(resource)!;
        var profiles = JsonSerializer.Deserialize<Dictionary<string, PsychometricProfile>>(
            stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return profiles ?? new Dictionary<string, PsychometricProfile>();
    }

    /// <summary>Number of personas in the library (the 1024 enriched personas).</summary>
    public static int Count => All.Count;

    /// <summary>Returns the persona at the supplied index in [0, Count).</summary>
    public static Persona Get(int index)
    {
        if (index < 0 || index >= Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"Index must be in [0, {Count}).");
        return All[index];
    }

    /// <summary>
    /// Returns <paramref name="count"/> distinct personas drawn at random WITHOUT replacement.
    /// If <paramref name="count"/> exceeds <see cref="Count"/>, the full library is returned in
    /// random order. Pass a seeded <see cref="Random"/> for reproducible sampling.
    /// </summary>
    public static IReadOnlyList<Persona> Sample(int count, Random? rng = null)
    {
        if (count <= 0) return Array.Empty<Persona>();
        rng ??= Random.Shared;
        var total = Count;
        var take = Math.Min(count, total);

        // Fisher-Yates partial shuffle on indices: O(take), no allocation per item.
        var indices = new int[total];
        for (int i = 0; i < total; i++) indices[i] = i;
        for (int i = 0; i < take; i++)
        {
            int j = rng.Next(i, total);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }
        var result = new Persona[take];
        for (int i = 0; i < take; i++) result[i] = All[indices[i]];
        return result;
    }

    /// <summary>
    /// Materializes 1024 personas by deterministically sampling 1024 distinct
    /// (archetype × worldview × background) combinations from the full 40×16×16
    /// space (10,240 combinations) — a fixed-seed Fisher–Yates draw, so the same
    /// 1024 are chosen on every build and machine. Each is enriched with a
    /// deterministic age, pronoun set, and signature quirk so it reads as a
    /// distinct individual. Sampling (rather than enumerating the whole cube)
    /// lets every axis carry a rich vocabulary without blowing past 1024.
    /// </summary>
    private static (IReadOnlyList<Persona> personas, IReadOnlyList<PersonaDetail> details) BuildEnriched()
    {
        int A = Archetypes.Length, W = Worldviews.Length, B = Backgrounds.Length;
        int total = A * W * B;

        // Deterministic Fisher–Yates over every combination index using a fixed
        // seed (own LCG, not System.Random — stable across .NET versions/machines),
        // then take the first EnrichedCount and sort for a stable, readable order.
        var order = new int[total];
        for (int k = 0; k < total; k++) order[k] = k;
        uint seed = SampleSeed;
        for (int k = total - 1; k > 0; k--)
        {
            seed = unchecked(seed * 1664525u + 1013904223u);
            int j = (int)(seed % (uint)(k + 1));
            (order[k], order[j]) = (order[j], order[k]);
        }
        var selected = new int[EnrichedCount];
        Array.Copy(order, selected, EnrichedCount);
        Array.Sort(selected);

        var personas = new Persona[EnrichedCount];
        var details = new PersonaDetail[EnrichedCount];
        int female = 0, male = 0;   // sequential draws from the gendered name pools
        for (int i = 0; i < EnrichedCount; i++)
        {
            int combo = selected[i];
            int a = combo / (W * B);
            int w = (combo / B) % W;
            int b = combo % B;

            var id = $"persona-{i:0000}";

            // Deterministic age in 18-80 (working-adult demographic), pronoun set + quirk rotated.
            var age = 18 + ((i * 17) % 63);
            var pronouns = PronounSets[i % PronounSets.Length];
            // Unique first name matching the pronoun's perspective — no last initial.
            var name = pronouns == "she/her" ? PersonaNames.Female[female++] : PersonaNames.Male[male++];
            var quirk = Quirks[i % Quirks.Length];

            var prompt =
$@"You are {name}, age {age} ({pronouns}). You are {Article(Worldviews[w])} {Worldviews[w]} {Archetypes[a]} from {Article(Backgrounds[b])} {Backgrounds[b]} background.
Signature trait: {quirk}
Speak in your own voice with conviction. Bring the perspective your life would actually shape — values, blind spots, and all. Don't break character.
Be concise. 2-3 sentences max.";
            personas[i] = new Persona(id, name, prompt);
            details[i] = new PersonaDetail(
                id, Archetypes[a], Worldviews[w], Backgrounds[b], age, pronouns, quirk,
                IsDefault: false, ProviderId: null);
        }
        return (personas, details);
    }

    /// <summary>Indefinite article ("a"/"an") for the following word, by its first letter.</summary>
    private static string Article(string word) =>
        word.Length > 0 && "aeiouAEIOU".IndexOf(word[0]) >= 0 ? "an" : "a";
}
