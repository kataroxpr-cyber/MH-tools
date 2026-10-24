using System.Runtime.CompilerServices;
using MH4UModern.Services;
using mh4edit;

namespace ClassicMhTools.Core.Tests;

public sealed class EquipmentReferenceTests
{
    [Fact]
    public void RemappingKeepsEquippedGearAndTheUnequippedSentinel()
    {
        var before = new EquippedIndexState(1399, 1400, 1401, 1499, 4, 5, ushort.MaxValue);
        var mapping = new Dictionary<int, int> { [1399] = 1499, [1400] = 0, [1401] = 1, [1499] = 2 };
        var after = EquipmentReferenceService.Remap(before, mapping);
        Assert.Equal(new EquippedIndexState(1499, 0, 1, 2, 4, 5, ushort.MaxValue), after);
        Assert.True(EquipmentReferenceService.IsEquipped(after, 1499));
        Assert.False(EquipmentReferenceService.IsEquipped(after, ushort.MaxValue));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void InvalidMappingDoesNotReplaceTheOriginalReference(int destination)
    {
        var before = new EquippedIndexState(1400, 1, 2, 3, 4, 5, 6);
        Assert.Equal(before, EquipmentReferenceService.Remap(before, new Dictionary<int, int> { [1400] = destination }));
    }

    [Fact]
    public void TheBundledCoreStillExposesAllSevenEquippedReferences()
    {
        var character = (MonHunCharacter)RuntimeHelpers.GetUninitializedObject(typeof(MonHunCharacter));
        var expected = new EquippedIndexState(1499, 1400, 20, 21, 22, 23, ushort.MaxValue);
        EquipmentReferenceService.Apply(character, expected);
        Assert.Equal(expected, EquipmentReferenceService.Capture(character));
    }
}
