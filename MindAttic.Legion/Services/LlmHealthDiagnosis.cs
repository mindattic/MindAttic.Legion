using System.Text.Json;

namespace MindAttic.Legion;

/// <summary>
/// Structured failure category produced by <see cref="LlmHealthDiagnoser"/>.
/// Lets apps branch on the actual reason a provider is unhealthy — e.g.
/// "is the LLM offline?" vs "is the API key expired?" vs "is the account out
/// of tokens?" — instead of trying to parse free-text error messages.
/// </summary>
public enum LlmHealthDiagnosis
{
    /// <summary>Diagnosis could not be determined from the available signals.</summary>
    Unknown = 0,

    /// <summary>Provider reachable, key valid, response correct.</summary>
    Healthy,

    /// <summary>Reachable, key valid, but reply did not match the probe expectation.</summary>
    ResponseMismatch,

    /// <summary>Reachable, but the response body could not be parsed (malformed JSON / missing fields).</summary>
    BadResponse,

    /// <summary>No API key configured in the credential store.</summary>
    MissingCredential,

    /// <summary>HTTP 401 — the API key is invalid or has been revoked / expired.</summary>
    AuthInvalid,

    /// <summary>HTTP 403 — key is recognised but the account/key is disabled or lacks permission for this model.</summary>
    AuthForbidden,

    /// <summary>HTTP 402 or 429-with-quota-signal — account is out of credits / paid tokens.</summary>
    QuotaExhausted,

    /// <summary>HTTP 429 without quota markers — too many requests, transient.</summary>
    RateLimited,

    /// <summary>HTTP 400 — payload rejected (bad model name, wrong field, exceeding limits).</summary>
    BadRequest,

    /// <summary>HTTP 404 — endpoint or model not found.</summary>
    NotFound,

    /// <summary>HTTP 413 — request payload too large.</summary>
    PayloadTooLarge,

    /// <summary>HTTP 5xx generic — provider had an internal error.</summary>
    ServerError,

    /// <summary>HTTP 503 — provider is down or in maintenance.</summary>
    ServiceUnavailable,

    /// <summary>HTTP 504 / 408 — gateway / origin timed out.</summary>
    GatewayTimeout,

    /// <summary>Local request timeout (per-provider deadline elapsed) — not from the user's cancellation token.</summary>
    Timeout,

    /// <summary>Network error with no HTTP status — DNS failure, connection refused, host unreachable.</summary>
    Offline,

    /// <summary>The per-provider circuit breaker is open — many recent failures, fail-fast in effect.</summary>
    CircuitOpen,

    /// <summary>The user's <see cref="CancellationToken"/> was triggered.</summary>
    CancelledByUser,
}

/// <summary>
/// Translates raw exceptions and HTTP responses from <see cref="LegionClient"/>
/// into a <see cref="LlmHealthDiagnosis"/> + a human-readable, actionable
/// message. The classification is deliberately structured so apps can render
/// targeted recovery prompts ("top up your account", "rotate your key", "check
/// the provider status page") instead of dumping a stack trace.
/// </summary>
public static class LlmHealthDiagnoser
{
    /// <summary>
    /// Classify an exception thrown by <see cref="LegionClient"/>. <paramref name="userToken"/>
    /// is the cancellation token the caller passed in — used to distinguish
    /// "user cancelled" from "request timed out internally".
    /// </summary>
    public static (LlmHealthDiagnosis Diagnosis, int? HttpStatusCode) ClassifyException(
        Exception ex,
        CancellationToken userToken = default)
    {
        if (ex is null) return (LlmHealthDiagnosis.Unknown, null);

        // Unwrap aggregates (the fallback chain throws AggregateException with N inner errors)
        if (ex is AggregateException agg && agg.InnerExceptions.Count > 0)
            return ClassifyException(agg.InnerExceptions[0], userToken);

        switch (ex)
        {
            case CircuitBreakerOpenException:
                return (LlmHealthDiagnosis.CircuitOpen, null);

            case OperationCanceledException when userToken.IsCancellationRequested:
                return (LlmHealthDiagnosis.CancelledByUser, null);

            case TaskCanceledException:
                return (LlmHealthDiagnosis.Timeout, 408);

            case OperationCanceledException:
                return (LlmHealthDiagnosis.Timeout, null);

            case JsonException:
            case KeyNotFoundException:
            case IndexOutOfRangeException:
                return (LlmHealthDiagnosis.BadResponse, 200);

            case InvalidOperationException ioe when LooksLikeMissingCredential(ioe.Message):
                return (LlmHealthDiagnosis.MissingCredential, null);

            case HttpRequestException hre:
                return ClassifyHttp(hre);

            case ArgumentException:
                return (LlmHealthDiagnosis.BadRequest, 400);

            case InvalidOperationException:
                // A non-credential InvalidOperationException is almost always a
                // malformed-response access (e.g. GetString on the wrong JSON
                // kind), not a missing key — don't steer the user to rotate a
                // valid key. (The credential-looking case is handled above.)
                return (LlmHealthDiagnosis.BadResponse, null);

            default:
                return (LlmHealthDiagnosis.Unknown, null);
        }
    }

    /// <summary>
    /// Convenience overload: provided a successful HTTP path that produced a
    /// reply but the reply did not match the probe's expected token, you can
    /// classify that as <see cref="LlmHealthDiagnosis.ResponseMismatch"/>.
    /// </summary>
    public static LlmHealthDiagnosis ClassifyResponseMatch(bool respondedCorrectly) =>
        respondedCorrectly ? LlmHealthDiagnosis.Healthy : LlmHealthDiagnosis.ResponseMismatch;

    /// <summary>
    /// Render a user-facing, actionable next-step message for a diagnosis.
    /// Embeds the dashboard / keys URLs from the provider catalog so the user
    /// can act immediately.
    /// </summary>
    public static string ActionableMessage(
        LlmHealthDiagnosis diagnosis,
        string displayName,
        string keysUrl,
        string dashboardUrl) => diagnosis switch
    {
        LlmHealthDiagnosis.Healthy =>
            $"{displayName} is online and responding correctly.",
        LlmHealthDiagnosis.ResponseMismatch =>
            $"{displayName} is reachable but the reply did not match the expected probe answer. Verify the configured model is suitable for instruction-following.",
        LlmHealthDiagnosis.BadResponse =>
            $"{displayName} returned a reply that could not be parsed. The model or provider may have changed its response shape.",
        LlmHealthDiagnosis.MissingCredential =>
            $"No API key configured for {displayName}. Generate one at {keysUrl} and add it to the MindAttic credential store.",
        LlmHealthDiagnosis.AuthInvalid =>
            $"{displayName} rejected the API key (HTTP 401). The key is invalid, has been revoked, or expired — generate a new one at {keysUrl}.",
        LlmHealthDiagnosis.AuthForbidden =>
            $"{displayName} returned HTTP 403 — the key is recognised but the account or model is disabled. Check {dashboardUrl}.",
        LlmHealthDiagnosis.QuotaExhausted =>
            $"{displayName} reports the account is out of credits / tokens. Top up your balance at {dashboardUrl}.",
        LlmHealthDiagnosis.RateLimited =>
            $"{displayName} is rate-limiting requests (HTTP 429). Slow down or wait a moment, then retry. If this persists, check usage at {dashboardUrl}.",
        LlmHealthDiagnosis.BadRequest =>
            $"{displayName} returned HTTP 400 Bad Request — verify the configured model name and request payload.",
        LlmHealthDiagnosis.NotFound =>
            $"{displayName} returned HTTP 404 — the endpoint or model was not found. The model may have been deprecated.",
        LlmHealthDiagnosis.PayloadTooLarge =>
            $"{displayName} rejected the request as too large (HTTP 413). Reduce prompt or context size.",
        LlmHealthDiagnosis.ServerError =>
            $"{displayName} is having server-side problems (HTTP 5xx). Try a different provider; check status at {dashboardUrl}.",
        LlmHealthDiagnosis.ServiceUnavailable =>
            $"{displayName} is currently unavailable (HTTP 503). The provider is offline or in maintenance — fall over to another provider.",
        LlmHealthDiagnosis.GatewayTimeout =>
            $"{displayName} did not respond in time (HTTP 504/408). The provider is slow or unreachable.",
        LlmHealthDiagnosis.Timeout =>
            $"{displayName} did not respond before the local timeout. Network is slow or the provider hung.",
        LlmHealthDiagnosis.Offline =>
            $"{displayName} is unreachable — the provider appears offline or the local network is down.",
        LlmHealthDiagnosis.CircuitOpen =>
            $"{displayName} is failing repeatedly — circuit breaker is open. Use a different provider until it cools down.",
        LlmHealthDiagnosis.CancelledByUser =>
            $"{displayName} probe was cancelled.",
        _ => $"{displayName} status is unknown."
    };

    // ── internals ───────────────────────────────────────────────────────────────

    private static (LlmHealthDiagnosis, int?) ClassifyHttp(HttpRequestException hre)
    {
        if (hre.StatusCode is null)
            return (LlmHealthDiagnosis.Offline, null);

        var code = (int)hre.StatusCode;
        var msg  = hre.Message ?? "";

        // OpenAI returns 429 for both rate-limits AND quota exhaustion. Disambiguate
        // via response-body markers when the body has been preserved on the exception.
        if (code == 429 && LooksLikeQuotaSignal(msg))
            return (LlmHealthDiagnosis.QuotaExhausted, code);

        return code switch
        {
            400 => (LlmHealthDiagnosis.BadRequest, code),
            401 => (LlmHealthDiagnosis.AuthInvalid, code),
            402 => (LlmHealthDiagnosis.QuotaExhausted, code),
            403 => (LlmHealthDiagnosis.AuthForbidden, code),
            404 => (LlmHealthDiagnosis.NotFound, code),
            408 => (LlmHealthDiagnosis.GatewayTimeout, code),
            413 => (LlmHealthDiagnosis.PayloadTooLarge, code),
            429 => (LlmHealthDiagnosis.RateLimited, code),
            500 => (LlmHealthDiagnosis.ServerError, code),
            502 => (LlmHealthDiagnosis.ServerError, code),
            503 => (LlmHealthDiagnosis.ServiceUnavailable, code),
            504 => (LlmHealthDiagnosis.GatewayTimeout, code),
            _ when code >= 500 => (LlmHealthDiagnosis.ServerError, code),
            _ when code >= 400 => (LlmHealthDiagnosis.BadRequest, code),
            _ => (LlmHealthDiagnosis.Unknown, code),
        };
    }

    private static bool LooksLikeQuotaSignal(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        var lower = s.ToLowerInvariant();
        return lower.Contains("insufficient_quota")
            || lower.Contains("quota")
            || lower.Contains("billing")
            || lower.Contains("credit balance")
            || lower.Contains("out of credit")
            || lower.Contains("exceeded your current quota");
    }

    private static bool LooksLikeMissingCredential(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        var lower = s.ToLowerInvariant();
        return lower.Contains("no api key") || lower.Contains("api key configured");
    }
}
