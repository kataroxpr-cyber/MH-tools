using System.Runtime.CompilerServices;
using MH4UModern.Services;
using mh4edit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ClassicMhTools.Core.Tests;

internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.CurrentDirectory = AppContext.BaseDirectory;
        WaistArmorIdFixService.Apply();
    }

    internal static MonHunSave CreateSave(MemoryStream stream)
    {
        // Synthetic decrypted data exercises the wrapper without opening or writing a real save.
        var save = (MonHunSave)RuntimeHelpers.GetUninitializedObject(typeof(MonHunSave));
        save.ms = stream;
        return save;
    }

    internal static MonHunEquip CreateEquipment(ushort id)
    {
        var equipment = MonHunEquip.Create(new byte[28]);
        equipment.Type = 3;
        equipment.ID = id;
        return MonHunEquip.Create(equipment.SerializeOnlyTypeAndId());
    }
}
