using Avalonia;
using Avalonia.Controls;
using ScreenRecorderApp.ViewModels;

namespace ScreenRecorderApp.Views.Components;

/// <summary>Live capture preview for side panels. Pulls frames only while it is on screen.</summary>
public partial class LivePreviewCard : UserControl
{
    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<LivePreviewCard, string?>(nameof(Hint));

    private MainViewModel? _owner;

    public LivePreviewCard() => InitializeComponent();

    /// <summary>Optional one-line explanation under the preview (e.g. "Move the mouse to see the zoom").</summary>
    public string? Hint { get => GetValue(HintProperty); set => SetValue(HintProperty, value); }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Register();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VisualRoot is not null) Register();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _owner?.DetachPreviewConsumer();
        _owner = null;
    }

    private void Register()
    {
        if (ReferenceEquals(_owner, DataContext)) return;
        _owner?.DetachPreviewConsumer();
        _owner = DataContext as MainViewModel;
        _owner?.AttachPreviewConsumer();
    }
}
