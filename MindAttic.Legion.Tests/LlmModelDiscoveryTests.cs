using System.Net;
using MindAttic.Legion;
using MindAttic.Legion.Tests.TestSupport;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

[TestFixture]
public class LlmModelDiscoveryTests
{
    private TempCredentialScope creds = null!;

    [SetUp]
    public void SetUp()
    {
        creds = new TempCredentialScope();
    }

    [TearDown]
    public void TearDown()
    {
        creds.Dispose();
    }

    [Test]
    public async Task DiscoverOne_CloudProviderWithoutKey_ReturnsMissingCredentialAndCatalogModels()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, """{"data":[{"id":"gpt-4.1-mini"}]}""");
        var discovery = new LlmModelDiscovery(new HttpClient(handler));

        var result = await discovery.DiscoverOneAsync("openai");

        Assert.That(result.LiveModelQuerySucceeded, Is.False);
        Assert.That(result.Diagnosis, Is.EqualTo(LlmHealthDiagnosis.MissingCredential));
        Assert.That(result.LiveModels, Is.Empty);
        Assert.That(result.KnownModels, Does.Contain("gpt-4.1-mini"));
        Assert.That(handler.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task DiscoverOne_CloudProviderWithKey_ParsesOpenAiCompatibleModelList()
    {
        creds.WriteKey("openai", "sk-test");
        var handler = new FixedResponseHandler(HttpStatusCode.OK,
            """{"data":[{"id":"gpt-4.1-mini"},{"id":"gpt-4.1"}]}""");
        var discovery = new LlmModelDiscovery(new HttpClient(handler));

        var result = await discovery.DiscoverOneAsync("openai");

        Assert.That(result.LiveModelQuerySucceeded, Is.True);
        Assert.That(result.LiveModels, Is.EqualTo(new[] { "gpt-4.1-mini", "gpt-4.1" }));
        Assert.That(handler.Requests.Single().AuthScheme, Is.EqualTo("Bearer"));
        Assert.That(handler.Requests.Single().AuthValue, Is.EqualTo("sk-test"));
    }

    [Test]
    public async Task DiscoverOne_Ollama_DoesNotRequireKeyAndParsesTags()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK,
            """{"models":[{"name":"llama3.2:latest"},{"name":"mistral:latest"}]}""");
        var discovery = new LlmModelDiscovery(new HttpClient(handler));

        var result = await discovery.DiscoverOneAsync("ollama");

        Assert.That(result.RequiresApiKey, Is.False);
        Assert.That(result.HasCredential, Is.True);
        Assert.That(result.LiveModelQuerySucceeded, Is.True);
        Assert.That(result.ModelsEndpoint, Is.EqualTo("http://localhost:11434/api/tags"));
        Assert.That(result.ChatEndpoint, Is.EqualTo("http://localhost:11434/v1/chat/completions"));
        Assert.That(result.LiveModels, Is.EqualTo(new[] { "llama3.2:latest", "mistral:latest" }));
        Assert.That(handler.Requests.Single().AuthScheme, Is.Null);
    }

    [Test]
    public async Task DiscoverOne_LmStudio_UsesConfiguredBaseUrlAndParsesModels()
    {
        var previous = Environment.GetEnvironmentVariable("MINDATTIC_LEGION_LMSTUDIO_BASE_URL");
        Environment.SetEnvironmentVariable("MINDATTIC_LEGION_LMSTUDIO_BASE_URL", "http://localhost:4321");
        try
        {
            var handler = new FixedResponseHandler(HttpStatusCode.OK,
                """{"data":[{"id":"local-qwen"},{"id":"local-llama"}]}""");
            var discovery = new LlmModelDiscovery(new HttpClient(handler));

            var result = await discovery.DiscoverOneAsync("lmstudio");

            Assert.That(result.LiveModelQuerySucceeded, Is.True);
            Assert.That(result.ModelsEndpoint, Is.EqualTo("http://localhost:4321/v1/models"));
            Assert.That(result.ChatEndpoint, Is.EqualTo("http://localhost:4321/v1/chat/completions"));
            Assert.That(result.LiveModels, Is.EqualTo(new[] { "local-qwen", "local-llama" }));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MINDATTIC_LEGION_LMSTUDIO_BASE_URL", previous);
        }
    }

    [Test]
    public async Task DiscoverOne_Gemini_TrimsModelsPrefix()
    {
        creds.WriteKey("gemini", "AIza-test");
        var handler = new FixedResponseHandler(HttpStatusCode.OK,
            """{"models":[{"name":"models/gemini-2.5-flash"},{"name":"models/gemini-2.5-pro"}]}""");
        var discovery = new LlmModelDiscovery(new HttpClient(handler));

        var result = await discovery.DiscoverOneAsync("gemini");

        Assert.That(result.LiveModels, Is.EqualTo(new[] { "gemini-2.5-flash", "gemini-2.5-pro" }));
        Assert.That(handler.Requests.Single().Uri.Query, Does.Contain("key=AIza-test"));
    }
}
