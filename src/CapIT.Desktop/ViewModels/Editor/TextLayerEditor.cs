using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorderApp.Models;

namespace ScreenRecorderApp.ViewModels;

public sealed record TextAnimationOption(string Key, string Label)
{
    public override string ToString() => Label;

    public static readonly IReadOnlyList<TextAnimationOption> All =
    [
        new("None", "None"),
        new("BounceLetters", "Bounce letters"),
        new("Fade", "Fade in"),
        new("Pop", "Pop"),
        new("SlideUp", "Slide up"),
    ];
}

/// <summary>
/// Observable editor for the selected text layer. <see cref="PresentationTextOverlay"/> is a plain
/// serialisable model, so this mirrors its fields and writes every change back through
/// <see cref="ReviewViewModel.UpdateTextOverlay"/> (which normalises, records history and saves).
/// </summary>
public sealed partial class TextLayerEditor : ObservableObject
{
    private static IReadOnlyList<string>? _systemFonts;
    private readonly ReviewViewModel _document;
    private bool _syncing;

    public TextLayerEditor(ReviewViewModel document)
    {
        _document = document;
        _document.CompositionChanged += (_, _) => { if (!_syncing) Refresh(); };
        _ = LoadFontsAsync();
    }

    /// <summary>Raised when the user picks another layer, so the preview's drag target follows it.</summary>
    public event Action<int>? LayerSelected;

    public ObservableCollection<string> Layers { get; } = [];
    public ObservableCollection<string> Fonts { get; } = [];
    public IReadOnlyList<TextAnimationOption> Animations { get; } = TextAnimationOption.All;
    public IReadOnlyList<string> HorizontalAlignments { get; } = ["Left", "Center", "Right"];
    public IReadOnlyList<string> VerticalAlignments { get; } = ["Top", "Center", "Bottom"];

    [ObservableProperty] private int _selectedIndex;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _fontFamily = "Segoe UI";
    [ObservableProperty] private double _fontSize = 48;
    [ObservableProperty] private string _color = "#FFFFFF";
    [ObservableProperty] private double _opacity = 1;
    [ObservableProperty] private double _xPercent = 50;
    [ObservableProperty] private double _yPercent = 50;
    [ObservableProperty] private string _horizontalAlignment = "Center";
    [ObservableProperty] private string _verticalAlignment = "Center";
    [ObservableProperty] private bool _bold;
    [ObservableProperty] private bool _italic;
    [ObservableProperty] private TextAnimationOption _animation = TextAnimationOption.All[0];
    [ObservableProperty] private double _animationDuration = .8;

    public bool HasMultipleLayers => Layers.Count > 1;

    /// <summary>Reloads every field from the document (after undo, preset, drag or layer change).</summary>
    public void Refresh()
    {
        _syncing = true;
        try
        {
            var texts = _document.Presentation.TextOverlays.Count > 0
                ? _document.Presentation.TextOverlays
                : [_document.Presentation.TextOverlay];
            if (Layers.Count != texts.Count)
            {
                Layers.Clear();
                for (var i = 0; i < texts.Count; i++) Layers.Add($"Text {i + 1}");
                OnPropertyChanged(nameof(HasMultipleLayers));
            }
            SelectedIndex = Math.Clamp(_document.SelectedTextOverlayIndex, 0, Math.Max(0, Layers.Count - 1));
            // Lists bound before their items existed drop the selection; re-announce it.
            OnPropertyChanged(nameof(SelectedIndex));
            var t = _document.SelectedTextOverlay;
            Text = t.Text;
            FontFamily = t.FontFamily;
            FontSize = t.FontSize;
            Color = t.Color;
            Opacity = t.Opacity;
            XPercent = Math.Round(t.X * 100, 1);
            YPercent = Math.Round(t.Y * 100, 1);
            HorizontalAlignment = t.HorizontalAlignment;
            VerticalAlignment = t.VerticalAlignment;
            Bold = t.Bold;
            Italic = t.Italic;
            Animation = Animations.FirstOrDefault(a => a.Key == t.Animation) ?? Animations[0];
            AnimationDuration = t.AnimationDuration;
            if (!Fonts.Contains(FontFamily)) Fonts.Insert(0, FontFamily);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void Update(Action<PresentationTextOverlay> change)
    {
        if (_syncing || !_document.IsReady) return;
        _syncing = true;
        try { _document.UpdateTextOverlay(change); }
        finally { _syncing = false; }
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (_syncing || value < 0) return;
        _document.SelectedTextOverlayIndex = value;
        Refresh();
        LayerSelected?.Invoke(value);
    }

    partial void OnTextChanged(string value) => Update(t => t.Text = value ?? "");
    partial void OnFontFamilyChanged(string value) { if (!string.IsNullOrWhiteSpace(value)) Update(t => t.FontFamily = value); }
    partial void OnFontSizeChanged(double value) => Update(t => t.FontSize = value);
    partial void OnColorChanged(string value) => Update(t => t.Color = value);
    partial void OnOpacityChanged(double value) => Update(t => t.Opacity = value);
    partial void OnXPercentChanged(double value) => Update(t => t.X = value / 100);
    partial void OnYPercentChanged(double value) => Update(t => t.Y = value / 100);
    partial void OnHorizontalAlignmentChanged(string value) => Update(t => t.HorizontalAlignment = value);
    partial void OnVerticalAlignmentChanged(string value) => Update(t => t.VerticalAlignment = value);
    partial void OnBoldChanged(bool value) => Update(t => t.Bold = value);
    partial void OnItalicChanged(bool value) => Update(t => t.Italic = value);
    partial void OnAnimationChanged(TextAnimationOption value) { if (value is not null) Update(t => t.Animation = value.Key); }
    partial void OnAnimationDurationChanged(double value) => Update(t => t.AnimationDuration = value);

    [RelayCommand]
    private void AddLayer()
    {
        _document.AddTextOverlay();
        Refresh();
        LayerSelected?.Invoke(_document.SelectedTextOverlayIndex);
    }

    [RelayCommand]
    private void RemoveLayer()
    {
        _document.RemoveSelectedTextOverlay();
        Refresh();
        LayerSelected?.Invoke(_document.SelectedTextOverlayIndex);
    }

    [RelayCommand]
    private void ClearText()
    {
        Update(t => t.Text = "");
        Refresh();
    }

    /// <summary>Installed GDI+ font families — the same set the exporter can draw with.</summary>
    private async Task LoadFontsAsync()
    {
        _systemFonts ??= await Task.Run(() =>
        {
            try { return (IReadOnlyList<string>)System.Drawing.FontFamily.Families.Select(f => f.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(); }
            catch { return ["Segoe UI"]; }
        });
        // Repopulating clears the bound selection; restore it without writing to the document.
        var current = FontFamily;
        _syncing = true;
        try
        {
            Fonts.Clear();
            foreach (var font in _systemFonts) Fonts.Add(font);
            if (!string.IsNullOrWhiteSpace(current) && !Fonts.Contains(current)) Fonts.Insert(0, current);
            FontFamily = current;
            OnPropertyChanged(nameof(FontFamily));
        }
        finally
        {
            _syncing = false;
        }
    }
}
