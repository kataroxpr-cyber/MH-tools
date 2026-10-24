using System.Windows.Media;
using MH4UModern.Services;
using mh4edit;

namespace MH4UModern.ViewModels;

public sealed class ItemRowViewModel : BindableBase
{
    private readonly UndoManager _undo;
    private readonly DirtyTracker _dirty;
    private ImageSource? _iconImage;
    private ushort _iconImageForId;
    private bool _iconResolved;

    public ItemRowViewModel(int index, MonHunItem model, UndoManager undo, DirtyTracker dirty)
    {
        Index = index;
        Model = model;
        _undo = undo;
        _dirty = dirty;
    }

    public int Index { get; }
    public int Slot => Index + 1;
    public MonHunItem Model { get; }

    public ushort ID
    {
        get => Model.ID;
        set
        {
            if (Model.ID == value) return;
            var old = Model.ID;
            ApplyId(value);
            _dirty.MarkDirty();
            _undo.Record(() => ApplyId(old), () => ApplyId(value), $"item slot {Slot}");
        }
    }

    public ushort Count
    {
        get => Model.Count;
        set
        {
            var safe = value > 99 ? (ushort)99 : value;
            if (Model.Count == safe) return;
            var old = Model.Count;
            ApplyCount(safe);
            _dirty.MarkDirty();
            _undo.Record(() => ApplyCount(old), () => ApplyCount(safe), $"item count in slot {Slot}");
        }
    }

    public string Name => ID < MonHunItem.Names.Length ? MonHunItem.Names[ID] : $"Unknown #{ID}";
    public ItemCatalogEntry? Catalog => GameCatalogService.GetItem(ID);
    public string Category => Catalog?.Category ?? ItemCategoryService.GetCategory(ID, Name);
    public int Rarity => Catalog?.Rarity ?? 0;
    public string RarityText => Catalog?.RarityText ?? string.Empty;
    public string DatabaseType => Catalog?.Type ?? string.Empty;
    public string Description => Catalog?.Description ?? string.Empty;
    public string EconomyText => Catalog?.EconomyText ?? string.Empty;
    public bool IsOccupied => ID != 0;
    public ImageSource? IconImage
    {
        get
        {
            if (!_iconResolved || _iconImageForId != ID)
            {
                _iconImage = ItemIconService.Resolve(ID, Name);
                _iconImageForId = ID;
                _iconResolved = true;
            }
            return _iconImage;
        }
    }
    public bool HasIcon => IconImage is not null;

    // Last-resort vector fallback if a texture is missing from disk.
    public string IconGlyph => Category switch
    {
        "Consumables & tools" => "✚",
        "Ammo & coatings" => "●",
        "Nectar" => "◉",
        "Decorations" => "◆",
        "Tickets & coins" => "¤",
        "Palico scraps" => "♟",
        "Quest & supply" => "▤",
        _ => "◇"
    };

    public Brush IconBrush => Category switch
    {
        "Consumables & tools" => new SolidColorBrush(Color.FromRgb(78, 205, 140)),
        "Ammo & coatings" => new SolidColorBrush(Color.FromRgb(238, 108, 116)),
        "Nectar" => new SolidColorBrush(Color.FromRgb(246, 184, 73)),
        "Decorations" => new SolidColorBrush(Color.FromRgb(126, 181, 255)),
        "Tickets & coins" => new SolidColorBrush(Color.FromRgb(246, 166, 76)),
        "Palico scraps" => new SolidColorBrush(Color.FromRgb(199, 125, 244)),
        "Quest & supply" => new SolidColorBrush(Color.FromRgb(75, 205, 214)),
        _ => new SolidColorBrush(Color.FromRgb(165, 177, 191))
    };

    public void SetSilently(ushort id, ushort count)
    {
        Model.ID = id;
        Model.Count = count;
        Refresh();
    }

    public void Refresh()
    {
        _iconResolved = false;
        _iconImage = null;
        Raise(nameof(ID));
        Raise(nameof(Count));
        Raise(nameof(Name));
        Raise(nameof(Catalog));
        Raise(nameof(Category));
        Raise(nameof(Rarity));
        Raise(nameof(RarityText));
        Raise(nameof(DatabaseType));
        Raise(nameof(Description));
        Raise(nameof(EconomyText));
        Raise(nameof(IsOccupied));
        Raise(nameof(IconImage));
        Raise(nameof(HasIcon));
        Raise(nameof(IconGlyph));
        Raise(nameof(IconBrush));
    }

    private void ApplyId(ushort value)
    {
        Model.ID = value;
        Refresh();
    }

    private void ApplyCount(ushort value)
    {
        Model.Count = value;
        Refresh();
    }
}
