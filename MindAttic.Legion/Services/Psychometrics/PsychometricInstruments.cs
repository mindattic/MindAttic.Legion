namespace MindAttic.Legion;

/// <summary>
/// One item on a psychometric instrument: a statement the respondent rates on a
/// Likert scale. <see cref="Reverse"/> means agreement counts <em>against</em>
/// the item's <see cref="Scale"/> (a reverse-keyed item, or — for the bipolar
/// MBTI-style axes — agreement that leans toward the second pole).
/// </summary>
/// <param name="Id">Globally unique item id across all instruments (1–110). Stable so raw responses are auditable.</param>
/// <param name="Text">The statement the persona rates.</param>
/// <param name="Scale">The trait/axis/type this item loads onto (e.g. "O", "EI", "D", "7").</param>
/// <param name="Reverse">True when agreement subtracts from the scale rather than adds.</param>
public sealed record PsychometricItem(int Id, string Text, string Scale, bool Reverse = false);

/// <summary>
/// A bundled, Likert-scored psychometric instrument: an ordered item bank plus
/// the response range and the in-character instructions shown to the persona.
/// </summary>
/// <param name="Key">Stable instrument key: "bigfive", "hexaco", "mbti", "disc", "enneagram".</param>
/// <param name="DisplayName">Human-readable name for CLI/logs.</param>
/// <param name="Min">Lowest valid response value (1 = strongly disagree).</param>
/// <param name="Max">Highest valid response value (5 = strongly agree).</param>
/// <param name="Instructions">Framing prepended to the item list when the persona is asked to respond.</param>
/// <param name="Items">The item bank in presentation order.</param>
public sealed record PsychometricInstrument(
    string Key,
    string DisplayName,
    int Min,
    int Max,
    string Instructions,
    IReadOnlyList<PsychometricItem> Items);

/// <summary>
/// The bundled, public-domain-derived item banks for the five frameworks Legion
/// scores: Big Five/OCEAN (Mini-IPIP), HEXACO (IPIP-derived), MBTI-style
/// (open Jungian, OEJTS-style), DISC-style, and Enneagram-style. All items use a
/// 1–5 Likert scale; scoring is computed deterministically by
/// <see cref="PsychometricScorer"/> — the LLM only answers items, it never
/// computes its own scores. Bump <see cref="SetVersion"/> whenever any item
/// changes so persisted profiles remain comparable across runs.
/// </summary>
public static class PsychometricInstruments
{
    /// <summary>Version of this item-bank set. Stored on every profile so re-runs are comparable.</summary>
    public const string SetVersion = "1.0.0";

    /// <summary>The common 1–5 Likert response range shared by every bundled instrument.</summary>
    public const int LikertMin = 1;
    public const int LikertMax = 5;

    private const string LikertInstructions =
        "Answer each statement AS THIS PERSON, on a 1–5 scale where " +
        "1 = strongly disagree, 2 = disagree, 3 = neutral, 4 = agree, 5 = strongly agree. " +
        "Stay fully in character — answer how this person honestly would, blind spots and all. " +
        "Do not explain. Return ONLY a JSON object of the form " +
        "{\"answers\":[{\"id\":1,\"value\":4}, ...]} with one entry per item id.";

    /// <summary>Big Five / OCEAN — the 20-item Mini-IPIP (4 items per domain). Scales: O C E A N.</summary>
    public static readonly PsychometricInstrument BigFive = new(
        "bigfive", "Big Five (Mini-IPIP)", LikertMin, LikertMax, LikertInstructions,
        new PsychometricItem[]
        {
            new(1,  "I am the life of the party.", "E"),
            new(2,  "I sympathize with others' feelings.", "A"),
            new(3,  "I get chores done right away.", "C"),
            new(4,  "I have frequent mood swings.", "N"),
            new(5,  "I have a vivid imagination.", "O"),
            new(6,  "I don't talk a lot.", "E", Reverse: true),
            new(7,  "I am not interested in other people's problems.", "A", Reverse: true),
            new(8,  "I often forget to put things back in their proper place.", "C", Reverse: true),
            new(9,  "I am relaxed most of the time.", "N", Reverse: true),
            new(10, "I am not interested in abstract ideas.", "O", Reverse: true),
            new(11, "I talk to a lot of different people at parties.", "E"),
            new(12, "I feel others' emotions.", "A"),
            new(13, "I like order.", "C"),
            new(14, "I get upset easily.", "N"),
            new(15, "I have difficulty understanding abstract ideas.", "O", Reverse: true),
            new(16, "I keep in the background.", "E", Reverse: true),
            new(17, "I am not really interested in others.", "A", Reverse: true),
            new(18, "I make a mess of things.", "C", Reverse: true),
            new(19, "I seldom feel blue.", "N", Reverse: true),
            new(20, "I do not have a good imagination.", "O", Reverse: true),
        });

    /// <summary>HEXACO — 24 IPIP-derived items (4 per factor). Scales: H E X A C O.</summary>
    public static readonly PsychometricInstrument Hexaco = new(
        "hexaco", "HEXACO (IPIP-derived)", LikertMin, LikertMax, LikertInstructions,
        new PsychometricItem[]
        {
            // Honesty-Humility
            new(21, "I wouldn't use flattery to get a raise or promotion, even if I thought it would work.", "H"),
            new(22, "If I knew I could never get caught, I would be willing to steal a large sum of money.", "H", Reverse: true),
            new(23, "Having a lot of money is not especially important to me.", "H"),
            new(24, "I would get a lot of pleasure from owning expensive luxury goods.", "H", Reverse: true),
            // Emotionality
            new(25, "I sometimes can't help worrying about little things.", "E"),
            new(26, "I rarely, if ever, feel anxious or afraid.", "E", Reverse: true),
            new(27, "When something bad happens to me, I need someone to comfort me.", "E"),
            new(28, "I stay unemotional even in situations where most people get sentimental.", "E", Reverse: true),
            // eXtraversion
            new(29, "I feel that I am an unpopular person.", "X", Reverse: true),
            new(30, "On most days, I feel cheerful and optimistic.", "X"),
            new(31, "I enjoy being the center of attention at social gatherings.", "X"),
            new(32, "In social situations, I'm usually the one who makes the first move.", "X"),
            // Agreeableness
            new(33, "I rarely hold a grudge, even against people who have badly wronged me.", "A"),
            new(34, "People sometimes tell me that I am too critical of others.", "A", Reverse: true),
            new(35, "I am usually willing to compromise when people disagree with me.", "A"),
            new(36, "I find it hard to forgive people who have hurt me.", "A", Reverse: true),
            // Conscientiousness
            new(37, "I plan ahead and organize things to avoid scrambling at the last minute.", "C"),
            new(38, "I push myself very hard when trying to achieve a goal.", "C"),
            new(39, "I often check my work carefully for mistakes.", "C"),
            new(40, "I make a lot of careless mistakes.", "C", Reverse: true),
            // Openness
            new(41, "I would enjoy creating a work of art, such as a story, a song, or a painting.", "O"),
            new(42, "I'm interested in learning about the history and politics of other countries.", "O"),
            new(43, "I don't think of myself as the artistic or creative type.", "O", Reverse: true),
            new(44, "I find it boring to discuss philosophy or abstract ideas.", "O", Reverse: true),
        });

    /// <summary>MBTI-style — 24 bipolar items (6 per axis). Scales: EI SN TF JP. Reverse = leans to second pole (I/N/F/P).</summary>
    public static readonly PsychometricInstrument Mbti = new(
        "mbti", "Jungian Type (OEJTS-style)", LikertMin, LikertMax, LikertInstructions,
        new PsychometricItem[]
        {
            // Extraversion (E) vs Introversion (I)
            new(45, "I feel energized after spending time with a group of people.", "EI"),
            new(46, "I prefer a quiet evening alone to a lively party.", "EI", Reverse: true),
            new(47, "I often start conversations with strangers.", "EI"),
            new(48, "I find it draining to be around people for too long.", "EI", Reverse: true),
            new(49, "I think out loud and talk through my ideas with others.", "EI"),
            new(50, "I keep my thoughts to myself until they're fully formed.", "EI", Reverse: true),
            // Sensing (S) vs iNtuition (N)
            new(51, "I focus on concrete facts more than theories or possibilities.", "SN"),
            new(52, "I am drawn to abstract ideas and what could be.", "SN", Reverse: true),
            new(53, "I trust experience and proven methods over hunches.", "SN"),
            new(54, "I often notice patterns and hidden meanings in things.", "SN", Reverse: true),
            new(55, "I pay close attention to practical, here-and-now details.", "SN"),
            new(56, "I like to imagine future possibilities more than dwell on present realities.", "SN", Reverse: true),
            // Thinking (T) vs Feeling (F)
            new(57, "I make decisions based on logic rather than how people will feel.", "TF"),
            new(58, "I value harmony and try hard to avoid hurting others' feelings.", "TF", Reverse: true),
            new(59, "I can stay detached and objective when judging a situation.", "TF"),
            new(60, "I am strongly moved by other people's circumstances.", "TF", Reverse: true),
            new(61, "I think it is more important to be truthful than tactful.", "TF"),
            new(62, "I weigh how a choice affects people more than whether it is strictly correct.", "TF", Reverse: true),
            // Judging (J) vs Perceiving (P)
            new(63, "I like to have things planned and settled well in advance.", "JP"),
            new(64, "I prefer to keep my options open and decide as I go.", "JP", Reverse: true),
            new(65, "I make lists and follow schedules.", "JP"),
            new(66, "I work in spontaneous bursts rather than steady routines.", "JP", Reverse: true),
            new(67, "I feel uneasy when things are left unresolved.", "JP"),
            new(68, "I enjoy being flexible and adapting plans at the last minute.", "JP", Reverse: true),
        });

    /// <summary>DISC-style — 24 items (6 per dimension), agreement loads onto the dimension. Scales: D I S C.</summary>
    public static readonly PsychometricInstrument Disc = new(
        "disc", "DISC-style", LikertMin, LikertMax, LikertInstructions,
        new PsychometricItem[]
        {
            // Dominance
            new(69, "I am direct and forceful about getting results.", "D"),
            new(70, "I am comfortable taking charge and making quick decisions.", "D"),
            new(71, "I am willing to confront problems head-on.", "D"),
            new(72, "I push hard to win and dislike losing.", "D"),
            new(73, "I get impatient when things move too slowly.", "D"),
            new(74, "I am blunt about what I want.", "D"),
            // Influence
            new(75, "I enjoy meeting new people and making them feel at ease.", "I"),
            new(76, "I am enthusiastic and like to inspire others.", "I"),
            new(77, "I talk easily and persuasively.", "I"),
            new(78, "I would rather work with people than work alone.", "I"),
            new(79, "I am optimistic and tend to see the bright side.", "I"),
            new(80, "I like being the center of social energy.", "I"),
            // Steadiness
            new(81, "I am patient and a good listener.", "S"),
            new(82, "I prefer steady, predictable routines over sudden change.", "S"),
            new(83, "I go out of my way to support and accommodate others.", "S"),
            new(84, "I stay calm and even-tempered under pressure.", "S"),
            new(85, "I value loyalty and long-term relationships.", "S"),
            new(86, "I dislike conflict and try to keep the peace.", "S"),
            // Conscientiousness (compliance)
            new(87, "I pay close attention to accuracy and detail.", "C"),
            new(88, "I prefer to follow rules and proven procedures.", "C"),
            new(89, "I research carefully before making a decision.", "C"),
            new(90, "I hold myself and others to high standards of quality.", "C"),
            new(91, "I prefer clear instructions and well-defined expectations.", "C"),
            new(92, "I double-check my work to make sure it is correct.", "C"),
        });

    /// <summary>Enneagram-style — 18 items (2 per type), agreement loads onto the type. Scales: "1".."9".</summary>
    public static readonly PsychometricInstrument Enneagram = new(
        "enneagram", "Enneagram-style", LikertMin, LikertMax, LikertInstructions,
        new PsychometricItem[]
        {
            new(93,  "I have a strong sense of right and wrong and feel responsible for improving things.", "1"),
            new(94,  "I get frustrated when things are done sloppily or incorrectly.", "1"),
            new(95,  "I focus on other people's needs, sometimes more than my own.", "2"),
            new(96,  "I feel most valued when people need and appreciate my help.", "2"),
            new(97,  "I am driven to succeed and to be seen as successful.", "3"),
            new(98,  "I adapt how I present myself to make the best impression.", "3"),
            new(99,  "I feel things deeply and often sense that I am different from others.", "4"),
            new(100, "I long for what is missing and can romanticize what I don't have.", "4"),
            new(101, "I prefer to observe and understand things before getting involved.", "5"),
            new(102, "I guard my time and energy and need plenty of privacy.", "5"),
            new(103, "I scan for what could go wrong so that I can be prepared.", "6"),
            new(104, "I value security and loyalty, and I look to trusted sources for guidance.", "6"),
            new(105, "I seek out new experiences and like to keep my options exciting.", "7"),
            new(106, "I avoid pain and boredom by staying busy with fun possibilities.", "7"),
            new(107, "I am assertive and protective, and I dislike being controlled.", "8"),
            new(108, "I confront things directly and take charge when others won't.", "8"),
            new(109, "I go along with others to keep the peace and avoid conflict.", "9"),
            new(110, "I find it hard to assert my own priorities and can become complacent.", "9"),
        });

    /// <summary>All five instruments, in administration order.</summary>
    public static readonly IReadOnlyList<PsychometricInstrument> All =
        new[] { BigFive, Hexaco, Mbti, Disc, Enneagram };

    /// <summary>Total item count across every instrument (currently 110).</summary>
    public static int TotalItemCount => All.Sum(i => i.Items.Count);

    /// <summary>Look up an instrument by its <see cref="PsychometricInstrument.Key"/>; null if unknown.</summary>
    public static PsychometricInstrument? Get(string key) =>
        All.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));
}
