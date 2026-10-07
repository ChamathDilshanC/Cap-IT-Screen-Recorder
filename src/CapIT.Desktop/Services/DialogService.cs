using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorderApp.Controls;

namespace ScreenRecorderApp.Services;

public enum DialogTone { Default, Warning, Danger, Error }

public enum DialogResult { None, Primary, Secondary }

/// <summary>A pending confirmation/notice shown by a <see cref="DialogHost"/>.</summary>
public sealed partial class DialogRequest : ObservableObject
{
    private readonly TaskCompletionSource<DialogResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public required string Title { get; init; }
    public string? Message { get; init; }
    public string PrimaryText { get; init; } = "OK";
    public string? SecondaryText { get; init; } = "Cancel";
    public DialogTone Tone { get; init; }
    public Geometry? Icon { get; init; }

    /// <summary>Technical details (exception text…) behind an expander, so the main message stays human.</summary>
    public string? Details { get; init; }

    [ObservableProperty] private bool _showDetails;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool HasSecondary => SecondaryText is not null;
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
    public bool IsDanger => Tone == DialogTone.Danger;
    public bool IsNotDanger => Tone != DialogTone.Danger;
    public bool IsWarning => Tone == DialogTone.Warning;
    public bool IsError => Tone is DialogTone.Error or DialogTone.Danger;

    public Task<DialogResult> Completion => _completion.Task;

    [RelayCommand] private void Primary() => _completion.TrySetResult(DialogResult.Primary);
    [RelayCommand] private void Secondary() => _completion.TrySetResult(DialogResult.Secondary);
    [RelayCommand] private void ToggleDetails() => ShowDetails = !ShowDetails;
    public void Cancel() => _completion.TrySetResult(DialogResult.None);
}

/// <summary>Shows dialogs inside one window's <see cref="DialogHost"/>. Each window that hosts dialogs gets its own instance.</summary>
public sealed class DialogService(DialogHost host)
{
    public Task<DialogResult> ShowAsync(DialogRequest request) => host.ShowAsync(request);

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, DialogTone tone = DialogTone.Default, Geometry? icon = null)
        => await ShowAsync(new DialogRequest { Title = title, Message = message, PrimaryText = confirmText, Tone = tone, Icon = icon }) == DialogResult.Primary;

    public Task ErrorAsync(string title, string message, string? details = null, Geometry? icon = null)
        => ShowAsync(new DialogRequest { Title = title, Message = message, PrimaryText = "Close", SecondaryText = null, Tone = DialogTone.Error, Details = details, Icon = icon });
}
