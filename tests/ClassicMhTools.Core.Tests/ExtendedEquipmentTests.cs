using MH4UModern.Services;
using mh4edit;

namespace ClassicMhTools.Core.Tests;

public sealed class ExtendedEquipmentTests
{
    [Fact]
    public void Box15RoundTripPreservesEverySlotSurroundingBytesAndStreamPosition()
    {
        const int extraOffset = 0xB05E;
        const int extraLength = 100 * 28;
        var original = Enumerable.Repeat((byte)0xA5, extraOffset + extraLength + 32).ToArray();
        using var stream = new MemoryStream(original.ToArray());
        stream.Position = 37;
        var save = TestEnvironment.CreateSave(stream);
        var equipment = Enumerable.Range(1, 100).Select(i => TestEnvironment.CreateEquipment((ushort)i)).ToArray();

        ExtendedEquipmentService.WriteTail(save, equipment);
        Assert.Equal(37L, stream.Position);
        var written = stream.ToArray();
        Assert.Equal(original[..extraOffset], written[..extraOffset]);
        Assert.Equal(original[(extraOffset + extraLength)..], written[(extraOffset + extraLength)..]);
        var reopened = ExtendedEquipmentService.ReadTail(save);
        Assert.Equal(100, reopened.Length);
        Assert.Equal(37L, stream.Position);
        for (var i = 0; i < 100; i++)
            Assert.Equal(equipment[i].Serialize(), reopened[i].Serialize());
    }

    [Theory]
    [InlineData(99)]
    [InlineData(101)]
    public void IncorrectTailLengthFailsBeforeWriting(int count)
    {
        using var stream = new MemoryStream(new byte[0xC000]);
        var before = stream.ToArray();
        var save = TestEnvironment.CreateSave(stream);
        Assert.Throws<InvalidOperationException>(() => ExtendedEquipmentService.WriteTail(save, new MonHunEquip[count]));
        Assert.Equal(before, stream.ToArray());
    }
}
