using Avalonia;
using Avalonia.Threading;

namespace Scribe.Desktop;

internal static class Program
{
    public static string[] Arguments = [];
    [STAThread]
    public static int Main(string[] args)
    {
        Arguments = args;
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); return 0; }
        catch (Exception ex) { ErrorLog.Write(ex); return 1; }
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}

internal static class ErrorLog
{
    public static string Write(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenpyScribe");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "errors.log");
            if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTime.Now:O} {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n");
            return path;
        }
        catch { return "无法写入日志，请检查文件权限。"; }
    }
}
