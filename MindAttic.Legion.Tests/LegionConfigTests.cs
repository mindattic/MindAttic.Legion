using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Unit tests for <see cref="LegionConfig"/> — the per-project
/// <c>legion.json</c> loader every project uses to declare its voter panel.
/// Mistakes here silently change what the CLI's vote command treats as the
/// active panel, so directory-walk boundaries, tolerant parsing, and the
/// merge semantics of <see cref="LegionConfig.ApplyTo"/> each get dedicated
/// coverage.
/// </summary>
[TestFixture]
public class LegionConfigTests
{
    private string tempDir = "";

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "legion-config-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }

    // ── LoadFromDirectory: finds and walks up ──────────────────────────────

    [Test]
    public void LoadFromDirectory_FindsFileInStartDir()
    {
        File.WriteAllText(Path.Combine(tempDir, LegionConfig.FileName), """{"voters":["claude"]}""");

        var cfg = LegionConfig.LoadFromDirectory(tempDir);

        Assert.That(cfg, Is.Not.Null);
        Assert.That(cfg!.Voters, Is.EquivalentTo(new[] { "claude" }));
    }

    [Test]
    public void LoadFromDirectory_WalksUpToParentDir()
    {
        File.WriteAllText(Path.Combine(tempDir, LegionConfig.FileName), """{"voters":["openai"]}""");
        var child = Path.Combine(tempDir, "child");
        Directory.CreateDirectory(child);

        var cfg = LegionConfig.LoadFromDirectory(child);

        Assert.That(cfg, Is.Not.Null);
        Assert.That(cfg!.Voters, Is.EquivalentTo(new[] { "openai" }));
    }

    [Test]
    public void LoadFromDirectory_NoFileAnywhere_ReturnsNull()
    {
        Assert.That(LegionConfig.LoadFromDirectory(tempDir), Is.Null);
    }

    [Test]
    public void LoadFromDirectory_BlankStartDir_ReturnsNull()
    {
        Assert.That(LegionConfig.LoadFromDirectory("   "), Is.Null);
    }

    [Test]
    public void LoadFromDirectory_InvalidPath_ReturnsNull()
    {
        Assert.That(LegionConfig.LoadFromDirectory("\0invalid"), Is.Null);
    }

    [Test]
    public void LoadFromDirectory_FileWithinTwelveLevels_IsFound()
    {
        // The walk checks the start dir plus 11 ancestors (12 total). Placing
        // the file at the 12th checked directory — 11 levels up — must hit.
        var deepest = CreateNestedDirs(levels: 12);
        var boundaryDir = Path.Combine(tempDir, "level1");
        File.WriteAllText(Path.Combine(boundaryDir, LegionConfig.FileName), """{"voters":["claude"]}""");

        var cfg = LegionConfig.LoadFromDirectory(deepest);

        Assert.That(cfg, Is.Not.Null);
        Assert.That(cfg!.Voters, Is.EquivalentTo(new[] { "claude" }));
    }

    [Test]
    public void LoadFromDirectory_FileBeyondTwelveLevels_StopsAndReturnsNull()
    {
        // tempDir is the 13th ancestor of the deepest "level12" dir — one past
        // the 12 directories the walk actually checks — so it must not be found.
        var deepest = CreateNestedDirs(levels: 12);
        File.WriteAllText(Path.Combine(tempDir, LegionConfig.FileName), """{"voters":["claude"]}""");

        Assert.That(LegionConfig.LoadFromDirectory(deepest), Is.Null);
    }

    private string CreateNestedDirs(int levels)
    {
        var path = tempDir;
        for (var i = 1; i <= levels; i++)
            path = Path.Combine(path, $"level{i}");
        Directory.CreateDirectory(path);
        return path;
    }

    // ── LoadFromFile: tolerant parsing ─────────────────────────────────────

    [Test]
    public void LoadFromFile_PlainJson_Parses()
    {
        var path = WriteConfig("""{"voters":["claude","openai"],"judge":"claude"}""");

        var cfg = LegionConfig.LoadFromFile(path);

        Assert.That(cfg, Is.Not.Null);
        Assert.That(cfg!.Voters, Is.EquivalentTo(new[] { "claude", "openai" }));
        Assert.That(cfg.Judge, Is.EqualTo("claude"));
    }

    [Test]
    public void LoadFromFile_TolerantOfComments()
    {
        var json = """
            {
                // panel for this project
                "voters": ["claude"], /* trusted only */
                "judge": "claude"
            }
            """;
        var path = WriteConfig(json);

        var cfg = LegionConfig.LoadFromFile(path);

        Assert.That(cfg, Is.Not.Null);
        Assert.That(cfg!.Voters, Is.EquivalentTo(new[] { "claude" }));
    }

    [Test]
    public void LoadFromFile_TolerantOfTrailingCommas()
    {
        var json = """{"voters":["claude","openai",],"models":{"claude":"opus",},}""";
        var path = WriteConfig(json);

        var cfg = LegionConfig.LoadFromFile(path);

        Assert.That(cfg, Is.Not.Null);
        Assert.That(cfg!.Voters, Is.EquivalentTo(new[] { "claude", "openai" }));
        Assert.That(cfg.Models["claude"], Is.EqualTo("opus"));
    }

    [Test]
    public void LoadFromFile_CaseInsensitiveKeys()
    {
        var json = """{"VOTERS":["claude"],"Judge":"claude","MODELS":{"claude":"opus"},"APIKEYS":{"claude":"k1"},"MAXCONCURRENCY":3}""";
        var path = WriteConfig(json);

        var cfg = LegionConfig.LoadFromFile(path);

        Assert.That(cfg, Is.Not.Null);
        Assert.That(cfg!.Voters, Is.EquivalentTo(new[] { "claude" }));
        Assert.That(cfg.Judge, Is.EqualTo("claude"));
        Assert.That(cfg.Models["claude"], Is.EqualTo("opus"));
        Assert.That(cfg.ApiKeys["claude"], Is.EqualTo("k1"));
        Assert.That(cfg.MaxConcurrency, Is.EqualTo(3));
    }

    [Test]
    public void LoadFromFile_MalformedJson_ReturnsNull()
    {
        var path = WriteConfig("""{"voters": ["claude" """);

        Assert.That(LegionConfig.LoadFromFile(path), Is.Null);
    }

    [Test]
    public void LoadFromFile_MissingFile_ReturnsNull()
    {
        Assert.That(LegionConfig.LoadFromFile(Path.Combine(tempDir, "nope.json")), Is.Null);
    }

    private string WriteConfig(string json)
    {
        var path = Path.Combine(tempDir, LegionConfig.FileName);
        File.WriteAllText(path, json);
        return path;
    }

    // ── ApplyTo: voters ─────────────────────────────────────────────────────

    [Test]
    public void ApplyTo_NonEmptyVoters_ReplacesAllowedProviderIds()
    {
        var cfg = new VotingConfiguration { AllowedProviderIds = new() { "cohere" } };
        var legion = new LegionConfig { Voters = { "claude", "OPENAI" } };

        legion.ApplyTo(cfg);

        Assert.That(cfg.AllowedProviderIds, Is.EquivalentTo(new[] { "claude", "OPENAI" }));
        Assert.That(cfg.AllowedProviderIds.Contains("cohere"), Is.False);
    }

    [Test]
    public void ApplyTo_Voters_IsCaseInsensitiveSet()
    {
        var cfg = new VotingConfiguration();
        var legion = new LegionConfig { Voters = { "Claude" } };

        legion.ApplyTo(cfg);

        Assert.That(cfg.AllowedProviderIds.Contains("CLAUDE"), Is.True);
        Assert.That(cfg.AllowedProviderIds.Contains("claude"), Is.True);
    }

    [Test]
    public void ApplyTo_EmptyVoters_LeavesAllowedProviderIdsUnchanged()
    {
        var original = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cohere" };
        var cfg = new VotingConfiguration { AllowedProviderIds = original };
        var legion = new LegionConfig();

        legion.ApplyTo(cfg);

        Assert.That(cfg.AllowedProviderIds, Is.SameAs(original));
    }

    // ── ApplyTo: judge ──────────────────────────────────────────────────────

    [Test]
    public void ApplyTo_NonBlankJudge_SetsJudgeProviderId()
    {
        var cfg = new VotingConfiguration { JudgeProviderId = "claude" };
        var legion = new LegionConfig { Judge = "openai" };

        legion.ApplyTo(cfg);

        Assert.That(cfg.JudgeProviderId, Is.EqualTo("openai"));
    }

    [Test]
    public void ApplyTo_NullJudge_LeavesJudgeProviderIdUnchanged()
    {
        var cfg = new VotingConfiguration { JudgeProviderId = "claude" };
        var legion = new LegionConfig { Judge = null };

        legion.ApplyTo(cfg);

        Assert.That(cfg.JudgeProviderId, Is.EqualTo("claude"));
    }

    [Test]
    public void ApplyTo_BlankJudge_LeavesJudgeProviderIdUnchanged()
    {
        var cfg = new VotingConfiguration { JudgeProviderId = "claude" };
        var legion = new LegionConfig { Judge = "   " };

        legion.ApplyTo(cfg);

        Assert.That(cfg.JudgeProviderId, Is.EqualTo("claude"));
    }

    // ── ApplyTo: models / apiKeys merge ─────────────────────────────────────

    [Test]
    public void ApplyTo_MergesModels_SkippingBlankKeysAndValues()
    {
        var cfg = new VotingConfiguration { ModelOverrides = { ["openai"] = "prior-model" } };
        var legion = new LegionConfig
        {
            Models =
            {
                ["claude"] = "claude-opus",
                [""] = "ignored-blank-key",
                ["openai"] = "   ",
            },
        };

        legion.ApplyTo(cfg);

        Assert.That(cfg.ModelOverrides["claude"], Is.EqualTo("claude-opus"));
        Assert.That(cfg.ModelOverrides["openai"], Is.EqualTo("prior-model"));
        Assert.That(cfg.ModelOverrides.ContainsKey(""), Is.False);
    }

    [Test]
    public void ApplyTo_MergesApiKeys_SkippingBlankKeysAndValues()
    {
        var cfg = new VotingConfiguration { ApiKeys = { ["openai"] = "prior-key" } };
        var legion = new LegionConfig
        {
            ApiKeys =
            {
                ["claude"] = "k1",
                ["  "] = "ignored-blank-key",
                ["openai"] = "",
            },
        };

        legion.ApplyTo(cfg);

        Assert.That(cfg.ApiKeys["claude"], Is.EqualTo("k1"));
        Assert.That(cfg.ApiKeys["openai"], Is.EqualTo("prior-key"));
        Assert.That(cfg.ApiKeys.ContainsKey("  "), Is.False);
    }

    // ── ApplyTo: maxConcurrency ──────────────────────────────────────────────

    [Test]
    public void ApplyTo_MaxConcurrencySet_IsApplied()
    {
        var cfg = new VotingConfiguration { MaxConcurrency = null };
        var legion = new LegionConfig { MaxConcurrency = 5 };

        legion.ApplyTo(cfg);

        Assert.That(cfg.MaxConcurrency, Is.EqualTo(5));
    }

    [Test]
    public void ApplyTo_MaxConcurrencyNull_LeavesUnchanged()
    {
        var cfg = new VotingConfiguration { MaxConcurrency = 7 };
        var legion = new LegionConfig { MaxConcurrency = null };

        legion.ApplyTo(cfg);

        Assert.That(cfg.MaxConcurrency, Is.EqualTo(7));
    }
}
