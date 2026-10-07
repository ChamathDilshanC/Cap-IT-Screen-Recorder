using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using ScreenRecorderApp.Services;

namespace ScreenRecorderApp.Controls;

/// <summary>
/// Hosts in-window modal dialogs over its <see cref="ContentControl.Content"/>: dims the window, centres
/// the dialog card, traps keyboard focus inside it, and treats Escape as "cancel". Requests queue, so two
/// callers never stack dialogs on top of each other.
/// </summary>
public sealed class DialogHost : ContentControl
{
    public static readonly DirectProperty<DialogHost, DialogRequest?> CurrentRequestProperty =
        AvaloniaProperty.RegisterDirect<DialogHost, DialogRequest?>(nameof(CurrentRequest), o => o.CurrentRequest);

    public static readonly DirectProperty<DialogHost, bool> IsOpenProperty =
        AvaloniaProperty.RegisterDirect<DialogHost, bool>(nameof(IsOpen), o => o.IsOpen);

    private readonly SemaphoreSlim _queue = new(1, 1);
    private DialogRequest? _current;
    private bool _isOpen;
    private IInputElement? _previousFocus;
    private Button? _primary;

    public DialogRequest? CurrentRequest { get => _current; private set => SetAndRaise(CurrentRequestProperty, ref _current, value); }
    public bool IsOpen { get => _isOpen; private set => SetAndRaise(IsOpenProperty, ref _isOpen, value); }

    public async Task<DialogResult> ShowAsync(DialogRequest request)
    {
        await _queue.WaitAsync();
        try
        {
            _previousFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            CurrentRequest = request;
            IsOpen = true;
            Dispatcher.UIThread.Post(FocusPrimary, DispatcherPriority.Loaded);
            return await request.Completion;
        }
        finally
        {
            IsOpen = false;
            CurrentRequest = null;
            _previousFocus?.Focus();
            _queue.Release();
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _primary = e.NameScope.Find<Button>("PART_Primary");
    }

    private void FocusPrimary() => _primary?.Focus(NavigationMethod.Tab);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (IsOpen && e.Key == Key.Escape && CurrentRequest is { } request)
        {
            request.Cancel();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}
