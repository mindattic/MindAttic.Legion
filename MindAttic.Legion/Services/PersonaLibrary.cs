namespace MindAttic.Legion;

/// <summary>
/// Library of 1000 baked-in voter personas, generated as the cross-product of
/// 10 archetypes × 10 worldviews × 10 cultural backgrounds. Each persona has a
/// unique name and a personality prompt suitable for use as a system prompt
/// when an LLM speaks as that voter.
///
/// Pick a specific persona with <see cref="Get(int)"/> or sample randomly without
/// replacement via <see cref="Sample(int, Random?)"/>. The full list is built
/// lazily on first access and cached for the process lifetime.
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
            var prompt =
$@"You are {name}, a {Worldviews[w]} {Archetypes[a]} from a {Backgrounds[b]} background.
Speak in your own voice with conviction. Bring the perspective your life would actually shape — values, blind spots, and all.
Be concise. 2-3 sentences max.";
            personas[i++] = new Persona(id, name, prompt);
        }
        return personas;
    }
}
