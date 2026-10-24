using System.Windows.Media;
using MH4UModern.Services;
using mh4edit;

namespace MH4UModern.ViewModels;

public sealed record CharacterChoice(byte Value, string Label);

public sealed class CharacterEditorViewModel : BindableBase
{
    private readonly MonHunCharacter _model;
    private readonly UndoManager _undo;
    private readonly DirtyTracker _dirty;

    public CharacterEditorViewModel(MonHunCharacter model, UndoManager undo, DirtyTracker dirty)
    {
        _model = model;
        _undo = undo;
        _dirty = dirty;
    }

    public MonHunCharacter Model => _model;
    public IReadOnlyList<string> GenderOptions { get; } = new[] { "Male", "Female" };

    // MH4U character creation exposes 16 faces, 28 hairstyles, 20 voices,
    // 6 clothing types, 10 eye colors and 12 feature/makeup types.
    // The save stores these as zero-based bytes, while the UI labels them 1-based.
    public IReadOnlyList<CharacterChoice> FaceOptions { get; } = BuildChoices(16, "Face");
    public IReadOnlyList<CharacterChoice> HairStyleOptions { get; } = BuildChoices(28, "Hair Style");
    public IReadOnlyList<CharacterChoice> VoiceOptions { get; } = BuildChoices(20, "Voice");
    public IReadOnlyList<CharacterChoice> ClothingTypeOptions { get; } = BuildChoices(6, "Clothing");
    public IReadOnlyList<CharacterChoice> EyeColorOptions { get; } = BuildChoices(10, "Eye Color");
    public IReadOnlyList<CharacterChoice> FeaturesTypeOptions { get; } = BuildChoices(12, "Feature");

    public string Name
    {
        get => _model.Name;
        set
        {
            value ??= string.Empty;
            if (value.Length > 12) value = value[..12];
            if (_model.Name == value) return;
            var old = _model.Name;
            _model.Name = value;
            Raise();
            _dirty.MarkDirty();
            _undo.Record(
                () => { _model.Name = old; Raise(nameof(Name)); },
                () => { _model.Name = value; Raise(nameof(Name)); },
                "character name");
        }
    }

    // The same byte represented by the runtime gender codes: 0 = male, 1 = female.
    public string Gender
    {
        get => _model.gender == 1 ? "Female" : "Male";
        set
        {
            var next = string.Equals(value, "Female", StringComparison.OrdinalIgnoreCase) ? (byte)1 : (byte)0;
            SetByte(nameof(Gender), _model.gender, next, v => _model.gender = v, () => Raise(nameof(Gender)));
        }
    }

    public byte Face
    {
        get => _model.face;
        set => SetByte(nameof(Face), _model.face, value, v => _model.face = v, () => Raise(nameof(Face)));
    }

    public byte HairStyle
    {
        get => _model.hairStyle;
        set => SetByte(nameof(HairStyle), _model.hairStyle, value, v => _model.hairStyle = v, () => Raise(nameof(HairStyle)));
    }

    public byte ClothingType
    {
        get => _model.clothingType;
        set => SetByte(nameof(ClothingType), _model.clothingType, value, v => _model.clothingType = v, () => Raise(nameof(ClothingType)));
    }

    public byte Voice
    {
        get => _model.voice;
        set => SetByte(nameof(Voice), _model.voice, value, v => _model.voice = v, () => Raise(nameof(Voice)));
    }

    public byte EyeColor
    {
        get => _model.eyeColor;
        set => SetByte(nameof(EyeColor), _model.eyeColor, value, v => _model.eyeColor = v, () => Raise(nameof(EyeColor)));
    }

    public byte FeaturesType
    {
        get => _model.featuresType;
        set => SetByte(nameof(FeaturesType), _model.featuresType, value, v => _model.featuresType = v, () => Raise(nameof(FeaturesType)));
    }

    // Raw RGB properties are retained internally for save compatibility and change preview.
    public byte FeatureColorR { get => _model.featuresColor[0]; set => SetArrayByte(nameof(FeatureColorR), _model.featuresColor, 0, value, "Feature"); }
    public byte FeatureColorG { get => _model.featuresColor[1]; set => SetArrayByte(nameof(FeatureColorG), _model.featuresColor, 1, value, "Feature"); }
    public byte FeatureColorB { get => _model.featuresColor[2]; set => SetArrayByte(nameof(FeatureColorB), _model.featuresColor, 2, value, "Feature"); }

    public byte HairColorR { get => _model.hairColor[0]; set => SetArrayByte(nameof(HairColorR), _model.hairColor, 0, value, "Hair"); }
    public byte HairColorG { get => _model.hairColor[1]; set => SetArrayByte(nameof(HairColorG), _model.hairColor, 1, value, "Hair"); }
    public byte HairColorB { get => _model.hairColor[2]; set => SetArrayByte(nameof(HairColorB), _model.hairColor, 2, value, "Hair"); }

    public byte ClothingColorR { get => _model.clothingColor[0]; set => SetArrayByte(nameof(ClothingColorR), _model.clothingColor, 0, value, "Clothing"); }
    public byte ClothingColorG { get => _model.clothingColor[1]; set => SetArrayByte(nameof(ClothingColorG), _model.clothingColor, 1, value, "Clothing"); }
    public byte ClothingColorB { get => _model.clothingColor[2]; set => SetArrayByte(nameof(ClothingColorB), _model.clothingColor, 2, value, "Clothing"); }

    public byte SkinColorR { get => _model.skinColor[0]; set => SetArrayByte(nameof(SkinColorR), _model.skinColor, 0, value, "Skin"); }
    public byte SkinColorG { get => _model.skinColor[1]; set => SetArrayByte(nameof(SkinColorG), _model.skinColor, 1, value, "Skin"); }
    public byte SkinColorB { get => _model.skinColor[2]; set => SetArrayByte(nameof(SkinColorB), _model.skinColor, 2, value, "Skin"); }

    public Brush FeatureColorBrush => MakeBrush(_model.featuresColor);
    public Brush HairColorBrush => MakeBrush(_model.hairColor);
    public Brush ClothingColorBrush => MakeBrush(_model.clothingColor);
    public Brush SkinColorBrush => MakeBrush(_model.skinColor);

    public string FeatureColorHex => MakeHex(_model.featuresColor);
    public string HairColorHex => MakeHex(_model.hairColor);
    public string ClothingColorHex => MakeHex(_model.clothingColor);
    public string SkinColorHex => MakeHex(_model.skinColor);

    public Color GetAppearanceColor(string key)
    {
        var rgb = GetColorArray(key, out _);
        return Color.FromRgb(rgb[0], rgb[1], rgb[2]);
    }

    public void SetAppearanceColor(string key, Color color)
    {
        var rgb = GetColorArray(key, out var prefix);
        var old = rgb.ToArray();
        var next = new[] { color.R, color.G, color.B };
        if (old.SequenceEqual(next)) return;

        Array.Copy(next, rgb, 3);
        RefreshColor(prefix);
        _dirty.MarkDirty();
        _undo.Record(
            () => { Array.Copy(old, rgb, 3); RefreshColor(prefix); },
            () => { Array.Copy(next, rgb, 3); RefreshColor(prefix); },
            $"{prefix.ToLowerInvariant()} color");
    }

    private byte[] GetColorArray(string key, out string prefix)
    {
        switch (key)
        {
            case "Feature":
            case "Features":
                prefix = "Feature";
                return _model.featuresColor;
            case "Hair":
                prefix = "Hair";
                return _model.hairColor;
            case "Clothing":
                prefix = "Clothing";
                return _model.clothingColor;
            case "Skin":
                prefix = "Skin";
                return _model.skinColor;
            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown appearance color field.");
        }
    }

    public double FeatureHue { get => RgbToHsv(_model.featuresColor).H; set => SetHsv("Feature", _model.featuresColor, h: value); }
    public double FeatureSaturation { get => RgbToHsv(_model.featuresColor).S; set => SetHsv("Feature", _model.featuresColor, s: value); }
    public double FeatureBrightness { get => RgbToHsv(_model.featuresColor).V; set => SetHsv("Feature", _model.featuresColor, v: value); }

    public double HairHue { get => RgbToHsv(_model.hairColor).H; set => SetHsv("Hair", _model.hairColor, h: value); }
    public double HairSaturation { get => RgbToHsv(_model.hairColor).S; set => SetHsv("Hair", _model.hairColor, s: value); }
    public double HairBrightness { get => RgbToHsv(_model.hairColor).V; set => SetHsv("Hair", _model.hairColor, v: value); }

    public double ClothingHue { get => RgbToHsv(_model.clothingColor).H; set => SetHsv("Clothing", _model.clothingColor, h: value); }
    public double ClothingSaturation { get => RgbToHsv(_model.clothingColor).S; set => SetHsv("Clothing", _model.clothingColor, s: value); }
    public double ClothingBrightness { get => RgbToHsv(_model.clothingColor).V; set => SetHsv("Clothing", _model.clothingColor, v: value); }

    public double SkinHue { get => RgbToHsv(_model.skinColor).H; set => SetHsv("Skin", _model.skinColor, h: value); }
    public double SkinSaturation { get => RgbToHsv(_model.skinColor).S; set => SetHsv("Skin", _model.skinColor, s: value); }
    public double SkinBrightness { get => RgbToHsv(_model.skinColor).V; set => SetHsv("Skin", _model.skinColor, v: value); }

    public uint HunterRank
    {
        get => _model.HunterRank;
        set => SetValue(nameof(HunterRank), _model.HunterRank, value, v => _model.HunterRank = v, () => Raise(nameof(HunterRank)));
    }

    public uint HunterRankPoints
    {
        get => _model.HunterRankPoints;
        set => SetValue(nameof(HunterRankPoints), _model.HunterRankPoints, value, v => _model.HunterRankPoints = v, () => Raise(nameof(HunterRankPoints)));
    }

    public uint Funds
    {
        get => _model.Funds;
        set => SetValue(nameof(Funds), _model.Funds, value, v => _model.Funds = v, () => Raise(nameof(Funds)));
    }

    private static IReadOnlyList<CharacterChoice> BuildChoices(int count, string label)
        => Enumerable.Range(0, count)
            .Select(i => new CharacterChoice((byte)i, $"{label} {i + 1}"))
            .ToArray();

    private void SetByte(string description, byte oldValue, byte newValue, Action<byte> setter, Action refresh)
        => SetValue(description, oldValue, newValue, setter, refresh);

    private void SetArrayByte(string propertyName, byte[] array, int index, byte newValue, string colorPrefix)
    {
        var oldValue = array[index];
        if (oldValue == newValue) return;
        array[index] = newValue;
        RefreshColor(colorPrefix);
        _dirty.MarkDirty();
        _undo.Record(
            () => { array[index] = oldValue; RefreshColor(colorPrefix); },
            () => { array[index] = newValue; RefreshColor(colorPrefix); },
            propertyName);
    }

    private void SetHsv(string prefix, byte[] rgb, double? h = null, double? s = null, double? v = null)
    {
        var old = rgb.ToArray();
        var hsv = RgbToHsv(rgb);
        var nextH = Math.Clamp(h ?? hsv.H, 0, 360);
        var nextS = Math.Clamp(s ?? hsv.S, 0, 1);
        var nextV = Math.Clamp(v ?? hsv.V, 0, 1);
        var next = HsvToRgb(nextH, nextS, nextV);
        if (old.SequenceEqual(next)) return;

        Array.Copy(next, rgb, 3);
        RefreshColor(prefix);
        _dirty.MarkDirty();
        _undo.Record(
            () => { Array.Copy(old, rgb, 3); RefreshColor(prefix); },
            () => { Array.Copy(next, rgb, 3); RefreshColor(prefix); },
            $"{prefix.ToLowerInvariant()} color");
    }

    private void RefreshColor(string prefix)
    {
        Raise(prefix + "ColorR");
        Raise(prefix + "ColorG");
        Raise(prefix + "ColorB");
        Raise(prefix + "ColorBrush");
        Raise(prefix + "ColorHex");
        Raise(prefix + "Hue");
        Raise(prefix + "Saturation");
        Raise(prefix + "Brightness");
    }

    private static Brush MakeBrush(byte[] rgb)
    {
        var brush = new SolidColorBrush(Color.FromRgb(rgb[0], rgb[1], rgb[2]));
        brush.Freeze();
        return brush;
    }

    private static string MakeHex(byte[] rgb) => $"#{rgb[0]:X2}{rgb[1]:X2}{rgb[2]:X2}";

    private static (double H, double S, double V) RgbToHsv(byte[] rgb)
    {
        var r = rgb[0] / 255.0;
        var g = rgb[1] / 255.0;
        var b = rgb[2] / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double h;
        if (delta < 0.000001) h = 0;
        else if (Math.Abs(max - r) < 0.000001) h = 60 * (((g - b) / delta) % 6);
        else if (Math.Abs(max - g) < 0.000001) h = 60 * (((b - r) / delta) + 2);
        else h = 60 * (((r - g) / delta) + 4);
        if (h < 0) h += 360;

        var s = max <= 0 ? 0 : delta / max;
        return (h, s, max);
    }

    private static byte[] HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60.0 % 2) - 1));
        var m = v - c;

        double r1, g1, b1;
        if (h < 60) (r1, g1, b1) = (c, x, 0);
        else if (h < 120) (r1, g1, b1) = (x, c, 0);
        else if (h < 180) (r1, g1, b1) = (0, c, x);
        else if (h < 240) (r1, g1, b1) = (0, x, c);
        else if (h < 300) (r1, g1, b1) = (x, 0, c);
        else (r1, g1, b1) = (c, 0, x);

        return new[]
        {
            (byte)Math.Clamp((int)Math.Round((r1 + m) * 255), 0, 255),
            (byte)Math.Clamp((int)Math.Round((g1 + m) * 255), 0, 255),
            (byte)Math.Clamp((int)Math.Round((b1 + m) * 255), 0, 255)
        };
    }

    private void SetValue<T>(string description, T oldValue, T newValue, Action<T> setter, Action refresh)
        where T : IEquatable<T>
    {
        if (oldValue.Equals(newValue)) return;
        setter(newValue);
        refresh();
        _dirty.MarkDirty();
        _undo.Record(
            () => { setter(oldValue); refresh(); },
            () => { setter(newValue); refresh(); },
            description);
    }
}
