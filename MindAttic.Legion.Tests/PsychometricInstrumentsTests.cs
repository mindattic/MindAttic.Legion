using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Integrity checks on the bundled item banks: stable counts, contiguous unique
/// ids, and correct per-scale loadings. These guard against a fat-fingered edit
/// silently skewing every persona's scores.
/// </summary>
[TestFixture]
public class PsychometricInstrumentsTests
{
    [Test]
    public void SetVersion_IsNonEmpty() =>
        Assert.That(PsychometricInstruments.SetVersion, Is.Not.Empty);

    [Test]
    public void FiveInstruments_AreBundled() =>
        Assert.That(PsychometricInstruments.All, Has.Count.EqualTo(5));

    [Test]
    public void ItemIds_AreUniqueAndContiguousFrom1()
    {
        var ids = PsychometricInstruments.All.SelectMany(i => i.Items).Select(i => i.Id).ToList();
        Assert.That(ids, Is.Unique);
        Assert.That(ids.OrderBy(x => x), Is.EqualTo(Enumerable.Range(1, ids.Count)));
        Assert.That(PsychometricInstruments.TotalItemCount, Is.EqualTo(ids.Count));
    }

    [Test]
    public void EveryItem_HasNonEmptyTextAndScale()
    {
        foreach (var item in PsychometricInstruments.All.SelectMany(i => i.Items))
        {
            Assert.That(item.Text, Is.Not.Empty, $"item {item.Id} text");
            Assert.That(item.Scale, Is.Not.Empty, $"item {item.Id} scale");
        }
    }

    [Test]
    public void EveryInstrument_UsesA1To5LikertRange()
    {
        foreach (var inst in PsychometricInstruments.All)
        {
            Assert.That(inst.Min, Is.EqualTo(1), inst.Key);
            Assert.That(inst.Max, Is.EqualTo(5), inst.Key);
            Assert.That(inst.Items, Is.Not.Empty, inst.Key);
            Assert.That(inst.Instructions, Is.Not.Empty, inst.Key);
        }
    }

    [TestCase("bigfive", "O", "C", "E", "A", "N", ExpectedResult = 4)]
    [TestCase("hexaco", "H", "E", "X", "A", "C", ExpectedResult = 4)]
    public int Instrument_HasExpectedItemsPerScale(string key, params string[] scales)
    {
        var inst = PsychometricInstruments.Get(key)!;
        var perScale = inst.Items.GroupBy(i => i.Scale).ToDictionary(g => g.Key, g => g.Count());
        foreach (var s in scales)
            Assert.That(perScale[s], Is.EqualTo(perScale[scales[0]]), $"{key}/{s} balance");
        // Return the items-per-scale count of the first scale for the ExpectedResult check.
        return perScale[scales[0]];
    }

    [Test]
    public void Mbti_LoadsOntoTheFourDichotomies()
    {
        var scales = PsychometricInstruments.Mbti.Items.Select(i => i.Scale).Distinct().OrderBy(s => s);
        Assert.That(scales, Is.EqualTo(new[] { "EI", "JP", "SN", "TF" }));
    }

    [Test]
    public void Enneagram_CoversAllNineTypes()
    {
        var types = PsychometricInstruments.Enneagram.Items.Select(i => i.Scale).Distinct()
            .Select(int.Parse).OrderBy(x => x);
        Assert.That(types, Is.EqualTo(Enumerable.Range(1, 9)));
    }

    [Test]
    public void Disc_HasFourDimensions()
    {
        var dims = PsychometricInstruments.Disc.Items.Select(i => i.Scale).Distinct().OrderBy(s => s);
        Assert.That(dims, Is.EqualTo(new[] { "C", "D", "I", "S" }));
    }
}
