using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using MH4UModern.Services;

namespace MH4UModern.ViewModels;

public sealed class EditablePropertyViewModel : BindableBase
{
    private readonly object _target;
    private readonly PropertyInfo _property;
    private readonly UndoManager _undo;
    private readonly DirtyTracker _dirty;
    private readonly Action? _afterChange;
    private string? _error;

    public EditablePropertyViewModel(
        object target,
        PropertyInfo property,
        string label,
        UndoManager undo,
        DirtyTracker dirty,
        Action? afterChange = null,
        string? help = null)
    {
        _target = target;
        _property = property;
        Label = label;
        _undo = undo;
        _dirty = dirty;
        _afterChange = afterChange;
        Help = help;
    }

    public string Label { get; }
    public string? Help { get; }
    public bool IsBoolean => _property.PropertyType == typeof(bool);
    public bool IsText => !IsBoolean;
    public string? Error { get => _error; private set => Set(ref _error, value); }

    public bool BoolValue
    {
        get => GetValue() is bool value && value;
        set
        {
            if (!IsBoolean) return;
            SetValue(value, value ? "On" : "Off");
        }
    }

    public string ValueText
    {
        get
        {
            var value = GetValue();
            return value switch
            {
                null => string.Empty,
                string text => SafeDisplayString(text),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
        }
        set
        {
            if (IsBoolean) return;
            if (!TryConvert(value, _property.PropertyType, out var converted, out var error))
            {
                Error = error;
                return;
            }

            if (_property.PropertyType == typeof(string))
            {
                var textValue = converted as string ?? string.Empty;
                if (_target is mh4edit.GuildQuest && _property.Name == "Owner")
                    converted = (textValue.Length > 12 ? textValue[..12] : textValue).PadRight(12, '\0');
                else if (_target is mh4edit.Palico && _property.Name == "Name" && textValue.Length > 12)
                    converted = textValue[..12];
            }

            Error = null;
            SetValue(converted, value);
        }
    }

    public void Refresh()
    {
        Raise(nameof(BoolValue));
        Raise(nameof(ValueText));
        Raise(nameof(Error));
    }

    private object? GetValue()
    {
        try { return _property.GetValue(_target); }
        catch { return null; }
    }

    private void SetValue(object? newValue, string descriptionValue)
    {
        var oldValue = GetValue();
        if (Equals(oldValue, newValue)) return;

        try
        {
            _property.SetValue(_target, newValue);
            _dirty.MarkDirty();
            Refresh();
            _afterChange?.Invoke();

            _undo.Record(
                () => { _property.SetValue(_target, oldValue); Refresh(); _afterChange?.Invoke(); },
                () => { _property.SetValue(_target, newValue); Refresh(); _afterChange?.Invoke(); },
                $"{Label} ({descriptionValue})");
        }
        catch (Exception ex)
        {
            Error = ex.GetBaseException().Message;
        }
    }

    private static string SafeDisplayString(string value)
    {
        value = value.TrimEnd('\0');
        if (value.Length == 0) return value;
        var chars = new List<char>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    chars.Add(ch);
                    chars.Add(value[++i]);
                }
                continue;
            }
            if (char.IsLowSurrogate(ch)) continue;
            if (char.IsControl(ch) && !char.IsWhiteSpace(ch)) continue;
            chars.Add(ch);
        }
        return new string(chars.ToArray());
    }

    private static bool TryConvert(string text, Type type, out object? value, out string? error)
    {
        error = null;
        value = null;
        try
        {
            if (type == typeof(string))
            {
                value = text;
                return true;
            }

            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (underlying.IsEnum)
            {
                value = Enum.Parse(underlying, text, ignoreCase: true);
                return true;
            }

            var converter = TypeDescriptor.GetConverter(underlying);
            if (converter.CanConvertFrom(typeof(string)))
            {
                value = converter.ConvertFromInvariantString(text);
                return true;
            }

            value = Convert.ChangeType(text, underlying, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            error = $"Invalid value for {FriendlyTypeName(type)}.";
            return false;
        }
    }

    private static string FriendlyTypeName(Type type)
    {
        if (type == typeof(byte)) return "0–255";
        if (type == typeof(ushort)) return "0–65,535";
        if (type == typeof(uint)) return "0–4,294,967,295";
        if (type == typeof(ulong)) return "a positive whole number";
        if (type == typeof(int)) return "a whole number";
        return type.Name;
    }
}
