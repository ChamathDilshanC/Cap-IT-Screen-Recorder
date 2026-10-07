using ScreenRecorderApp.Views;

namespace ScreenRecorderApp.Services;

/// <summary>One Review &amp; Export window per recording; reopening a recording focuses its existing window.</summary>
public sealed class ReviewWindowService : IReviewWindowService
{
    private readonly List<ReviewWindow> _windows = [];

    public void Open(string recordingPath)
    {
        var existing = _windows.FirstOrDefault(w => w.FilePath.Equals(recordingPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Activate();
            return;
        }

        var window = new ReviewWindow(recordingPath);
        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        window.Show();
    }

    public async Task CloseAllAsync()
    {
        foreach (var window in _windows.ToArray()) await window.CloseAndSaveAsync();
        _windows.Clear();
    }

    public void CloseAll()
    {
        foreach (var window in _windows.ToArray()) window.Close();
        _windows.Clear();
    }
}
