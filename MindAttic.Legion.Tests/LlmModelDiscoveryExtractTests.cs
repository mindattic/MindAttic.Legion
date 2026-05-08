using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Unit tests for <see cref="LlmModelDiscovery.ExtractModelIds"/> — the pure
/// JSON-shape-tolerant extractor that turns vendor-specific model-list
/// payloads into a flat list of ids. These tests pin every shape Legion has
/// run into in the wild so a vendor introducing a new wrapper or property
/// name fails loudly here, not at runtime against a live endpoint.
/// </summary>
[TestFixture]
public class LlmModelDiscoveryExtractTests
{
    [Test]
    public void OpenAiShape_DataArrayWithIdProperty()
    {
        var json = """{"data":[{"id":"gpt-4.1"},{"id":"gpt-4.1-mini"}]}""";
        var ids  = LlmModelDiscovery.ExtractModelIds("openai", json);
        Assert.That(ids, Is.EqualTo(new[] { "gpt-4.1", "gpt-4.1-mini" }));
    }

    [Test]
    public void GeminiShape_ModelsArrayWithNameProperty_TrimsModelsPrefix()
    {
        // Gemini returns "models/gemini-2.5-pro"; the catalog uses bare ids.
        var json = """{"models":[{"name":"models/gemini-2.5-pro"},{"name":"models/gemini-1.5-flash"}]}""";
        var ids  = LlmModelDiscovery.ExtractModelIds("gemini", json);
        Assert.That(ids, Is.EqualTo(new[] { "gemini-2.5-pro", "gemini-1.5-flash" }));
    }

    [Test]
    public void AnthropicShape_DataArrayWithIdProperty()
    {
        // Anthropic's /v1/models is OpenAI-shaped — same `data` wrapper.
        var json = """{"data":[{"id":"claude-opus-4-7"},{"id":"claude-sonnet-4-6"}]}""";
        var ids  = LlmModelDiscovery.ExtractModelIds("claude", json);
        Assert.That(ids, Is.EqualTo(new[] { "claude-opus-4-7", "claude-sonnet-4-6" }));
    }

    [Test]
    public void CohereShape_ModelsArrayWithNameProperty()
    {
        var json = """{"models":[{"name":"command-r-plus"},{"name":"command-r"}]}""";
        var ids  = LlmModelDiscovery.ExtractModelIds("cohere", json);
        Assert.That(ids, Is.EqualTo(new[] { "command-r-plus", "command-r" }));
    }

    [Test]
    public void BareArray_OfIdObjects_Works()
    {
        var json = """[{"id":"a"},{"id":"b"}]""";
        var ids  = LlmModelDiscovery.ExtractModelIds("openai", json);
        Assert.That(ids, Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void BareArray_OfStrings_Works()
    {
        // Some local tooling returns just an array of model names.
        var json = """["model-a","model-b"]""";
        var ids  = LlmModelDiscovery.ExtractModelIds("openai", json);
        Assert.That(ids, Is.EqualTo(new[] { "model-a", "model-b" }));
    }

    [Test]
    public void DuplicateIds_AreDedupedCaseInsensitively()
    {
        var json = """{"data":[{"id":"GPT-4.1"},{"id":"gpt-4.1"},{"id":"gpt-4.1-mini"}]}""";
        var ids  = LlmModelDiscovery.ExtractModelIds("openai", json);
        // First occurrence wins; second case-variant is dropped.
        Assert.That(ids, Is.EqualTo(new[] { "GPT-4.1", "gpt-4.1-mini" }));
    }

    [Test]
    public void EmptyData_ReturnsEmpty()
    {
        var ids = LlmModelDiscovery.ExtractModelIds("openai", """{"data":[]}""");
        Assert.That(ids, Is.Empty);
    }

    [Test]
    public void MalformedJson_ReturnsEmpty()
    {
        // Bad JSON shouldn't blow up — the discovery layer treats it as a soft
        // failure (the caller reports the underlying error separately).
        var ids = LlmModelDiscovery.ExtractModelIds("openai", "not-json-at-all");
        Assert.That(ids, Is.Empty);
    }

    [Test]
    public void ObjectWithUnknownPropertyNames_ReturnsEmpty()
    {
        // No id/name/model/model_id property → nothing to extract.
        var json = """{"data":[{"unknown_field":"x"}]}""";
        var ids  = LlmModelDiscovery.ExtractModelIds("openai", json);
        Assert.That(ids, Is.Empty);
    }

    [Test]
    public void ModelIdProperty_IsAlsoAccepted()
    {
        // Some legacy shapes use `model_id`.
        var json = """{"data":[{"model_id":"x"},{"model_id":"y"}]}""";
        var ids  = LlmModelDiscovery.ExtractModelIds("openai", json);
        Assert.That(ids, Is.EqualTo(new[] { "x", "y" }));
    }

    [Test]
    public void ModelProperty_IsAlsoAccepted()
    {
        var json = """{"data":[{"model":"foo"},{"model":"bar"}]}""";
        var ids  = LlmModelDiscovery.ExtractModelIds("openai", json);
        Assert.That(ids, Is.EqualTo(new[] { "foo", "bar" }));
    }

    [Test]
    public void IdPrecedesNamePrecedesModel_WhenSeveralPresent()
    {
        // Probe order: id > name > model > model_id. The first non-empty wins.
        var json = """{"data":[{"id":"id-wins","name":"name-loses","model":"model-loses"}]}""";
        var ids  = LlmModelDiscovery.ExtractModelIds("openai", json);
        Assert.That(ids, Is.EqualTo(new[] { "id-wins" }));
    }

    [Test]
    public void GeminiPrefix_IsOnlyStrippedForGemini()
    {
        // The "models/" prefix trim is intentionally Gemini-only — for any
        // other provider the prefix is part of the model id.
        var json = """{"data":[{"id":"models/gemini-2.5-pro"}]}""";
        var openaiIds = LlmModelDiscovery.ExtractModelIds("openai", json);
        var geminiIds = LlmModelDiscovery.ExtractModelIds("gemini", json);
        Assert.That(openaiIds, Is.EqualTo(new[] { "models/gemini-2.5-pro" }));
        Assert.That(geminiIds, Is.EqualTo(new[] { "gemini-2.5-pro" }));
    }

    [Test]
    public void MixedTypesInArray_AreHandledIndependently()
    {
        // A mixed array of strings + objects — both extraction paths fire.
        var json = """["literal-id",{"id":"object-id"}]""";
        var ids  = LlmModelDiscovery.ExtractModelIds("openai", json);
        Assert.That(ids, Is.EqualTo(new[] { "literal-id", "object-id" }));
    }
}
