using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Flux.Native.ViewModels.Base;

namespace Flux.Native.ViewModels.Settings.Metadata;

public abstract class MetadataFieldViewModel : ViewModelBase
{
    private readonly Action? requestRefresh;
    private readonly Func<bool>? visibleWhen;

    protected MetadataFieldViewModel(string label, string? description = null, Action? requestRefresh = null, Func<bool>? visibleWhen = null)
    {
        Label = label;
        Description = description ?? string.Empty;
        this.requestRefresh = requestRefresh;
        this.visibleWhen = visibleWhen;
    }

    public string Label { get; }
    public string Description { get; }
    public Visibility Visibility => visibleWhen?.Invoke() == false ? Visibility.Collapsed : Visibility.Visible;

    public virtual void Refresh()
    {
        OnPropertyChanged(nameof(Visibility));
    }

    protected void NotifyValueChanged(string propertyName)
    {
        OnPropertyChanged(propertyName);
        requestRefresh?.Invoke();
    }
}

public sealed class MetadataBooleanFieldViewModel : MetadataFieldViewModel
{
    private readonly Func<bool> getter;
    private readonly Action<bool> setter;
    private readonly Action? afterChange;

    public MetadataBooleanFieldViewModel(
        string label,
        Func<bool> getter,
        Action<bool> setter,
        string? description = null,
        Action? requestRefresh = null,
        Func<bool>? visibleWhen = null,
        Action? afterChange = null) : base(label, description, requestRefresh, visibleWhen)
    {
        this.getter = getter;
        this.setter = setter;
        this.afterChange = afterChange;
    }

    public bool Value
    {
        get => getter();
        set
        {
            if (getter() == value)
            {
                return;
            }

            setter(value);
            afterChange?.Invoke();
            NotifyValueChanged(nameof(Value));
        }
    }

    public override void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(Value));
    }
}

public class MetadataTextFieldViewModel : MetadataFieldViewModel
{
    private readonly Func<string> getter;
    private readonly Action<string> setter;
    private readonly Action? afterChange;

    public MetadataTextFieldViewModel(
        string label,
        Func<string> getter,
        Action<string> setter,
        string? description = null,
        Action? requestRefresh = null,
        Func<bool>? visibleWhen = null,
        Action? afterChange = null) : base(label, description, requestRefresh, visibleWhen)
    {
        this.getter = getter;
        this.setter = setter;
        this.afterChange = afterChange;
    }

    public virtual string Value
    {
        get => getter() ?? string.Empty;
        set
        {
            if (string.Equals(getter() ?? string.Empty, value ?? string.Empty, StringComparison.Ordinal))
            {
                return;
            }

            setter(value ?? string.Empty);
            afterChange?.Invoke();
            NotifyValueChanged(nameof(Value));
        }
    }

    public override void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(Value));
    }
}

public sealed class MetadataMultilineTextFieldViewModel : MetadataTextFieldViewModel
{
    public MetadataMultilineTextFieldViewModel(
        string label,
        Func<string> getter,
        Action<string> setter,
        string? description = null,
        Action? requestRefresh = null,
        Func<bool>? visibleWhen = null,
        Action? afterChange = null) : base(label, getter, setter, description, requestRefresh, visibleWhen, afterChange)
    {
    }
}

public sealed class MetadataIntegerFieldViewModel : MetadataFieldViewModel
{
    private readonly Func<int> getter;
    private readonly Action<int> setter;
    private readonly Action? afterChange;

    public MetadataIntegerFieldViewModel(
        string label,
        Func<int> getter,
        Action<int> setter,
        int minimum = 0,
        int maximum = int.MaxValue,
        int interval = 1,
        string? description = null,
        Action? requestRefresh = null,
        Func<bool>? visibleWhen = null,
        Action? afterChange = null) : base(label, description, requestRefresh, visibleWhen)
    {
        this.getter = getter;
        this.setter = setter;
        this.afterChange = afterChange;
        Minimum = minimum;
        Maximum = maximum;
        Interval = interval;
    }

    public int Minimum { get; }
    public int Maximum { get; }
    public int Interval { get; }

    public int Value
    {
        get => getter();
        set
        {
            if (getter() == value)
            {
                return;
            }

            setter(value);
            afterChange?.Invoke();
            NotifyValueChanged(nameof(Value));
        }
    }

    public override void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(Value));
    }
}

public sealed class MetadataEnumFieldViewModel : MetadataFieldViewModel
{
    private readonly Func<object> getter;
    private readonly Action<object> setter;
    private readonly Action? afterChange;

    public MetadataEnumFieldViewModel(
        string label,
        Func<object> getter,
        Action<object> setter,
        IEnumerable options,
        string? description = null,
        Action? requestRefresh = null,
        Func<bool>? visibleWhen = null,
        Action? afterChange = null) : base(label, description, requestRefresh, visibleWhen)
    {
        this.getter = getter;
        this.setter = setter;
        this.afterChange = afterChange;
        Options = options.Cast<object>().ToList();
    }

    public IReadOnlyList<object> Options { get; }

    public object Value
    {
        get => getter();
        set
        {
            if (Equals(getter(), value))
            {
                return;
            }

            setter(value);
            afterChange?.Invoke();
            NotifyValueChanged(nameof(Value));
        }
    }

    public override void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(Value));
    }
}

public sealed class MetadataMessageFieldViewModel : MetadataFieldViewModel
{
    public MetadataMessageFieldViewModel(
        string message,
        Brush? foreground = null,
        Action? requestRefresh = null,
        Func<bool>? visibleWhen = null) : base(string.Empty, null, requestRefresh, visibleWhen)
    {
        Message = message;
        Foreground = foreground ?? Brushes.Goldenrod;
    }

    public string Message { get; }
    public Brush Foreground { get; }
}
