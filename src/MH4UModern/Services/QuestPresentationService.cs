using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClassicMhTools.Core.CustomQuests;

namespace MH4UModern.Services;

public sealed record QuestMonsterIcon(uint Id, string Name, ImageSource? Image);

public static class QuestPresentationService
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "assets", "quests");
    public static QuestReferenceCatalog Catalog { get; } = QuestReferenceCatalog.Load(Path.Combine(Root, "reference.json"));
    private static readonly Lazy<BitmapSource> MonsterSheet = new(() => LoadBitmap("monsters.png"));
    private static readonly ConcurrentDictionary<int, ImageSource> Monsters = new();
    private static readonly ConcurrentDictionary<byte, ImageSource> Maps = new();

    public static QuestMonsterIcon Monster(uint id)
    {
        var name = Catalog.MonsterNames.GetValueOrDefault(id, $"Monster {id}");
        var index = Catalog.MonsterIcons.GetValueOrDefault(id, 0);
        return new(id, name, PreviewImage(index));
    }

    public static QuestMonsterIcon Preview(ushort id) => new(id, Catalog.PreviewNames.GetValueOrDefault(id, $"Artwork {id}"), id == 98 ? null : PreviewImage(id));
    private static ImageSource PreviewImage(int index) => Monsters.GetOrAdd(index, i =>
        {
            var position = Catalog.SpritePositions.GetValueOrDefault(i, new(0, 0));
            var sheet = MonsterSheet.Value;
            if (position.X + 36 > sheet.PixelWidth || position.Y + 36 > sheet.PixelHeight) position = new(0, 0);
            var crop = new CroppedBitmap(sheet, new Int32Rect(position.X, position.Y, 36, 36));
            crop.Freeze();
            return crop;
        });

    public static string MapName(byte id) => Catalog.MapNames.GetValueOrDefault(id, $"Map {id}");
    public static ImageSource? MapIcon(byte id) => File.Exists(Path.Combine(Root, $"map{id}.png"))
        ? Maps.GetOrAdd(id, n => LoadBitmap($"map{n}.png")) : null;

    private static BitmapImage LoadBitmap(string file)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(Path.Combine(Root, file));
        image.EndInit();
        image.Freeze();
        return image;
    }
}
