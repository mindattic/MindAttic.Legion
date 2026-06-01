using MindAttic.Legion.Data;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Connection-string precedence: explicit argument → MINDATTIC_LEGION_DB env
/// var → LocalDB default. Mutates a process-global env var, so it runs
/// non-parallel and restores the original value.
/// </summary>
[TestFixture]
[NonParallelizable]
public class LegionConnectionStringTests
{
    private string? original;

    [SetUp]
    public void SaveEnv() => original = Environment.GetEnvironmentVariable(LegionConnectionString.EnvVar);

    [TearDown]
    public void RestoreEnv() => Environment.SetEnvironmentVariable(LegionConnectionString.EnvVar, original);

    [Test]
    public void ExplicitArgument_WinsOverEverything()
    {
        Environment.SetEnvironmentVariable(LegionConnectionString.EnvVar, "Server=env;Database=X;");
        Assert.That(LegionConnectionString.Resolve("Server=explicit;Database=Y;"),
            Is.EqualTo("Server=explicit;Database=Y;"));
    }

    [Test]
    public void EnvVar_UsedWhenNoExplicitArgument()
    {
        Environment.SetEnvironmentVariable(LegionConnectionString.EnvVar, "Server=env;Database=X;");
        Assert.That(LegionConnectionString.Resolve(), Is.EqualTo("Server=env;Database=X;"));
    }

    [Test]
    public void Default_UsedWhenNothingConfigured()
    {
        Environment.SetEnvironmentVariable(LegionConnectionString.EnvVar, null);
        Assert.That(LegionConnectionString.Resolve(), Is.EqualTo(LegionConnectionString.Default));
        Assert.That(LegionConnectionString.Default, Does.Contain("MSSQLLocalDB"));
    }

    [Test]
    public void WhitespaceArgument_TreatedAsAbsent()
    {
        Environment.SetEnvironmentVariable(LegionConnectionString.EnvVar, null);
        Assert.That(LegionConnectionString.Resolve("   "), Is.EqualTo(LegionConnectionString.Default));
    }
}
