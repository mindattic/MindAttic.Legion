namespace MindAttic.Legion;

/// <summary>
/// Helpers for pulling a JSON value out of a noisy LLM reply that may wrap the
/// payload in prose or markdown code fences. Shared by the voting service and
/// the psychometric assessor so both slice replies identically.
/// </summary>
internal static class LegionJson
{
    /// <summary>Extract the longest balanced top-level JSON object, or <c>"{}"</c> on miss.</summary>
    public static string ExtractObject(string text) => ExtractBalanced(text, '{', '}', "{}");

    /// <summary>Extract the longest balanced top-level JSON array, or <c>"[]"</c> on miss.</summary>
    public static string ExtractArray(string text) => ExtractBalanced(text, '[', ']', "[]");

    /// <summary>
    /// Returns the longest balanced <paramref name="open"/>…<paramref name="close"/>
    /// region in <paramref name="text"/>. Tracks nesting depth and skips bracket
    /// characters that occur inside JSON string literals (honouring backslash
    /// escapes), so prose such as <c>"see note {1}: {\"decision\":\"Yes\"}"</c> —
    /// or a closing brace embedded in a string value — no longer mis-slices the
    /// JSON. Picking the longest top-level region means a stray <c>{1}</c> in the
    /// preamble loses to the real (larger) object. Falls back to a first-open…
    /// last-close slice for a truncated reply, and returns
    /// <paramref name="emptySentinel"/> when nothing matches.
    /// </summary>
    public static string ExtractBalanced(string text, char open, char close, string emptySentinel)
    {
        if (string.IsNullOrEmpty(text)) return emptySentinel;

        int bestStart = -1, bestLen = 0;
        int curStart = -1, depth = 0;
        bool inString = false, escaped = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }

            if (c == open)
            {
                if (depth == 0) curStart = i;
                depth++;
            }
            else if (c == close && depth > 0)
            {
                depth--;
                if (depth == 0 && curStart >= 0)
                {
                    var len = i - curStart + 1;
                    if (len > bestLen) { bestLen = len; bestStart = curStart; }
                }
            }
        }

        if (bestStart >= 0) return text.Substring(bestStart, bestLen);

        // No complete balanced region (e.g. a truncated reply) — fall back to the
        // naive first-open…last-close slice so a mostly-complete object still has
        // a chance to parse.
        var fallbackStart = text.IndexOf(open);
        var fallbackEnd   = text.LastIndexOf(close);
        return fallbackStart >= 0 && fallbackEnd > fallbackStart
            ? text[fallbackStart..(fallbackEnd + 1)]
            : emptySentinel;
    }
}
