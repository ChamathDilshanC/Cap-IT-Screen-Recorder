using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ScreenRecorderApp.Services;

public enum ToastKind { Info, Success, Warning, Error }

public sealed partial class ToastItem : ObservableObject
{
    public required ToastKind Kind { get; init; }
    public required string Title { get; init; }
    public string? Message { get; init; }
    public string? ActionText { get; init; }
    public Action? Action { get; init; }
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool HasAction => ActionText is not null && Action is not null;
    public bool IsSuccess => Kind == ToastKind.Success;
    public bool IsWarning => Kind == ToastKind.Warning;
    public bool IsError => Kind == ToastKind.Error;
    public bool IsInfo => Kind == ToastKind.Info;
}

/// <summary>
/// Non-blocking notifications ("Recording saved", "Export complete"…). Routine outcomes use toasts,
/// never modal dialogs. Safe to call from any thread.
/// </summary>
public sealed partial class ToastService
{
    private const int MaxVisible = 4;

    public ObservableCollection<ToastItem> Items { get; } = [];

    public void Show(ToastKind kind, string title, string? message = null, string? actionText = null, Action? action = null, int durationMs = 4500)
    {
        var item = new ToastItem { Kind = kind, Title = title, Message = message, ActionText = actionText, Action = action };
        Dispatcher.UIThread.Post(() =>
        {
            Items.Add(item);
            while (Items.Count > MaxVisible) Items.RemoveAt(0);
            DispatcherTimer.RunOnce(() => Items.Remove(item), TimeSpan.FromMilliseconds(kind == ToastKind.Error ? durationMs * 2 : durationMs));
        });
    }

    public void Success(string title, string? message = null, string? actionText = null, Action? action = null) =>
        Show(ToastKind.Success, title, message, actionText, action);

    public void Info(string title, string? message = null) => Show(ToastKind.Info, title, message);
    public void Warning(string title, string? message = null) => Show(ToastKind.Warning, title, message);
    public void Error(string title, string? message = null) => Show(ToastKind.Error, title, message);

    [RelayCommand]
    private void Dismiss(ToastItem item) => Items.Remove(item);

    [RelayCommand]
    private void Invoke(ToastItem item)
    {
        Items.Remove(item);
        item.Action?.Invoke();
    }
}
