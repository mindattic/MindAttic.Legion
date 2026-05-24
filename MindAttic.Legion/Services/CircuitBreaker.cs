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
        // OpenUntilTicks holds DateTimeOffset.UtcNow.UtcTicks for the next
        // probe-eligible instant. Stored as a long so reads/writes can use
        // Interlocked and stay coherent under concurrent fan-out.
        public int ConsecutiveFailures;
        public long OpenUntilTicks;
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
        var openUntil = new DateTimeOffset(Interlocked.Read(ref s.OpenUntilTicks), TimeSpan.Zero);
        if (openUntil > DateTimeOffset.UtcNow)
            throw new CircuitBreakerOpenException(providerId, openUntil - DateTimeOffset.UtcNow);
    }

    /// <summary>True if the breaker for this provider is currently open.</summary>
    public static bool IsOpen(string providerId)
    {
        if (!states.TryGetValue(providerId, out var s)) return false;
        var openUntil = new DateTimeOffset(Interlocked.Read(ref s.OpenUntilTicks), TimeSpan.Zero);
        return openUntil > DateTimeOffset.UtcNow;
    }

    /// <summary>Reset failure count after a successful call.</summary>
    public static void RecordSuccess(string providerId)
    {
        if (states.TryGetValue(providerId, out var s))
        {
            Interlocked.Exchange(ref s.ConsecutiveFailures, 0);
            Interlocked.Exchange(ref s.OpenUntilTicks, 0L);
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
            Interlocked.Exchange(ref s.OpenUntilTicks, DateTimeOffset.UtcNow.Add(cooldown).UtcTicks);
        return failures;
    }

    /// <summary>Test/diagnostic — wipe all breaker state.</summary>
    public static void ResetAll() => states.Clear();

    /// <summary>Test/diagnostic — wipe state for one provider.</summary>
    public static void Reset(string providerId)
    {
        if (states.TryGetValue(providerId, out var s))
        {
            Interlocked.Exchange(ref s.ConsecutiveFailures, 0);
            Interlocked.Exchange(ref s.OpenUntilTicks, 0L);
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
    /// <summary>The provider whose breaker is currently open.</summary>
    public string ProviderId { get; }

    /// <summary>
    /// Approximate time remaining before the breaker will allow a probe call again.
    /// Use this to back off intelligently or to decide which fallback to try first.
    /// </summary>
    public TimeSpan TimeUntilProbe { get; }

    /// <summary>
    /// Builds a <see cref="CircuitBreakerOpenException"/> with a message that
    /// names the provider and includes the remaining cooldown in seconds.
    /// </summary>
    public CircuitBreakerOpenException(string providerId, TimeSpan timeUntilProbe)
        : base($"Circuit breaker open for provider '{providerId}'. Retry in ~{timeUntilProbe.TotalSeconds:F0}s or fall over to another provider.")
    {
        ProviderId = providerId;
        TimeUntilProbe = timeUntilProbe;
    }
}
