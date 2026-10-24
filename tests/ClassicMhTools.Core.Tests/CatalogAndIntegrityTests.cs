using System.Security.Cryptography;
using MH4UModern.Services;
using mh4edit;

namespace ClassicMhTools.Core.Tests;

public sealed class CatalogAndIntegrityTests
{
    [Fact]
    public void SaveCoreMatchesTheImportedBaselineByteForByte()
    {
        using var stream = File.OpenRead(typeof(MonHunSave).Assembly.Location);
        Assert.Equal("30282552B7996117FA9041738758B548F1A63ABA74791D34AD0FA80938BAA214", Convert.ToHexString(SHA256.HashData(stream)));
    }

    [Theory]
    [InlineData(51, "Golden Lunecoil")]
    [InlineData(59, "Rathalos Faulds")]
    [InlineData(141, "Kecha Faulds")]
    [InlineData(151, "Tetsucabra Faulds")]
    public void WaistLookupUsesTheCorrectSaveId(int id, string name)
    {
        var before = MonHunEquipStatic.allEqpWaist;
        WaistArmorIdFixService.Apply();
        Assert.Same(before, MonHunEquipStatic.allEqpWaist);
        Assert.Equal(name, MonHunEquipStatic.allEqpWaist[id]);
    }

    [Fact]
    public void MetadataIsResolvedBySaveIdWithoutReplacingItWithADatabaseId()
    {
        var matched = Enumerable.Range(0, MonHunItem.Names.Length)
            .Select(i => GameCatalogService.GetItem((ushort)i)).OfType<ItemCatalogEntry>().ToArray();
        Assert.Equal(1887, matched.Length);
        Assert.Contains(matched, entry => entry.EditorId != entry.DatabaseId);
        foreach (var entry in matched)
            Assert.Equal(MonHunItem.Names[entry.EditorId], entry.Name);
    }

    [Fact]
    public void SkillFirstPickerOnlyOffersJewelsWithAPositiveMatchingEffect()
    {
        var choices = DecorationCatalogService.ForSkill("Attack");
        Assert.NotEmpty(choices);
        Assert.All(choices, choice =>
        {
            Assert.Equal(MonHunEquipStatic.allJewels[choice.EditorId].Trim(), choice.Name);
            Assert.Contains(choice.Effects, effect => effect.Skill == "Attack" && effect.Points > 0);
        });
    }
}
