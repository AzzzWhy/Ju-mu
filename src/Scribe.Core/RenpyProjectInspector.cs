using System.Text.RegularExpressions;

namespace Scribe.Core;

/// <summary>Looks for a global start label in the scripts beside an export target.</summary>
public static class RenpyProjectInspector
{
    private static readonly Regex StartLabel = new(@"^label[ \t]+start(?:[ \t]*\([^)]*\))?[ \t]*:", RegexOptions.CultureInvariant);

    public static string? FindStartLabel(string exportPath)
    {
        if (string.IsNullOrWhiteSpace(exportPath)) throw new UserFacingException("请先选择导出文件位置。");
        try
        {
            var target = Path.GetFullPath(exportPath);
            var directory = Path.GetDirectoryName(target)!;
            if (!Directory.Exists(directory)) return null;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var files = Directory.EnumerateFiles(directory, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = false
            });
            var count = 0;
            foreach (var file in files)
            {
                if (!file.EndsWith(".rpy", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFullPath(file), target, comparison)) continue;
                if (++count > 10_000) throw new UserFacingException("工程脚本超过 10000 个，无法自动检查 start 入口，请手动确认。");
                foreach (var line in File.ReadLines(file))
                    if (StartLabel.IsMatch(line.TrimStart())) return file;
            }
            return null;
        }
        catch (UserFacingException) { throw; }
        catch (IOException) { throw new UserFacingException("无法读取 Ren’Py 工程中的脚本，请检查权限或手动确认是否已有 label start。"); }
        catch (UnauthorizedAccessException) { throw new UserFacingException("无法读取 Ren’Py 工程中的脚本，请检查权限或手动确认是否已有 label start。"); }
    }
}
