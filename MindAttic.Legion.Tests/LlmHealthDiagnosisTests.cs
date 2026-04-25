using System.Net;
using System.Text.Json;
using MindAttic.Legion;
using MindAttic.Legion.Tests.TestSupport;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Unit tests for <see cref="LlmHealthDiagnoser"/>. These pin down the mapping
/// from raw exception → user-facing failure category, so apps can render the
/// right next-step regardless of which provider misbehaved.
///
/// The categories tested here are the ones a user actually cares about when
/// "the LLM doesn't work":
///   • Offline                — provider is down / unreachable
///   • AuthInvalid             — API key wrong or expired (401)
///   • AuthForbidden           — key valid but model/account disabled (403)
///   • QuotaExhausted          — out of credits / paid tokens (402 or 429+billing body)
///   • RateLimited             — too many requests (429 transient)
///   • ServerError / 503       — provider's fault
///   • Timeout / GatewayTimeout — slow responses
///   • CircuitOpen             — Legion fast-failing
///   • MissingCredential       — no key configured
///   • BadResponse             — provider replied but the JSON was malformed
/// </summary>
[TestFixture]
public class LlmHealthDiagnosisExceptionClassifierTests
{
    [Test]
    public void NullException_IsUnknown()
    {
        var (d, c) = LlmHealthDiagnoser.ClassifyException(null!);
        Assert.That(d, Is.EqualTo(LlmHealthDiagnosis.Unknown));
        Assert.That(c, Is.Null);
    }

    [Test]
    public void HttpRequestException_NoStatus_IsOffline()
    {
        var (d, c) = LlmHealthDiagnoser.ClassifyException(new HttpRequestException("DNS blew up"));
        Assert.That(d, Is.EqualTo(LlmHealthDiagnosis.Offline));
        Assert.That(c, Is.Null);
    }

    [TestCase(HttpStatusCode.BadRequest,            LlmHealthDiagnosis.BadRequest)]
    [TestCase(HttpStatusCode.Unauthorized,          LlmHealthDiagnosis.AuthInvalid)]
    [TestCase(HttpStatusCode.PaymentRequired,       LlmHealthDiagnosis.QuotaExhausted)]
    [TestCase(HttpStatusCode.Forbidden,             LlmHealthDiagnosis.AuthForbidden)]
    [TestCase(HttpStatusCode.NotFound,              LlmHealthDiagnosis.NotFound)]
    [TestCase(HttpStatusCode.RequestTimeout,        LlmHealthDiagnosis.GatewayTimeout)]
    [TestCase(HttpStatusCode.RequestEntityTooLarge, LlmHealthDiagnosis.PayloadTooLarge)]
    [TestCase(HttpStatusCode.TooManyRequests,       LlmHealthDiagnosis.RateLimited)]
    [TestCase(HttpStatusCode.InternalServerError,   LlmHealthDiagnosis.ServerError)]
    [TestCase(HttpStatusCode.BadGateway,            LlmHealthDiagnosis.ServerError)]
    [TestCase(HttpStatusCode.ServiceUnavailable,    LlmHealthDiagnosis.ServiceUnavailable)]
    [TestCase(HttpStatusCode.GatewayTimeout,        LlmHealthDiagnosis.GatewayTimeout)]
    public void HttpStatusCode_MapsToExpectedDiagnosis(HttpStatusCode status, LlmHealthDiagnosis expected)
    {
        var ex = new HttpRequestException("err", inner: null, statusCode: status);
        var (d, c) = LlmHealthDiagnoser.ClassifyException(ex);
        Assert.That(d, Is.EqualTo(expected));
        Assert.That(c, Is.EqualTo((int)status));
    }

    [Test]
    public void TooManyRequests_WithBillingBody_IsQuotaExhausted_NotRateLimited()
    {
        // Real OpenAI body: 429 with insufficient_quota. We must classify it as
        // a quota problem so the user knows to top up — not as a "wait and retry".
        var ex = new HttpRequestException(
            "429 Too Many Requests: " + Bodies.OpenAiQuota,
            inner: null,
            statusCode: HttpStatusCode.TooManyRequests);
        var (d, _) = LlmHealthDiagnoser.ClassifyException(ex);
        Assert.That(d, Is.EqualTo(LlmHealthDiagnosis.QuotaExhausted));
    }

    [Test]
    public void TooManyRequests_PlainBody_IsRateLimited()
    {
        var ex = new HttpRequestException(
            "429 Too Many Requests: rate_limit_exceeded — please slow down",
            inner: null,
            statusCode: HttpStatusCode.TooManyRequests);
        var (d, _) = LlmHealthDiagnoser.ClassifyException(ex);
        Assert.That(d, Is.EqualTo(LlmHealthDiagnosis.RateLimited));
    }

    [Test]
    public void TaskCanceledException_WithoutUserCancel_IsTimeout()
    {
        var (d, _) = LlmHealthDiagnoser.ClassifyException(new TaskCanceledException("timed out"));
        Assert.That(d, Is.EqualTo(LlmHealthDiagnosis.Timeout));
    }

    [Test]
    public void OperationCanceledException_WithUserCancel_IsCancelledByUser()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var (d, _) = LlmHealthDiagnoser.ClassifyException(
            new OperationCanceledException(cts.Token), cts.Token);
        Assert.That(d, Is.EqualTo(LlmHealthDiagnosis.CancelledByUser));
    }

    [Test]
    public void TaskCanceledException_WithUserCancel_IsCancelledByUser()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var (d, _) = LlmHealthDiagnoser.ClassifyException(
            new TaskCanceledException("user-aborted"), cts.Token);
        Assert.That(d, Is.EqualTo(LlmHealthDiagnosis.CancelledByUser));
    }

    [Test]
    public void CircuitBreakerOpenException_IsCircuitOpen()
    {
        var ex = new CircuitBreakerOpenException("claude", TimeSpan.FromSeconds(30));
        var (d, _) = LlmHealthDiagnoser.ClassifyException(ex);
        Assert.That(d, Is.EqualTo(LlmHealthDiagnosis.CircuitOpen));
    }

    [Test]
    public void JsonException_IsBadResponse()
    {
        var (d, _) = LlmHealthDiagnoser.ClassifyException(new JsonException("garbled"));
        Assert.That(d, Is.EqualTo(LlmHealthDiagnosis.BadResponse));
    }

    [Test]
    public void KeyNotFoundException_IsBadResponse()
    {
        var (d, _) = LlmHealthDiagnoser.ClassifyException(new KeyNotFoundException("missing field"));
        Assert.That(d, Is.EqualTo(LlmHealthDiagnosis.BadResponse));
    }

    [Test]
    public void IndexOutOfRangeException_IsBadResponse()
    {
        // Happens when provider returns content:[] but our code expects [0]
        var (d, _) = LlmHealthDiagnoser.ClassifyException(new IndexOutOfRangeException());
        Assert.That(d, Is.EqualTo(LlmHealthDiagnosis.BadResponse));
    }

    [Test]
    public void InvalidOperationException_NoApiKey_IsMissingCredential()
    {
        var ex = new InvalidOperationException("No API key configured for provider 'claude'.");
        var (d, _) = LlmHealthDiagnoser.ClassifyException(ex);
        Assert.That(d, Is.EqualTo(LlmHealthDiagnosis.MissingCredential));
    }

    [Test]
    public void ArgumentException_IsBadRequest()
    {
        var (d, _) = LlmHealthDiagnoser.ClassifyException(new ArgumentException("bad payload"));
        Assert.That(d, Is.EqualTo(LlmHealthDiagnosis.BadRequest));
    }

    [Test]
    public void AggregateException_UnwrapsToFirstInner()
    {
        var inner = new HttpRequestException("auth", inner: null, statusCode: HttpStatusCode.Unauthorized);
        var agg   = new AggregateException("wrapped", inner);
        var (d, _) = LlmHealthDiagnoser.ClassifyException(agg);
        Assert.That(d, Is.EqualTo(LlmHealthDiagnosis.AuthInvalid));
    }

    [Test]
    public void Unrecognised4xx_IsBadRequest()
    {
        var ex = new HttpRequestException("418", inner: null, statusCode: HttpStatusCode.UnprocessableEntity);
        var (d, _) = LlmHealthDiagnoser.ClassifyException(ex);
        Assert.That(d, Is.EqualTo(LlmHealthDiagnosis.BadRequest));
    }

    [Test]
    public void Unrecognised5xx_IsServerError()
    {
        var ex = new HttpRequestException("507", inner: null, statusCode: HttpStatusCode.InsufficientStorage);
        var (d, _) = LlmHealthDiagnoser.ClassifyException(ex);
        Assert.That(d, Is.EqualTo(LlmHealthDiagnosis.ServerError));
    }
}

/// <summary>
/// Tests the <see cref="LlmHealthDiagnoser.ActionableMessage"/> renderer —
/// every diagnosis must yield a non-empty, actionable message that mentions
/// the provider name and (where appropriate) the dashboard / keys URL the user
/// needs to visit to fix the problem.
/// </summary>
[TestFixture]
public class LlmHealthDiagnoserActionableMessageTests
{
    private const string Display     = "Claude";
    private const string KeysUrl     = "https://console.anthropic.com/settings/keys";
    private const string DashUrl     = "https://console.anthropic.com/";

    [Test]
    public void EveryDiagnosis_HasNonEmptyMessage()
    {
        foreach (LlmHealthDiagnosis d in Enum.GetValues<LlmHealthDiagnosis>())
        {
            var msg = LlmHealthDiagnoser.ActionableMessage(d, Display, KeysUrl, DashUrl);
            Assert.That(msg, Is.Not.Null.And.Not.Empty, $"empty message for {d}");
        }
    }

    [Test]
    public void EveryDiagnosis_MentionsProviderName()
    {
        foreach (LlmHealthDiagnosis d in Enum.GetValues<LlmHealthDiagnosis>())
        {
            var msg = LlmHealthDiagnoser.ActionableMessage(d, Display, KeysUrl, DashUrl);
            Assert.That(msg, Does.Contain(Display), $"{d} message did not mention provider name");
        }
    }

    [TestCase(LlmHealthDiagnosis.MissingCredential, KeysUrl)]
    [TestCase(LlmHealthDiagnosis.AuthInvalid,       KeysUrl)]
    [TestCase(LlmHealthDiagnosis.QuotaExhausted,    DashUrl)]
    [TestCase(LlmHealthDiagnosis.AuthForbidden,     DashUrl)]
    [TestCase(LlmHealthDiagnosis.ServerError,       DashUrl)]
    public void ActionableDiagnosis_LinksToCorrectUrl(LlmHealthDiagnosis d, string expected)
    {
        var msg = LlmHealthDiagnoser.ActionableMessage(d, Display, KeysUrl, DashUrl);
        Assert.That(msg, Does.Contain(expected),
            $"{d} should link the user to {expected} so they can act on it");
    }

    [Test]
    public void QuotaExhausted_TellsUserToTopUp()
    {
        var msg = LlmHealthDiagnoser.ActionableMessage(
            LlmHealthDiagnosis.QuotaExhausted, Display, KeysUrl, DashUrl);
        Assert.That(msg, Does.Contain("Top up").IgnoreCase);
    }

    [Test]
    public void AuthInvalid_TellsUserToGenerateNewKey()
    {
        var msg = LlmHealthDiagnoser.ActionableMessage(
            LlmHealthDiagnosis.AuthInvalid, Display, KeysUrl, DashUrl);
        Assert.That(msg, Does.Contain("new").IgnoreCase);
        Assert.That(msg, Does.Contain("key").IgnoreCase);
    }

    [Test]
    public void Offline_TellsUserProviderUnreachable()
    {
        var msg = LlmHealthDiagnoser.ActionableMessage(
            LlmHealthDiagnosis.Offline, Display, KeysUrl, DashUrl);
        Assert.That(msg, Does.Contain("unreachable").IgnoreCase
                       .Or.Contain("offline").IgnoreCase);
    }

    [Test]
    public void CircuitOpen_TellsUserToUseDifferentProvider()
    {
        var msg = LlmHealthDiagnoser.ActionableMessage(
            LlmHealthDiagnosis.CircuitOpen, Display, KeysUrl, DashUrl);
        Assert.That(msg, Does.Contain("different provider").IgnoreCase
                       .Or.Contain("another provider").IgnoreCase);
    }

    [Test]
    public void ResponseMatchClassifier_HealthyWhenTrue()
    {
        Assert.That(LlmHealthDiagnoser.ClassifyResponseMatch(true),
            Is.EqualTo(LlmHealthDiagnosis.Healthy));
    }

    [Test]
    public void ResponseMatchClassifier_MismatchWhenFalse()
    {
        Assert.That(LlmHealthDiagnoser.ClassifyResponseMatch(false),
            Is.EqualTo(LlmHealthDiagnosis.ResponseMismatch));
    }
}
