using System.Net;
using MindAttic.Legion;
using MindAttic.Legion.Tests.TestSupport;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// End-to-end diagnostic coverage for <see cref="LlmHealthCheck"/>. For every
/// failure mode an LLM provider can exhibit in the wild, we simulate the wire
/// response (or the network condition) and assert that the result carries:
///   • the right <see cref="LlmHealthDiagnosis"/>
///   • the right <see cref="LlmHealthResult.HttpStatusCode"/>
///   • a populated, actionable message
///
/// These are the user-facing tests — they should fail loudly the moment a
/// classifier change makes "401 expired key" indistinguishable from
/// "503 provider down" or "402 out of credits".
/// </summary>
[TestFixture]
public class LlmHealthCheckDiagnosticsTests
{
    private TempCredentialScope creds = null!;

    [SetUp]
    public void SetUp()
    {
        CircuitBreaker.ResetAll();
        creds = new TempCredentialScope();
    }

    [TearDown]
    public void TearDown()
    {
        creds.Dispose();
        CircuitBreaker.ResetAll();
    }

    private LlmHealthCheck Build(HttpMessageHandler handler) =>
        new(new LegionClient(new HttpClient(handler), TestOptions.Instant()));

    // ── happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Probe_HealthyProvider_IsHealthy()
    {
        creds.WriteKey("claude-api", "sk-ant-good");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.OK, Bodies.ClaudeOk));

        var r = await hc.CheckOneAsync("claude-api");

        Assert.That(r.IsHealthy, Is.True);
        Assert.That(r.RespondedCorrectly, Is.True);
        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.Healthy));
        Assert.That(r.HttpStatusCode, Is.EqualTo(200));
        Assert.That(r.ActionableMessage, Does.Contain("online").IgnoreCase);
    }

    [Test]
    public async Task Probe_HealthyButWrongReply_IsResponseMismatch()
    {
        creds.WriteKey("claude-api", "sk-ant-good");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.OK, Bodies.ClaudeWrong));

        var r = await hc.CheckOneAsync("claude-api");

        Assert.That(r.IsHealthy, Is.True, "the wire was healthy — the response just didn't match");
        Assert.That(r.RespondedCorrectly, Is.False);
        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.ResponseMismatch));
        Assert.That(r.Status, Does.StartWith("WRONG REPLY"));
    }

    // ── credential failures ─────────────────────────────────────────────────────

    [Test]
    public async Task Probe_NoKey_IsMissingCredential()
    {
        var hc = Build(new FixedResponseHandler(HttpStatusCode.OK, Bodies.ClaudeOk));
        var r  = await hc.CheckOneAsync("claude-api");

        Assert.That(r.HasCredential, Is.False);
        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.MissingCredential));
        Assert.That(r.ActionableMessage, Does.Contain("Generate one").IgnoreCase);
        Assert.That(r.ActionableMessage, Does.Contain(r.KeysUrl));
    }

    [Test]
    public async Task Probe_401Unauthorized_IsAuthInvalid()
    {
        creds.WriteKey("claude-api", "sk-ant-expired");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.Unauthorized, Bodies.AuthInvalidBody));

        var r = await hc.CheckOneAsync("claude-api");

        Assert.That(r.IsHealthy, Is.False);
        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.AuthInvalid));
        Assert.That(r.HttpStatusCode, Is.EqualTo(401));
        Assert.That(r.ActionableMessage, Does.Contain("new").IgnoreCase);
        Assert.That(r.ActionableMessage, Does.Contain(r.KeysUrl));
    }

    [Test]
    public async Task Probe_403Forbidden_IsAuthForbidden()
    {
        creds.WriteKey("claude-api", "sk-ant-disabled");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.Forbidden, "{}"));

        var r = await hc.CheckOneAsync("claude-api");

        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.AuthForbidden));
        Assert.That(r.HttpStatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task Probe_402PaymentRequired_IsQuotaExhausted()
    {
        creds.WriteKey("claude-api", "sk-ant-broke");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.PaymentRequired, Bodies.ClaudeCreditLow));

        var r = await hc.CheckOneAsync("claude-api");

        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.QuotaExhausted));
        Assert.That(r.ActionableMessage, Does.Contain("Top up").IgnoreCase);
    }

    [Test]
    public async Task Probe_429WithQuotaSignalInBody_IsQuotaExhausted()
    {
        // Real-world OpenAI behavior: returns 429 with insufficient_quota body.
        // Critical that we surface this as "out of credits" not "rate limited",
        // because the user actions are different.
        creds.WriteKey("openai", "sk-broke");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.TooManyRequests, Bodies.OpenAiQuota));

        var r = await hc.CheckOneAsync("openai");

        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.QuotaExhausted),
            "429 with insufficient_quota body must be classified as QuotaExhausted, not RateLimited");
        Assert.That(r.HttpStatusCode, Is.EqualTo(429));
    }

    [Test]
    public async Task Probe_429TransientRateLimit_IsRateLimited()
    {
        creds.WriteKey("openai", "sk-good");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.TooManyRequests,
            """{"error":{"message":"rate_limit_exceeded; please retry","code":"rate_limit_exceeded"}}"""));

        var r = await hc.CheckOneAsync("openai");

        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.RateLimited),
            "plain 429 (no quota markers) must be classified as RateLimited");
    }

    // ── server-side failures ────────────────────────────────────────────────────

    [Test]
    public async Task Probe_500InternalError_IsServerError()
    {
        creds.WriteKey("claude-api", "sk-good");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.InternalServerError, Bodies.ServerErrorBody));

        var r = await hc.CheckOneAsync("claude-api");

        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.ServerError));
        Assert.That(r.HttpStatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task Probe_502BadGateway_IsServerError()
    {
        creds.WriteKey("claude-api", "sk-good");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.BadGateway, ""));

        var r = await hc.CheckOneAsync("claude-api");

        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.ServerError));
    }

    [Test]
    public async Task Probe_503ServiceUnavailable_IsServiceUnavailable()
    {
        creds.WriteKey("claude-api", "sk-good");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.ServiceUnavailable, ""));

        var r = await hc.CheckOneAsync("claude-api");

        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.ServiceUnavailable));
        Assert.That(r.ActionableMessage, Does.Contain("offline").IgnoreCase
                                          .Or.Contain("unavailable").IgnoreCase);
    }

    [Test]
    public async Task Probe_504GatewayTimeout_IsGatewayTimeout()
    {
        creds.WriteKey("claude-api", "sk-good");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.GatewayTimeout, ""));

        var r = await hc.CheckOneAsync("claude-api");

        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.GatewayTimeout));
    }

    // ── client-side wire failures ───────────────────────────────────────────────

    [Test]
    public async Task Probe_NetworkDown_IsOffline()
    {
        creds.WriteKey("claude-api", "sk-good");
        var hc = Build(new NetworkFailureHandler("No such host is known."));

        var r = await hc.CheckOneAsync("claude-api");

        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.Offline));
        Assert.That(r.HttpStatusCode, Is.Null);
        Assert.That(r.ActionableMessage, Does.Contain("unreachable").IgnoreCase);
    }

    [Test]
    public async Task Probe_LocalTimeout_IsTimeout()
    {
        creds.WriteKey("claude-api", "sk-good");
        var hc = Build(new HangingHandler());

        var r = await hc.CheckOneAsync("claude-api", timeout: TimeSpan.FromMilliseconds(50));

        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.Timeout));
    }

    [Test]
    public async Task Probe_400BadRequest_IsBadRequest()
    {
        creds.WriteKey("claude-api", "sk-good");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.BadRequest,
            """{"error":{"type":"invalid_request_error","message":"Invalid model specified"}}"""));

        var r = await hc.CheckOneAsync("claude-api");

        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.BadRequest));
    }

    [Test]
    public async Task Probe_404NotFound_IsNotFound()
    {
        creds.WriteKey("claude-api", "sk-good");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.NotFound, "{}"));

        var r = await hc.CheckOneAsync("claude-api");

        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.NotFound));
    }

    [Test]
    public async Task Probe_413PayloadTooLarge_IsPayloadTooLarge()
    {
        creds.WriteKey("claude-api", "sk-good");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.RequestEntityTooLarge, ""));

        var r = await hc.CheckOneAsync("claude-api");

        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.PayloadTooLarge));
    }

    // ── response-shape failures ────────────────────────────────────────────────

    [Test]
    public async Task Probe_MalformedJson_IsBadResponse()
    {
        creds.WriteKey("claude-api", "sk-good");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.OK, Bodies.MalformedJson));

        var r = await hc.CheckOneAsync("claude-api");

        Assert.That(r.IsHealthy, Is.False);
        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.BadResponse));
    }

    [Test]
    public async Task Probe_EmptyJsonObject_IsResponseMismatch()
    {
        // 200 OK with `{}` — valid JSON but no extractable text. ExtractClaudeText returns ""
        // (no throw), so the response is treated as a non-matching reply, not a parse error.
        creds.WriteKey("claude-api", "sk-good");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.OK, Bodies.EmptyJsonObject));

        var r = await hc.CheckOneAsync("claude-api");

        Assert.That(r.IsHealthy, Is.True, "wire was healthy (200 OK)");
        Assert.That(r.RespondedCorrectly, Is.False);
        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.ResponseMismatch));
    }

    [Test]
    public async Task Probe_EmptyContentArray_IsResponseMismatch()
    {
        // Provider returns `{"content":[]}` — ExtractClaudeText finds no text blocks
        // and returns "", which the health check treats as a non-matching reply.
        creds.WriteKey("claude-api", "sk-good");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.OK,
            """{"content":[]}"""));

        var r = await hc.CheckOneAsync("claude-api");

        Assert.That(r.IsHealthy, Is.True, "wire was healthy (200 OK)");
        Assert.That(r.RespondedCorrectly, Is.False);
        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.ResponseMismatch));
    }

    // ── circuit breaker integration ─────────────────────────────────────────────

    [Test]
    public async Task Probe_BreakerOpen_IsCircuitOpen()
    {
        creds.WriteKey("claude-api", "sk-good");
        // Pre-trip the breaker
        CircuitBreaker.RecordFailure("claude-api", threshold: 1, cooldown: TimeSpan.FromMinutes(5));

        var hc = Build(new FixedResponseHandler(HttpStatusCode.OK, Bodies.ClaudeOk));
        var r  = await hc.CheckOneAsync("claude-api");

        Assert.That(r.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.CircuitOpen),
            "when the breaker is open, the probe must surface CircuitOpen so the user can try a different provider");
        Assert.That(r.IsHealthy, Is.False);
        Assert.That(r.ActionableMessage, Does.Contain("provider").IgnoreCase);
    }

    // ── per-provider credential routing ─────────────────────────────────────────

    [Test]
    public async Task CheckAllAsync_ProducesOneResultPerCatalogProvider()
    {
        var hc  = Build(new FixedResponseHandler(HttpStatusCode.OK, Bodies.ClaudeOk));
        var all = await hc.CheckAllAsync();
        Assert.That(all, Has.Count.EqualTo(LlmProviderCatalog.All.Count));
    }

    [Test]
    public async Task CheckAllAsync_AllProvidersWithoutKeys_AllAreMissingCredential()
    {
        var hc = Build(new FixedResponseHandler(HttpStatusCode.OK, Bodies.ClaudeOk));
        var all = await hc.CheckAllAsync();
        // claude-team uses OAuth (not a file-based key), so it may have a live token on dev machines.
        var keyBased = all.Where(r => !string.Equals(r.ProviderId, "claude-team", StringComparison.OrdinalIgnoreCase));
        Assert.That(keyBased.All(r => r.Diagnosis == LlmHealthDiagnosis.MissingCredential), Is.True);
    }

    [Test]
    public async Task CheckAllAsync_MixedFailureModes_EachClassifiedIndependently()
    {
        // Drop keys for three providers and route each to a different failure mode
        // via ProviderAwareHandler — the canonical "give me a status board" use case.
        creds.WriteKey("claude-api", "sk-good");
        creds.WriteKey("openai", "sk-broke");
        creds.WriteKey("gemini", "sk-server-issue");

        var routing = new ProviderAwareHandler();
        routing.SetForUri("api.anthropic.com",      HttpStatusCode.OK,                 Bodies.ClaudeOk);
        routing.SetForUri("api.openai.com",         HttpStatusCode.TooManyRequests,    Bodies.OpenAiQuota);
        routing.SetForUri("generativelanguage.googleapis.com", HttpStatusCode.ServiceUnavailable, "");

        var hc      = Build(routing);
        var results = (await hc.CheckAsync(new[] { "claude-api", "openai", "gemini" }))
                      .ToDictionary(r => r.ProviderId);

        Assert.That(results["claude-api"].Diagnosis, Is.EqualTo(LlmHealthDiagnosis.Healthy));
        Assert.That(results["openai"].Diagnosis, Is.EqualTo(LlmHealthDiagnosis.QuotaExhausted));
        Assert.That(results["gemini"].Diagnosis, Is.EqualTo(LlmHealthDiagnosis.ServiceUnavailable));
    }

    [Test]
    public async Task CheckOneAsync_PreservesElapsedTime()
    {
        creds.WriteKey("claude-api", "sk-good");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.OK, Bodies.ClaudeOk));
        var r  = await hc.CheckOneAsync("claude-api");
        Assert.That(r.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task CheckOneAsync_UnknownProvider_DoesNotCrash()
    {
        creds.WriteKey("madeup", "k");
        var hc = Build(new FixedResponseHandler(HttpStatusCode.OK, "{}"));
        var r  = await hc.CheckOneAsync("madeup");

        Assert.That(r.IsHealthy, Is.False);
        // unknown provider has no DefaultModel — call to LegionClient should fail with ArgumentException
        Assert.That(r.Diagnosis,
            Is.EqualTo(LlmHealthDiagnosis.BadRequest)
              .Or.EqualTo(LlmHealthDiagnosis.Unknown));
    }
}
