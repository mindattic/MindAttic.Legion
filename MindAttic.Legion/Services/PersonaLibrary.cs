namespace MindAttic.Legion;

/// <summary>
/// Library of 1000 baked-in voter personas. The base diversity skeleton is the
/// cross-product of 10 archetypes × 10 worldviews × 10 cultural backgrounds;
/// each persona is further enriched with a deterministic age, pronouns, and a
/// signature quirk so even neighbouring entries feel like distinct people.
///
/// All 1000 personas have unique ids, unique names, and unique personality
/// prompts. <see cref="Sample(int, Random?)"/> draws WITHOUT replacement, so
/// any panel built via <see cref="VoterFactory"/> never repeats a persona
/// inside a single batch. The full list is materialized lazily on first use
/// and cached for the process lifetime.
/// </summary>
public static class PersonaLibrary
{
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
    };

    private static readonly string[] Worldviews =
    {
        "cautious traditionalist",
        "impatient pragmatist",
        "dry-witted skeptic",
        "relentless optimist",
        "contrarian gadfly",
        "soft-spoken idealist",
        "data-driven empiricist",
        "religious moralist",
        "blunt populist",
        "quietly anxious worrier",
    };

    private static readonly string[] Backgrounds =
    {
        "rural Midwestern",
        "coastal urban",
        "first-generation immigrant",
        "Southern small-town",
        "Pacific Northwest",
        "New England Yankee",
        "Texan",
        "multi-generational Californian",
        "Appalachian",
        "Mid-Atlantic suburban",
    };

    // 100 first names spanning genders, generations, and origins.
    // Combined with 10 letter suffixes (A.-J.) below, this yields 1000 unique names.
    private static readonly string[] FirstNames =
    {
        "Margaret","Paul","Elaine","Roger","Iris","Curtis","Henrietta","Vincent","Joan","Samuel",
        "Beverly","Wallace","Phyllis","Edgar","Ramona","Otis","Yvonne","Stanley","Lillian","Bernard",
        "Rosa","Gerald","Frances","Lloyd","Mabel","Marvin","Adelaide","Roy","Hazel","Floyd",
        "Imani","Jamal","Priya","Diego","Mei","Hassan","Sofia","Kenji","Anika","Eduardo",
        "Aisha","Dmitri","Naledi","Tariq","Yuki","Olamide","Camila","Rashid","Nadia","Mateo",
        "Brenda","Keith","Doris","Lester","Marlene","Harold","Joyce","Ralph","Eleanor","Walter",
        "Tasha","Jerome","Letitia","Maurice","Tabitha","Reginald","Geraldine","Cornelius","Loretta","Theodore",
        "Sage","River","Quinn","Ash","Rowan","Phoenix","Arden","Jules","Skylar","Wren",
        "Bertha","Norman","Edna","Clarence","Hilda","Wilbur","Gladys","Homer","Mavis","Ernest",
        "Tomoko","Ranjit","Inara","Thabo","Sibel","Olufemi","Xiao","Demetri","Halimah","Zlatan",
    };

    private static readonly string[] PronounSets =
    {
        "she/her", "he/him", "they/them",
    };

    // 50 signature quirks rotated through the 1000 personas (20 personas per quirk).
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

    /// <summary>The fixed total persona count: 10 × 10 × 10.</summary>
    public const int Total = 1000;

    private static readonly Lazy<IReadOnlyList<Persona>> all = new(BuildAll);

    /// <summary>The full set of 1000 personas in deterministic order.</summary>
    public static IReadOnlyList<Persona> All => all.Value;

    /// <summary>Number of personas in the library.</summary>
    public static int Count => Total;

    /// <summary>Returns the persona at the supplied index in [0, Count).</summary>
    public static Persona Get(int index)
    {
        if (index < 0 || index >= Total)
            throw new ArgumentOutOfRangeException(nameof(index), $"Index must be in [0, {Total}).");
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
        var take = Math.Min(count, Total);

        // Fisher-Yates partial shuffle on indices: O(take), no allocation per item.
        var indices = new int[Total];
        for (int i = 0; i < Total; i++) indices[i] = i;
        for (int i = 0; i < take; i++)
        {
            int j = rng.Next(i, Total);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }
        var result = new Persona[take];
        for (int i = 0; i < take; i++) result[i] = All[indices[i]];
        return result;
    }

    private static IReadOnlyList<Persona> BuildAll()
    {
        var personas = new Persona[Total];
        int i = 0;
        for (int a = 0; a < Archetypes.Length; a++)
        for (int w = 0; w < Worldviews.Length; w++)
        for (int b = 0; b < Backgrounds.Length; b++)
        {
            var first = FirstNames[i % FirstNames.Length];
            var initial = (char)('A' + (i / FirstNames.Length));
            var name = $"{first} {initial}.";
            var id = $"persona-{i:0000}";

            // Deterministic age in 22-78 (57-year span), pronouns rotated, signature quirk rotated.
            var age = 22 + ((i * 7) % 57);
            var pronouns = PronounSets[i % PronounSets.Length];
            var quirk = Quirks[i % Quirks.Length];

            var prompt =
$@"You are {name}, age {age} ({pronouns}). You are a {Worldviews[w]} {Archetypes[a]} from a {Backgrounds[b]} background.
Signature trait: {quirk}
Speak in your own voice with conviction. Bring the perspective your life would actually shape — values, blind spots, and all. Don't break character.
Be concise. 2-3 sentences max.";
            personas[i++] = new Persona(id, name, prompt);
        }
        return personas;
    }
}
