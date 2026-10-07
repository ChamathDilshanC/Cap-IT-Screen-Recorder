using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;

namespace ScreenRecorderApp.Controls;

/// <summary>Page transition: the old page fades out quickly while the new one fades in and rises 6px. 180 ms, eased.</summary>
public sealed class FadeUpTransition : IPageTransition
{
    private static readonly TimeSpan OutDuration = TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan InDuration = TimeSpan.FromMilliseconds(180);

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;
        var tasks = new List<Task>(2);

        if (from is not null)
        {
            var fadeOut = new Animation
            {
                Duration = OutDuration,
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                },
            };
            tasks.Add(fadeOut.RunAsync(from, cancellationToken));
        }

        if (to is not null)
        {
            to.IsVisible = true;
            var fadeIn = new Animation
            {
                Duration = InDuration,
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters =
                        {
                            new Setter(Visual.OpacityProperty, 0d),
                            new Setter(TranslateTransform.YProperty, 6d),
                        },
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters =
                        {
                            new Setter(Visual.OpacityProperty, 1d),
                            new Setter(TranslateTransform.YProperty, 0d),
                        },
                    },
                },
            };
            tasks.Add(fadeIn.RunAsync(to, cancellationToken));
        }

        await Task.WhenAll(tasks);
        if (from is not null && !cancellationToken.IsCancellationRequested) from.IsVisible = false;
    }
}
