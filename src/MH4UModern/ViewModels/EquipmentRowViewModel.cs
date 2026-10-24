using System.Windows.Media;
using MH4UModern.Services;
using mh4edit;

namespace MH4UModern.ViewModels;

public sealed class EquipmentRowViewModel : BindableBase
{
    private MonHunEquip _model;
    private readonly DirtyTracker _dirty;

    public EquipmentRowViewModel(int index, MonHunEquip model, DirtyTracker dirty)
    {
        Index = index;
        _model = model;
        _dirty = dirty;
    }

    public int Index { get; }
    public int Slot => Index + 1;

    public MonHunEquip Model
    {
        get => _model;
        private set
        {
            _model = value;
            Refresh();
        }
    }

    public bool IsOccupied => Model.Type != 0;
    public string Type => Safe(() => Model.TypeString, "Unknown");
    public string Name => Safe(() => Model.IDString, $"ID {Model.ID}");
    public string Summary => IsOccupied ? $"{Type} · {Name}" : "Empty";

    public WeaponCatalogEntry? WeaponCatalog => Model is MonHunWeapon ? GameCatalogService.GetWeapon(Name) : null;
    public ArmorCatalogEntry? ArmorCatalog => Model is MonHunArmor ? GameCatalogService.GetArmor(Name) : null;
    public bool HasCatalogMetadata => WeaponCatalog is not null || ArmorCatalog is not null;

    public int StoredRarity => EquipmentCatalogIconService.GetRarity(Model);

    public string RarityText => !IsOccupied
        ? string.Empty
        : StoredRarity > 0
            ? $"Rarity {StoredRarity}{(Model is MonHunWeapon weapon && !weapon.IsRelic ? " · Crafted" : string.Empty)}"
            : "Rarity metadata pending";

    public string CatalogStats => WeaponCatalog?.Summary ?? ArmorCatalog?.Summary ?? string.Empty;

    public ImageSource? Image
    {
        get
        {
            try
            {
                return EquipmentCatalogIconService.Resolve(Model)
                       ?? (Model is MonHunArmor ? ArmorRarityService.ResolveArmorImage(Model) : null)
                       ?? Model.GetImage();
            }
            catch { return null; }
        }
    }

    public void Replace(MonHunEquip model)
    {
        Model = model;
        _dirty.MarkDirty();
    }

    public void ReplaceSilently(MonHunEquip model)
    {
        Model = model;
    }

    public void Refresh()
    {
        Raise(nameof(Model));
        Raise(nameof(IsOccupied));
        Raise(nameof(Type));
        Raise(nameof(Name));
        Raise(nameof(Summary));
        Raise(nameof(WeaponCatalog));
        Raise(nameof(ArmorCatalog));
        Raise(nameof(HasCatalogMetadata));
        Raise(nameof(StoredRarity));
        Raise(nameof(RarityText));
        Raise(nameof(CatalogStats));
        Raise(nameof(Image));
    }

    private static string Safe(Func<string> getter, string fallback)
    {
        try { return getter(); }
        catch { return fallback; }
    }
}
