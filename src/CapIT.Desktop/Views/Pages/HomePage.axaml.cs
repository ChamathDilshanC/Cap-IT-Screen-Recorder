using Avalonia;
using Avalonia.Controls;
using ScreenRecorderApp.ViewModels;

namespace ScreenRecorderApp.Views.Pages;

public partial class HomePage : UserControl
{
    private MainViewModel? _previewOwner;

    public HomePage() => InitializeComponent();

    // The live preview only pulls frames while a page showing it is on screen.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ShellViewModel shell)
        {
            _previewOwner = shell.Main;
            _previewOwner.AttachPreviewConsumer();
            _ = shell.Recordings.RefreshAsync();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _previewOwner?.DetachPreviewConsumer();
        _previewOwner = null;
    }
}
