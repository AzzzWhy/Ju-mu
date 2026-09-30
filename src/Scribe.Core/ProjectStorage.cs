using System.Text;
using System.Text.Json;

namespace Scribe.Core;

public static class ProjectStorage
{
    private const long MaxProjectBytes = 96L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        MaxDepth = 96,
        IgnoreReadOnlyProperties = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Save(string path, ProjectDocument project)
    {
        Validate(project);
        string json = JsonSerializer.Serialize(project, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxProjectBytes) throw new UserFacingException("工程文件过大，请分章节保存。");
        SafeFileWriter.Write(path, json, true, "工程");
    }

    public static ProjectDocument Load(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) throw new UserFacingException("请选择工程文件。");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxProjectBytes) throw new UserFacingException("工程超过 96 MB，请使用分章节保存的工程。");
            using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 96 });
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("Version", out var version) || version.ValueKind != JsonValueKind.Number ||
                !json.RootElement.TryGetProperty("SourceText", out _) || !json.RootElement.TryGetProperty("Options", out _) ||
                !json.RootElement.TryGetProperty("Export", out _) || !json.RootElement.TryGetProperty("Segments", out _))
                throw new UserFacingException("文件缺少工程版本或必要数据。请选择本程序保存的工程文件。");
            var project = json.RootElement.Deserialize<ProjectDocument>(JsonOptions);
            if (project is null) throw new UserFacingException("工程为空或格式不正确。");
            if (project.Options is { } parseOptions && json.RootElement.GetProperty("Options").ValueKind == JsonValueKind.Object &&
                !json.RootElement.GetProperty("Options").TryGetProperty("AutoDetectKinds", out _))
                parseOptions.AutoDetectKinds = true;
            Validate(project);
            project.Options.Aliases = new Dictionary<string, string>(project.Options.Aliases, StringComparer.OrdinalIgnoreCase);
            project.Export.CharacterVariables = new Dictionary<string, string>(project.Export.CharacterVariables, StringComparer.OrdinalIgnoreCase);
            return project;
        }
        catch (UserFacingException) { throw; }
        catch (JsonException) { throw new UserFacingException("工程文件格式错误或已损坏。请选择本程序保存的工程，必要时使用旁边的 .bak 备份恢复。"); }
        catch (UnauthorizedAccessException) { throw new UserFacingException("没有权限读取工程文件，请复制到可访问的文件夹后重试。"); }
        catch (IOException) { throw new UserFacingException("无法读取工程文件。请检查文件是否存在、被占用或所在磁盘是否可用。"); }
        catch (ArgumentException) { throw new UserFacingException("工程路径或角色映射无效。请检查是否存在仅大小写不同的重复角色名。"); }
        catch (NotSupportedException) { throw new UserFacingException("工程文件路径不受支持，请重新选择。"); }
    }

    private static void Validate(ProjectDocument? project)
    {
        if (project is null) throw new UserFacingException("没有可保存的工程。");
        if (project.Version != 1) throw new UserFacingException($"不支持工程版本 {project.Version}。请使用创建此工程的版本打开，或升级程序。");
        if (project.Options is null || project.Export is null || project.Segments is null || project.Options.Aliases is null || project.Export.CharacterVariables is null || project.SpeakerShortcuts is null || project.ChoiceTrees is null)
            throw new UserFacingException("工程缺少必要的设置或段落数据，请重新导入原文。");
        if (project.SourceText is null || project.SourceFile is null || project.SourceText.Length > DocumentImporter.MaxTextCharacters || project.SourceFile.Length > 32768)
            throw new UserFacingException("工程原文为空、格式错误或超过 1000 万字符。");
        if (!Enum.IsDefined(project.Options.Mode) || !Enum.IsDefined(project.Options.Split) || project.Options.MaxLength is < 10 or > 10_000)
            throw new UserFacingException("工程的解析模式或拆分长度无效，请重新导入原文。");
        if (project.Segments.Count > 200_000 || project.Options.Aliases.Count > 10_000 || project.Export.CharacterVariables.Count > 10_000 || project.SpeakerShortcuts.Count > 10_000)
            throw new UserFacingException("工程包含过多段落或角色，请分章节保存。");
        // Draft export settings may be incomplete; identifiers are validated at export time.
        if (project.Export.Label is null || project.Export.Label.Length > 1024) throw new UserFacingException("工程的剧情标签无效。");
        foreach (var pair in project.Options.Aliases.Concat(project.Export.CharacterVariables))
            if (pair.Key.Length is 0 or > 1024 || pair.Value is null || pair.Value.Length > 1024)
                throw new UserFacingException("工程包含无效或过长的角色映射。");
        var shortcuts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? speaker in project.SpeakerShortcuts)
            if (string.IsNullOrWhiteSpace(speaker) || speaker.Length > 1024 || !shortcuts.Add(speaker.Trim()))
                throw new UserFacingException("工程中有空白、过长或重复的人物姓名快捷选项，请检查后重试。");
        long characters = project.SourceText.Length;
        foreach (var segment in project.Segments)
        {
            if (segment is null || segment.Text is null || segment.Source is null || segment.Speaker is null || segment.Id is null || segment.Warnings is null || !Enum.IsDefined(segment.Kind))
                throw new UserFacingException("工程中有损坏的段落，请使用备份恢复或重新导入原文。");
            if (segment.Id.Length > 1024 || segment.Speaker.Length > 1024 || segment.Paragraph < 0 || segment.Warnings.Count > 100 || segment.Warnings.Any(w => w is null || w.Length > 4096))
                throw new UserFacingException("工程段落的角色名、位置或提示信息无效。");
            characters += segment.Text.Length + segment.Source.Length;
            if (characters > 30_000_000) throw new UserFacingException("工程文字总量过大，请分章节保存。");
        }
        ChoiceTreeValidator.Validate(project.ChoiceTrees, project.Segments);
    }
}

internal static class SafeFileWriter
{
    public static void Write(string path, string content, bool overwrite, string kind)
    {
        string? temporary = null;
        try
        {
            if (string.IsNullOrWhiteSpace(path)) throw new UserFacingException($"请选择{kind}的保存位置。");
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath)!;
            if (!Directory.Exists(directory)) throw new UserFacingException("保存文件夹不存在，请选择现有文件夹。");
            if (Directory.Exists(fullPath)) throw new UserFacingException("保存位置是文件夹，请输入一个文件名。");
            if (!overwrite && File.Exists(fullPath)) throw new UserFacingException("目标文件已存在。请选择新的文件名，或确认覆盖后重试；覆盖前会保留备份。");
            temporary = Path.Combine(directory, ".scribe-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file, new UTF8Encoding(false, true)))
            {
                writer.Write(content);
                writer.Flush();
                file.Flush(true);
            }
            if (overwrite && File.Exists(fullPath))
            {
                string backup = fullPath + ".bak." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture) + "." + Guid.NewGuid().ToString("N")[..8];
                // Atomic replacement also snapshots the original as the backup. If this is unsupported
                // on the destination filesystem, fail without attempting a destructive fallback.
                File.Replace(temporary, fullPath, backup);
            }
            else File.Move(temporary, fullPath, false);
            temporary = null;
        }
        catch (UserFacingException) { throw; }
        catch (UnauthorizedAccessException) { throw new UserFacingException($"没有权限保存{kind}，或目标文件为只读。请选择其他保存位置。"); }
        catch (EncoderFallbackException) { throw new UserFacingException("文字包含损坏的 Unicode 字符，无法保存。请修正该文字后重试。"); }
        catch (IOException) { throw new UserFacingException($"无法保存{kind}。请检查文件是否被占用、磁盘空间是否充足；覆盖时需要文件系统支持安全替换，可尝试另存为新文件。"); }
        catch (ArgumentException) { throw new UserFacingException("保存路径或文件名无效，请重新选择。"); }
        catch (NotSupportedException) { throw new UserFacingException("保存路径或磁盘不支持此操作，请选择本地文件夹并使用新文件名。"); }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
