namespace MindAttic.Legion;

/// <summary>
/// Segments a completed <see cref="VotingResult"/> by the psychometric
/// composition of the panel — e.g. "did high-Openness voters split from
/// low-Openness ones?". Pure and read-only: it joins each vote to its voter's
/// <see cref="VoterProfile.Psychometrics"/> by voter id and tallies decisions
/// per segment, leaving the voting engine itself untouched. Voters without a
/// profile (or error votes) are ignored.
/// </summary>
public static class PsychometricVoteAnalysis
{
    /// <summary>
    /// Tally decisions per segment, where <paramref name="segmentOf"/> maps a
    /// voter's profile to a segment label. Returns segment → (decision → count).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Segment(
        IEnumerable<VoterProfile> voters,
        VotingResult result,
        Func<PsychometricProfile, string> segmentOf)
    {
        var profileByVoter = voters
            .Where(v => v.Psychometrics is not null)
            .ToDictionary(v => v.VoterId, v => v.Psychometrics!);

        var segments = new Dictionary<string, Dictionary<string, int>>();
        foreach (var vote in result.IndividualVotes)
        {
            if (vote.IsError) continue;
            if (!profileByVoter.TryGetValue(vote.VoterId, out var profile)) continue;

            var segment = segmentOf(profile);
            var decision = vote.Decision ?? "";
            if (!segments.TryGetValue(segment, out var dist))
                segments[segment] = dist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            dist[decision] = dist.GetValueOrDefault(decision) + 1;
        }

        return segments.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyDictionary<string, int>)kv.Value);
    }

    /// <summary>Segment label: the voter's MBTI-style type (e.g. "INTJ").</summary>
    public static Func<PsychometricProfile, string> ByMbtiType => p => p.Mbti.Type;

    /// <summary>Segment label: "DISC-D"/"DISC-I"/"DISC-S"/"DISC-C".</summary>
    public static Func<PsychometricProfile, string> ByDiscPrimary => p => $"DISC-{p.Disc.PrimaryStyle}";

    /// <summary>Segment label: the Enneagram triad ("Gut"/"Heart"/"Head").</summary>
    public static Func<PsychometricProfile, string> ByEnneagramTriad => p => p.Enneagram.Triad;

    /// <summary>Segment label: split the panel at the Openness midpoint.</summary>
    public static Func<PsychometricProfile, string> ByOpennessHalf =>
        p => p.Ocean.Openness >= 50 ? "High-Openness" : "Low-Openness";
}
