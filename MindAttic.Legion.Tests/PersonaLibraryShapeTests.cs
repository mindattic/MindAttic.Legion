using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Locks the library at 1024 personas (deterministically sampled from the
/// 40×16×16 space) and verifies the structured <see cref="PersonaDetail"/>
/// metadata stays aligned with the prompts.
/// </summary>
[TestFixture]
public class PersonaLibraryShapeTests
{
    [Test]
    public void EnrichedCount_Is1024()
    {
        Assert.That(PersonaLibrary.EnrichedCount, Is.EqualTo(1024));
        Assert.That(PersonaLibrary.Enriched, Has.Count.EqualTo(1024));
    }

    [Test]
    public void Sampling_IsDeterministic_SameComboSetEveryBuild()
    {
        // The fixed-seed sample must be stable: identical id→combo mapping across calls.
        var a = PersonaLibrary.AllDetails.Select(d => (d.Id, d.Archetype, d.Worldview, d.Background)).ToList();
        var b = PersonaLibrary.AllDetails.Select(d => (d.Id, d.Archetype, d.Worldview, d.Background)).ToList();
        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void Pronouns_AreOnlyFemaleOrMalePerspective()
    {
        var sets = PersonaLibrary.AllDetails.Select(d => d.Pronouns).Distinct().ToList();
        Assert.That(sets, Is.EquivalentTo(new[] { "she/her", "he/him" }));
    }

    [Test]
    public void Names_AreUnique_MatchPronounGender_NoLastInitial()
    {
        var female = new HashSet<string>(PersonaNames.Female);
        var male = new HashSet<string>(PersonaNames.Male);
        var nameById = PersonaLibrary.All.ToDictionary(p => p.Id, p => p.Name);

        Assert.That(nameById.Values.Distinct().Count(), Is.EqualTo(1024), "all names unique");
        foreach (var d in PersonaLibrary.AllDetails)
        {
            var name = nameById[d.Id];
            Assert.That(name, Does.Not.Contain("."), $"{d.Id}: no last initial");
            Assert.That(name, Does.Not.Contain(" "), $"{d.Id}: single given name");
            if (d.Pronouns == "she/her")
                Assert.That(female, Does.Contain(name), $"{d.Id}: she/her -> female name");
            else
                Assert.That(male, Does.Contain(name), $"{d.Id}: he/him -> male name");
        }
    }

    [Test]
    public void AllDetails_AlignWithAllByIndexAndId()
    {
        var all = PersonaLibrary.All;
        var details = PersonaLibrary.AllDetails;
        Assert.That(details, Has.Count.EqualTo(all.Count));
        for (var i = 0; i < all.Count; i++)
            Assert.That(details[i].Id, Is.EqualTo(all[i].Id), $"misaligned at index {i}");
    }

    [Test]
    public void EnrichedDetails_HaveAllAxesPopulated()
    {
        var enrichedIds = PersonaLibrary.Enriched.Select(p => p.Id).ToHashSet();
        foreach (var d in PersonaLibrary.AllDetails.Where(d => enrichedIds.Contains(d.Id)))
        {
            Assert.That(d.IsDefault, Is.False, d.Id);
            Assert.That(d.Archetype, Is.Not.Null.And.Not.Empty, d.Id);
            Assert.That(d.Worldview, Is.Not.Null.And.Not.Empty, d.Id);
            Assert.That(d.Background, Is.Not.Null.And.Not.Empty, d.Id);
            Assert.That(d.Age, Is.InRange(18, 80), d.Id);
            Assert.That(d.Pronouns, Is.Not.Null.And.Not.Empty, d.Id);
            Assert.That(d.Quirk, Is.Not.Null.And.Not.Empty, d.Id);
        }
    }

    [Test]
    public void NoDefaultPersonas_EverythingIsEnriched()
    {
        // A bare LLM has no persona — there are no per-provider "default" entries.
        Assert.That(PersonaLibrary.AllDetails.Any(d => d.IsDefault), Is.False);
        Assert.That(PersonaLibrary.All.Any(p => p.Id.StartsWith("default-")), Is.False);
        Assert.That(PersonaLibrary.Count, Is.EqualTo(1024));
    }

    [Test]
    public void Profiles_AreEmbeddedAndLoad()
    {
        Assert.That(PersonaLibrary.Profiles, Has.Count.GreaterThanOrEqualTo(1024), "embedded profiles should cover the library");
        var p = PersonaLibrary.GetProfile("persona-0500");
        Assert.That(p, Is.Not.Null);
        Assert.That(p!.PersonaId, Is.EqualTo("persona-0500"));
        Assert.That(p.Mbti.Type, Has.Length.EqualTo(4));
        Assert.That(p.Ocean.Openness, Is.InRange(0, 100));
        Assert.That(PersonaLibrary.GetProfile("does-not-exist"), Is.Null);
    }

    [Test]
    public void SampledCombinations_AreAllUnique()
    {
        // Sampled (not the full cube), but every selected (archetype, worldview,
        // background) triple must be distinct.
        var combos = PersonaLibrary.AllDetails
            .Select(d => (d.Archetype, d.Worldview, d.Background))
            .ToList();
        Assert.That(combos, Is.Unique);
        Assert.That(combos, Has.Count.EqualTo(1024));
    }

    [Test]
    public void Sampling_DrawsAcrossTheWholeVocabulary()
    {
        // A good sample should touch most of every axis, not cluster.
        var d = PersonaLibrary.AllDetails;
        Assert.That(d.Select(x => x.Archetype).Distinct().Count(), Is.GreaterThanOrEqualTo(36), "archetypes covered");
        Assert.That(d.Select(x => x.Worldview).Distinct().Count(), Is.EqualTo(16), "all worldviews covered");
        Assert.That(d.Select(x => x.Background).Distinct().Count(), Is.EqualTo(16), "all backgrounds covered");
    }
}
