using System.Net;
using System.Net.Http;
using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

[TestFixture]
public class LlmHealthCheckTests
{
    private string tempDir = "";
    private string? prevEnv;

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "legion-health-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        prevEnv = Environment.GetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS");
        Environment.SetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS", tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS", prevEnv);
        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }

    [Test]
    public async Task CheckOneAsync_NoKey_ReturnsMissingCredential()
    {
        var hc = new LlmHealthCheck(new LegionClient(new HttpClient(new CapturingHandler("{}"))));
        var r  = await hc.CheckOneAsync("claude");

        Assert.That(r.HasCredential, Is.False);
        Assert.That(r.IsHealthy, Is.False);
        Assert.That(r.Status, Is.EqualTo("MISSING KEY"));
        Assert.That(r.DashboardUrl, Does.StartWith("https://"));
        Assert.That(r.KeysUrl, Does.StartWith("https://"));
    }

    [Test]
    public async Task CheckOneAsync_KeyPresent_CorrectReply_IsOk()
    {
        File.WriteAllText(Path.Combine(tempDir, "claude.key"), "sk-ant-test");
        var stub = """{"content":[{"text":"Hello World!"}]}""";
        var hc   = new LlmHealthCheck(new LegionClient(new HttpClient(new CapturingHandler(stub))));
        var r    = await hc.CheckOneAsync("claude");

        Assert.That(r.HasCredential, Is.True);
        Assert.That(r.IsHealthy, Is.True);
        Assert.That(r.RespondedCorrectly, Is.True);
        Assert.That(r.Status, Is.EqualTo("OK"));
        Assert.That(r.Response, Does.Contain("Hello World"));
    }

    [Test]
    public async Task CheckOneAsync_KeyPresent_WrongReply_IsFlaggedWrong()
    {
        File.WriteAllText(Path.Combine(tempDir, "openai.key"), "sk-test");
        var stub = """{"choices":[{"message":{"content":"goodbye"}}]}""";
        var hc   = new LlmHealthCheck(new LegionClient(new HttpClient(new CapturingHandler(stub))));
        var r    = await hc.CheckOneAsync("openai");

        Assert.That(r.IsHealthy, Is.True);
        Assert.That(r.RespondedCorrectly, Is.False);
        Assert.That(r.Status, Does.StartWith("WRONG REPLY"));
    }

    [Test]
    public async Task CheckOneAsync_HttpError_MarksUnhealthy()
    {
        File.WriteAllText(Path.Combine(tempDir, "claude.key"), "k");
        var hc = new LlmHealthCheck(new LegionClient(new HttpClient(new ErrorHandler(HttpStatusCode.Unauthorized))));
        var r  = await hc.CheckOneAsync("claude");

        Assert.That(r.HasCredential, Is.True);
        Assert.That(r.IsHealthy, Is.False);
        Assert.That(r.Status, Does.StartWith("ERROR"));
    }

    [Test]
    public async Task CheckAllAsync_RunsEveryCatalogProvider()
    {
        var hc = new LlmHealthCheck(new LegionClient(new HttpClient(new CapturingHandler("{}"))));
        var all = await hc.CheckAllAsync();
        Assert.That(all, Has.Count.EqualTo(LlmProviderCatalog.All.Count));
        Assert.That(all.All(r => !r.HasCredential), Is.True, "no keys configured, so all should be MISSING KEY");
    }

    [Test]
    public async Task CheckAsync_OnSubset_OnlyRunsThose()
    {
        var hc = new LlmHealthCheck(new LegionClient(new HttpClient(new CapturingHandler("{}"))));
        var ids = new[] { "claude", "openai" };
        var subset = await hc.CheckAsync(ids);
        Assert.That(subset, Has.Count.EqualTo(2));
        Assert.That(subset.Select(r => r.ProviderId), Is.EquivalentTo(ids));
    }

    [Test]
    public async Task CheckAsync_DedupesAndNormalizesIds()
    {
        var hc = new LlmHealthCheck(new LegionClient(new HttpClient(new CapturingHandler("{}"))));
        var subset = await hc.CheckAsync(new[] { "Claude", "claude", "  CLAUDE  " });
        Assert.That(subset, Has.Count.EqualTo(1));
        Assert.That(subset[0].ProviderId, Is.EqualTo("claude"));
    }
}
