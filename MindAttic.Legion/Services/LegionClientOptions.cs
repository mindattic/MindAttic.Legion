namespace MindAttic.Legion;

/// <summary>
/// Tunes LegionClient's resilience behaviour. Pass to the LegionClient constructor
/// to override defaults; pass <c>null</c> for the default policy.
///
/// Defaults:
///   - Retry up to <see cref="MaxRetries"/>=2 times on transient errors (5xx / 429 /
///     network) with exponential backoff starting at <see cref="InitialBackoff"/>=500ms
///     and doubling each attempt.
///   - Open the circuit breaker after <see cref="CircuitBreakerThreshold"/>=5
///     consecutive failures for a single provider. While open, calls to that
///     provider fail fast for <see cref="CircuitBreakerCooldown"/>=2 minutes.
/// </summary>
public sealed class LegionClientOptions
{
    /// <summary>How many extra attempts to make after the first failure.</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Initial backoff delay before the first retry.</summary>
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Multiplier applied to the backoff after each retry.</summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    /// Consecutive failures that trip the circuit breaker for a provider. While
    /// the breaker is open, further calls to that provider throw immediately
    /// without attempting the remote call.
    /// </summary>
    public int CircuitBreakerThreshold { get; init; } = 5;

    /// <summary>How long the breaker stays open before allowing a probe call.</summary>
    public TimeSpan CircuitBreakerCooldown { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>The default policy.</summary>
    public static LegionClientOptions Default { get; } = new();

    /// <summary>A no-resilience policy — no retries, no circuit breaker.</summary>
    public static LegionClientOptions NoResilience { get; } = new()
    {
        MaxRetries = 0,
        CircuitBreakerThreshold = int.MaxValue,
    };
}
