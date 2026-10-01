using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Scribe.Desktop;

/// <summary>Short, cancellable visual feedback. Never delays or changes an editing operation.</summary>
internal static class UiMotion
{
    private sealed class RevealState
    {
        public CancellationTokenSource? Cancellation;
        public long LastStarted;
    }

    private static readonly ConditionalWeakTable<Control, RevealState> Reveals = new();
    private static readonly List<WeakReference<Window>> Windows = [];
    public static bool Enabled { get; private set; } = true;

    public static void Attach(Window window)
    {
        Windows.RemoveAll(reference => !reference.TryGetTarget(out _));
        Windows.Add(new WeakReference<Window>(window));
        window.Classes.Set("motion-off", !Enabled);
        window.Opened += (_, _) => { if (window.Content is Control content) Reveal(content); };
        window.Closed += (_, _) => { if (window.Content is Control content) Cancel(content); };
    }

    public static void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        foreach (var reference in Windows)
        {
            if (!reference.TryGetTarget(out var window)) continue;
            window.Classes.Set("motion-off", !enabled);
            if (!enabled)
            {
                if (window.Content is Control content) Cancel(content);
                foreach (var control in window.GetVisualDescendants().OfType<Control>()) Cancel(control);
            }
        }
    }

    private static void Cancel(Control control)
    {
        if (Reveals.TryGetValue(control, out var state)) state.Cancellation?.Cancel();
    }

    public static async void Reveal(Control control)
    {
        if (!Enabled || control.GetVisualRoot() is null) return;
        var state = Reveals.GetOrCreateValue(control);
        var now = Environment.TickCount64;
        // Rapid drag-selection must not keep restarting animations.
        if (now - state.LastStarted < 100) return;
        state.LastStarted = now;
        state.Cancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        state.Cancellation = cancellation;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(160), Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.86d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d) } }
            }
        };
        EventHandler<VisualTreeAttachmentEventArgs> detached = (_, _) => cancellation.Cancel();
        control.DetachedFromVisualTree += detached;
        try { await animation.RunAsync(control, cancellationToken: cancellation.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorLog.Write(ex); }
        finally
        {
            control.DetachedFromVisualTree -= detached;
            if (ReferenceEquals(state.Cancellation, cancellation)) state.Cancellation = null;
        }
    }
}
