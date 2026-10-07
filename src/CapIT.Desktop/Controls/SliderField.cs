using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace ScreenRecorderApp.Controls;

/// <summary>
/// A slider with its formatted value in a fixed-width, right-aligned column, so every slider row in the
/// app lines its numbers up. <see cref="Format"/> is a composite format string such as "{0:0}%".
/// </summary>
/// <remarks>
/// The inner <see cref="Slider"/> is driven from code rather than a two-way template binding: a range
/// control coerces its value against whatever minimum/maximum it has at that instant, and template
/// bindings don't guarantee the range arrives first. A two-way binding would then write the coerced
/// value (e.g. a 12 Mbps bitrate clamped to the default maximum of 100) straight back into the setting.
/// Here the range is always applied before the value, and only user changes flow back.
/// </remarks>
public sealed class SliderField : TemplatedControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SliderField, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<SliderField, double>(nameof(Minimum));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<SliderField, double>(nameof(Maximum), 100);

    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<SliderField, double>(nameof(Step), 1);

    public static readonly StyledProperty<string> FormatProperty =
        AvaloniaProperty.Register<SliderField, string>(nameof(Format), "{0:0}");

    public static readonly StyledProperty<double> ValueScaleProperty =
        AvaloniaProperty.Register<SliderField, double>(nameof(ValueScale), 1);

    public static readonly DirectProperty<SliderField, string> DisplayTextProperty =
        AvaloniaProperty.RegisterDirect<SliderField, string>(nameof(DisplayText), o => o.DisplayText);

    private string _displayText = "";
    private Slider? _slider;
    private bool _syncing;

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public string Format { get => GetValue(FormatProperty); set => SetValue(FormatProperty, value); }

    /// <summary>Multiplier applied before formatting (e.g. 100 to show a 0..1 value as a percentage).</summary>
    public double ValueScale { get => GetValue(ValueScaleProperty); set => SetValue(ValueScaleProperty, value); }

    public string DisplayText
    {
        get => _displayText;
        private set => SetAndRaise(DisplayTextProperty, ref _displayText, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_slider is not null) _slider.PropertyChanged -= OnSliderPropertyChanged;
        _slider = e.NameScope.Find<Slider>("PART_Slider");
        if (_slider is null) return;
        PushToSlider();
        _slider.PropertyChanged += OnSliderPropertyChanged;
    }

    private void PushToSlider()
    {
        if (_slider is null) return;
        _syncing = true;
        try
        {
            // Range first, then value — see remarks.
            _slider.Maximum = Math.Max(Minimum, Maximum);
            _slider.Minimum = Minimum;
            _slider.SmallChange = Step;
            _slider.LargeChange = Step;
            _slider.TickFrequency = Step;
            _slider.Value = Math.Clamp(Value, Minimum, Math.Max(Minimum, Maximum));
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnSliderPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_syncing || e.Property != RangeBase.ValueProperty || _slider is null) return;
        // Only genuine user input reaches here; write it back to the bound setting.
        if (Math.Abs(_slider.Value - Value) > double.Epsilon) SetCurrentValue(ValueProperty, _slider.Value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == MinimumProperty || change.Property == MaximumProperty || change.Property == StepProperty)
            PushToSlider();
        if (change.Property == ValueProperty || change.Property == FormatProperty || change.Property == ValueScaleProperty)
            UpdateDisplayText();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        UpdateDisplayText();
    }

    private void UpdateDisplayText()
    {
        try { DisplayText = string.Format(CultureInfo.CurrentCulture, Format, Value * ValueScale); }
        catch (FormatException) { DisplayText = Value.ToString("0.##", CultureInfo.CurrentCulture); }
    }
}
