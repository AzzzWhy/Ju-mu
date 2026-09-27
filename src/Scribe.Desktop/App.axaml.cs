using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Scribe.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                e.Handled = true;
                _ = window.ReportError(e.Exception);
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
