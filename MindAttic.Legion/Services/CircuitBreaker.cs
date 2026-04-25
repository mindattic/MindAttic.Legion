using System.Collections.Concurrent;

namespace MindAttic.Legion;

/// <summary>
/// Per-provider failure tracker. After N consecutive failures, the breaker opens
/// for a cooldown window; calls for that provider during the open window throw
/// <see cref="CircuitBreakerOpenException"/> immediately so the caller can fail
/// over to a different provider without burning more wall-clock on a sick endpoint.
///
/// State is process-static and shared across all LegionClient instances —
/// "Claude is down" should mean the same thing to every consumer.
/// </summary>
public static class CircuitBreaker
{
    private sealed class State
    {
        public int ConsecutiveFailures;
        public DateTimeOffset OpenUntil = DateTimeOffset.MinValue;
    }

    private static readonly ConcurrentDictionary<string, State> states =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Throws <see cref="CircuitBreakerOpenException"/> if the breaker for this
    /// provider is currently open. Callers should invoke this immediately before
    /// the remote call.
    /// </summary>
    public static void ThrowIfOpen(string providerId)
    {
        if (!states.TryGetValue(providerId, out var s)) return;
        if (s.OpenUntil > DateTimeOffset.UtcNow)
            throw new CircuitBreakerOpenException(providerId, s.OpenUntil - DateTimeOffset.UtcNow);
    }

    /// <summary>True if the breaker for this provider is currently open.</summary>
    public static bool IsOpen(string providerId) =>
        states.TryGetValue(providerId, out var s) && s.OpenUntil > DateTimeOffset.UtcNow;

    /// <summary>Reset failure count after a successful call.</summary>
    public static void RecordSuccess(string providerId)
    {
        if (states.TryGetValue(providerId, out var s))
        {
            s.ConsecutiveFailures = 0;
            s.OpenUntil = DateTimeOffset.MinValue;
        }
    }

    /// <summary>
    /// Increment failure count; open the breaker when the threshold is hit.
    /// Returns the new failure count.
    /// </summary>
    public static int RecordFailure(string providerId, int threshold, TimeSpan cooldown)
    {
        var s = states.GetOrAdd(providerId, _ => new State());
        var failures = Interlocked.Increment(ref s.ConsecutiveFailures);
        if (failures >= threshold)
            s.OpenUntil = DateTimeOffset.UtcNow.Add(cooldown);
        return failures;
    }

    /// <summary>Test/diagnostic — wipe all breaker state.</summary>
    public static void ResetAll() => states.Clear();

    /// <summary>Test/diagnostic — wipe state for one provider.</summary>
    public static void Reset(string providerId)
    {
        if (states.TryGetValue(providerId, out var s))
        {
            s.ConsecutiveFailures = 0;
            s.OpenUntil = DateTimeOffset.MinValue;
        }
    }
}

/// <summary>
/// Thrown when a provider's circuit breaker is open. The caller should pick a
/// different provider in their fallback chain instead of waiting for this one
/// to recover.
/// </summary>
public sealed class CircuitBreakerOpenException : Exception
{
    public string ProviderId { get; }
    public TimeSpan TimeUntilProbe { get; }

    public CircuitBreakerOpenException(string providerId, TimeSpan timeUntilProbe)
        : base($"Circuit breaker open for provider '{providerId}'. Retry in ~{timeUntilProbe.TotalSeconds:F0}s or fall over to another provider.")
    {
        ProviderId = providerId;
        TimeUntilProbe = timeUntilProbe;
    }
}
