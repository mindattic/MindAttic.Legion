using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Locks the enriched library at 1024 (16×8×8) and verifies the structured
/// <see cref="PersonaDetail"/> metadata stays aligned with the prompts.
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
    public void EnrichedCount_FactorsAs16x8x8() =>
        Assert.That(PersonaLibrary.EnrichedCount, Is.EqualTo(16 * 8 * 8));

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
            Assert.That(d.Age, Is.InRange(22, 78), d.Id);
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
    public void EveryAxisCombination_AppearsExactlyOnce()
    {
        var enrichedIds = PersonaLibrary.Enriched.Select(p => p.Id).ToHashSet();
        var combos = PersonaLibrary.AllDetails
            .Where(d => enrichedIds.Contains(d.Id))
            .Select(d => (d.Archetype, d.Worldview, d.Background))
            .ToList();
        Assert.That(combos, Is.Unique);
        Assert.That(combos, Has.Count.EqualTo(1024));
    }
}
