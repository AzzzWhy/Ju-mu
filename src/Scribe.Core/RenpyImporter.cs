using System.Text;
using System.Text.RegularExpressions;

namespace Scribe.Core;

public sealed record RenpyImportResult(ProjectDocument Project, IReadOnlyList<string> Warnings, string EntryLabel);

/// <summary>A bounded static reader, not a Python evaluator or a complete Ren'Py compiler.</summary>
public static class RenpyImporter
{
    private sealed record Line(int Number, int Indent, string Code, string Source);
    private sealed class Route(string label)
    {
        public string Label = label;
        public List<Segment> Segments = [];
        public List<ChoiceTree> Trees = [];
        public string? Next;
    }
    private static readonly Regex Name = new(@"\A[\p{L}_][\p{L}\p{Nd}_]{0,127}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Label = new(@"\Alabel ([A-Za-z_][A-Za-z0-9_]{0,127}):\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private const string End = "@end";

    public static IReadOnlyList<string> Labels(string text) => Lines(text)
        .Where(line => line.Indent == 0 && Label.IsMatch(line.Code)).Select(line => Label.Match(line.Code).Groups[1].Value).ToList();

    public static RenpyImportResult Read(string path, string? entryLabel = null)
    {
        if (!string.Equals(Path.GetExtension(path), ".rpy", StringComparison.OrdinalIgnoreCase))
            throw new UserFacingException("反向导入需要 .rpy 源文件，不支持 .rpyc 编译文件或整个 Ren’Py 工程。");
        var document = DocumentImporter.Read(path);
        var result = Parse(document.Text, path, entryLabel);
        return result with { Warnings = document.Warnings.Concat(result.Warnings).ToList() };
    }

    public static RenpyImportResult Parse(string text, string sourceFile = "", string? entryLabel = null)
    {
        var reader = new Reader(Lines(text));
        return reader.Build(text, sourceFile, entryLabel);
    }

    private static UserFacingException Error(Line line, string message) => new($"RPY 第 {line.Number} 行：{message}\n{line.Code[..Math.Min(line.Code.Length, 140)]}");
    private static List<Line> Lines(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new UserFacingException("RPY 文件没有内容。");
        if (text.Length > DocumentImporter.MaxTextCharacters) throw new UserFacingException("RPY 文本超过 1000 万字符，请按章节导入。");
        var physical = DocumentImporter.NormalizeNewlines(text).TrimStart('\uFEFF').Split('\n');
        if (physical.Length > 200_000) throw new UserFacingException("RPY 行数过多，请按章节导入。");
        var result = new List<Line>();
        for (var number = 0; number < physical.Length; number++)
        {
            var source = physical[number];
            var spaces = source.Length - source.TrimStart(' ', '\t').Length;
            if (source[..spaces].Contains('\t')) throw new UserFacingException($"RPY 第 {number + 1} 行的缩进含 Tab，请先转换为同宽空格。");
            var code = source[spaces..].TrimEnd();
            if (code.StartsWith("# 说明：", StringComparison.Ordinal))
                code = "@direction " + code[5..];
            else code = StripComment(code).TrimEnd();
            if (code.Length != 0) result.Add(new(number + 1, spaces, code, source));
        }
        return result;
    }

    private static string StripComment(string code)
    {
        char quote = '\0'; bool escaped = false;
        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];
            if (escaped) { escaped = false; continue; }
            if (quote != '\0' && c == '\\') { escaped = true; continue; }
            if (quote != '\0') { if (c == quote) quote = '\0'; }
            else if (c is '\'' or '"') quote = c;
            else if (c == '#') return code[..i];
        }
        return code;
    }

    private sealed class Reader(List<Line> lines)
    {
        private readonly List<string> _warnings = [];
        private readonly Dictionary<string, string?> _characters = new(StringComparer.Ordinal);
        private readonly List<Route> _routes = [];
        private readonly Dictionary<ChoiceBranch, string> _jumps = [];
        private readonly Dictionary<string, string> _wrappers = new(StringComparer.Ordinal);
        private int _nodes;
        private bool _markup;
        private void Warn(string warning) { if (_warnings.Count < 100 && !_warnings.Contains(warning)) _warnings.Add(warning); }
        private void Count(Line line)
        {
            if (++_nodes > 20_000) throw Error(line, "反向导入最多支持 2 万个文本/选项节点，请按章节导入。");
        }

        public RenpyImportResult Build(string text, string file, string? selected)
        {
            // Inspect all definitions without evaluating any arguments or Python expressions.
            foreach (var line in lines.Where(line => line.Indent == 0))
                if (line.Code.StartsWith("define ", StringComparison.Ordinal)) Character(line);
            var labels = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < lines.Count;)
            {
                var header = lines[index++];
                if (header.Indent != 0) continue; // only a top-level declaration can start a route
                var match = Label.Match(header.Code);
                if (!match.Success)
                {
                    if (header.Code.StartsWith("label ", StringComparison.Ordinal)) throw Error(header, "暂不支持带参数或局部 label，请使用全局静态 label。");
                    if (!header.Code.StartsWith("define ", StringComparison.Ordinal)) Warn($"第 {header.Number} 行的顶层声明未转换：{header.Code[..Math.Min(header.Code.Length, 80)]}。原代码保存在左侧原文中，重新导出不会还原它。");
                    continue;
                }
                var label = match.Groups[1].Value;
                if (!labels.Add(label)) throw Error(header, "label 重复，无法可靠建立跳转。");
                if (labels.Count > 10_000) throw Error(header, "label 过多，请按章节导入。");
                var last = index;
                while (last < lines.Count && lines[last].Indent > 0) last++;
                var body = lines.GetRange(index, last - index); index = last;
                if (body.Count == 2 && body[0].Code.StartsWith("call ", StringComparison.Ordinal) && body[1].Code == "return")
                {
                    var target = body[0].Code[5..];
                    if (!LabelName(target)) throw Error(body[0], "启动包装器只能 call 一个静态全局 label。");
                    _wrappers.Add(label, target); _routes.Add(new(label) { Next = target }); continue;
                }
                var route = new Route(label);
                if (body.Count > 0)
                {
                    var cursor = 0;
                    ParseRoute(body, ref cursor, body[0].Indent, route);
                    if (cursor != body.Count) throw Error(body[cursor], "缩进或 label 内容无法转换。");
                }
                _routes.Add(route);
            }
            if (_routes.Count == 0) throw new UserFacingException("未找到可导入的全局 label。请选择包含剧情的 .rpy 源文件。");
            for (var i = 0; i < _routes.Count; i++)
                _routes[i].Next ??= i + 1 < _routes.Count ? _routes[i + 1].Label : End;
            string Resolve(string name)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (_wrappers.TryGetValue(name, out var target))
                {
                    if (!visited.Add(name)) throw new UserFacingException("启动 call 包装器出现循环，无法反向导入。");
                    name = target;
                }
                if (name != End && !labels.Contains(name)) throw new UserFacingException($"跳转目标 label {name} 不在本文件中。第一版仅支持单个文件，请先合并相关剧情脚本。");
                return name;
            }
            var requested = selected ?? (_routes.Any(route => route.Label == "start") ? "start" : _routes[0].Label);
            if (!labels.Contains(requested)) throw new UserFacingException("选择的入口 label 不存在，请重新选择。");
            var entry = Resolve(requested);
            var routes = _routes.Where(route => !_wrappers.ContainsKey(route.Label)).ToDictionary(route => route.Label, StringComparer.Ordinal);
            foreach (var route in routes.Values) route.Next = Resolve(route.Next!);
            foreach (var branch in _jumps.Keys.ToList()) _jumps[branch] = Resolve(_jumps[branch]);
            // Flatten the entry's deterministic continuation chain into the main route.
            // Other labels remain bag nodes. A cycle cannot be flattened without changing play order.
            var main = new List<Route>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var target = entry; target != End; target = routes[target].Next!)
            {
                if (!seen.Add(target)) throw new UserFacingException("入口 label 的无条件 jump / 顺序继续存在循环，当前版本无法转换；请换一个有结束点的入口。菜单分支内的回跳可以导入。");
                main.Add(routes[target]);
            }
            if (!main.Any(route => route.Segments.Any(segment => segment.Include && !string.IsNullOrWhiteSpace(segment.Text)) || route.Trees.Count > 0))
                throw new UserFacingException("选择的入口没有可编辑剧情。请选择包含台词或 menu 的入口 label。");
            foreach (var route in routes.Values)
                if (route.Segments.Count == 0) route.Segments.Add(Anchor(route.Label));
            var project = new ProjectDocument { SourceFile = file, SourceText = text, RenpyEntryLabel = requested,
                Options = new ParseOptions { Mode = ImportMode.Script, Split = SplitMode.Paragraph },
                Export = new ExportOptions { Label = entry, CreateStartLabel = requested == "start" && entry != "start" } };
            foreach (var route in main) { project.Segments.AddRange(route.Segments); project.ChoiceTrees.AddRange(route.Trees); }
            var destinations = main.ToDictionary(route => route.Label, route => route.Segments[0].Id, StringComparer.Ordinal);
            foreach (var route in routes.Values.Where(route => !seen.Contains(route.Label)))
            {
                var fragment = new StoryFragment { Title = route.Label, Segments = route.Segments, ChoiceTrees = route.Trees };
                project.Fragments.Add(fragment); destinations.Add(route.Label, fragment.Id);
            }
            if (_jumps.Values.Contains(End))
            {
                var ending = new StoryFragment { Title = "结束剧情（导入的 return）", Segments = [Anchor("return")] };
                project.Fragments.Add(ending); destinations.Add(End, ending.Id);
            }
            foreach (var fragment in project.Fragments.Where(fragment => destinations.ContainsKey(fragment.Title)))
            {
                var next = routes[fragment.Title].Next!;
                fragment.NextNodeId = next == End ? "" : destinations[next];
            }
            foreach (var (branch, target) in _jumps) branch.TargetNodeId = destinations[target];
            project.SpeakerShortcuts = routes.Values.SelectMany(route => route.Segments.Concat(StoryFragmentEditor.Branches(route.Trees)
                .SelectMany(branch => branch.Items.Where(item => item.Segment != null).Select(item => item.Segment!))))
                .Where(segment => segment.Speaker.Length > 0).Select(segment => segment.Speaker).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!routes.Values.Any(route => route.Segments.Any(segment => segment.Include) || route.Trees.Count > 0))
                throw new UserFacingException("未找到可编辑的对白、旁白或 menu。空标签和纯代码脚本不能导入为故事。");
            StoryGraphValidator.Validate(project.Segments, project.ChoiceTrees, project.Fragments);
            Warn("仅转换文本、选项和静态跳转。角色样式、演出资源及文本标签/插值不自动还原；角色代号会按人物姓名重新生成。原始 RPY 不会被覆盖。");
            return new(project, _warnings, requested);
        }

        private static Segment Anchor(string name) => new() { Text = $"〔label {name}：结构锚点，不显示〕", Kind = SegmentKind.Direction, Include = false, Reviewed = true };
        private static bool LabelName(string name) => Label.IsMatch("label " + name + ":");

        private void Character(Line line)
        {
            var assignment = line.Code[7..].Split('=', 2, StringSplitOptions.TrimEntries);
            if (assignment.Length != 2 || !Name.IsMatch(assignment[0])) { Warn($"第 {line.Number} 行的 define 未转换。" ); return; }
            var value = assignment[1];
            if (!value.StartsWith("Character(", StringComparison.Ordinal)) { Warn($"第 {line.Number} 行的非 Character 定义未转换。"); return; }
            if (!value.EndsWith(')')) throw Error(line, "Character 定义请写在同一行。");
            var argument = value[10..^1].TrimStart(); string? name;
            if (argument == "None" || argument.StartsWith("None,", StringComparison.Ordinal)) name = null;
            else
            {
                var cursor = 0; name = String(line, argument, ref cursor);
                var rest = argument[cursor..].Trim();
                if (rest.Length > 0 && !rest.StartsWith(',')) throw Error(line, "角色名必须为静态字符串。");
                if (rest.Contains("dynamic", StringComparison.Ordinal)) throw Error(line, "暂不支持动态人物姓名。");
                if (rest.Length > 0) Warn($"第 {line.Number} 行的 Character 样式参数未转换，只保留人物姓名。");
            }
            if (!_characters.TryAdd(assignment[0], name)) throw Error(line, "人物代号重复定义。");
        }

        private void ParseRoute(List<Line> body, ref int index, int indent, Route route)
        {
            while (index < body.Count && body[index].Indent >= indent)
            {
                var line = body[index];
                if (line.Indent != indent) throw Error(line, "非 menu 内容出现额外缩进，暂不支持这类代码块。");
                if (route.Next != null && line.Code != "pass") throw Error(line, "jump / return 后仍有内容，暂不转换不可达代码。");
                if (line.Code == "menu:")
                {
                    var tree = Menu(body, ref index, 1);
                    if (route.Segments.Count == 0) route.Segments.Add(Anchor(route.Label));
                    tree.AnchorSegmentId = route.Segments[^1].Id; tree.Placement = ChoicePlacement.After; route.Trees.Add(tree); continue;
                }
                index++;
                if (line.Code == "pass") continue;
                if (line.Code == "return") { route.Next = End; continue; }
                if (line.Code.StartsWith("jump ", StringComparison.Ordinal)) { route.Next = Jump(line); continue; }
                route.Segments.Add(Say(line));
            }
        }

        private ChoiceTree Menu(List<Line> body, ref int index, int depth)
        {
            var header = body[index++]; Count(header);
            if (depth > 12) throw Error(header, "menu 嵌套超过 12 层，请拆分。");
            if (index == body.Count || body[index].Indent <= header.Indent) throw Error(header, "menu 缺少选项内容。");
            var tree = new ChoiceTree(); var indent = body[index].Indent;
            while (index < body.Count && body[index].Indent > header.Indent)
            {
                var option = body[index];
                if (option.Indent != indent) throw Error(option, "选项缩进不一致。");
                var position = 0; var text = String(option, option.Code, ref position); var suffix = option.Code[position..].Trim();
                if (suffix.Length == 0 && tree.Branches.Count == 0 && tree.Title.Length == 0) { tree.Title = text; index++; continue; }
                if (suffix != ":") throw Error(option, "暂不支持带条件、set 或其他表达式的选项。");
                if (string.IsNullOrWhiteSpace(text)) throw Error(option, "选项文字不能为空。");
                Count(option); var branch = new ChoiceBranch { Label = text }; tree.Branches.Add(branch); index++;
                if (index == body.Count || body[index].Indent <= indent) throw Error(option, "选项缺少分支内容。");
                var childIndent = body[index].Indent; var terminal = false;
                while (index < body.Count && body[index].Indent > indent)
                {
                    var child = body[index];
                    if (child.Indent != childIndent) throw Error(child, "分支缩进不一致。");
                    if (terminal && child.Code != "pass") throw Error(child, "jump / return 后仍有分支内容。");
                    if (child.Code == "menu:") { branch.Items.Add(new() { Tree = Menu(body, ref index, depth + 1) }); continue; }
                    index++;
                    if (child.Code == "pass") continue;
                    if (child.Code == "return") { _jumps.Add(branch, End); terminal = true; continue; }
                    if (child.Code.StartsWith("jump ", StringComparison.Ordinal)) { _jumps.Add(branch, Jump(child)); terminal = true; continue; }
                    branch.Items.Add(new() { Segment = Say(child) });
                }
            }
            if (tree.Branches.Count == 0) throw Error(header, "menu 至少需要一个选项。");
            return tree;
        }

        private static string Jump(Line line)
        {
            var target = line.Code[5..];
            if (!LabelName(target)) throw Error(line, "仅支持 jump 全局标签；暂不支持 expression 或局部标签。");
            return target;
        }

        private Segment Say(Line line)
        {
            Count(line);
            if (line.Code.StartsWith("@direction ", StringComparison.Ordinal))
                return new() { Text = line.Code[11..], Source = line.Source, Paragraph = line.Number, Kind = SegmentKind.Direction, Reviewed = true };
            var code = line.Code; var index = 0; string speaker = ""; string text; bool narration = true;
            if (code[0] is '\'' or '"')
            {
                text = String(line, code, ref index); Skip(code, ref index);
                if (index < code.Length && code[index] is '\'' or '"') { speaker = text; text = String(line, code, ref index); narration = false; }
            }
            else
            {
                while (index < code.Length && !char.IsWhiteSpace(code[index])) index++;
                var variable = code[..index];
                if (!Name.IsMatch(variable)) throw Error(line, "暂不支持此语句，只转换 say、menu、静态 jump、return 和 pass。");
                if (new[] { "if", "elif", "else", "while", "call", "python", "scene", "show", "hide", "play", "stop", "pause", "with", "window", "voice", "menu", "return", "jump", "extend", "nvl" }.Contains(variable))
                    throw Error(line, "暂不支持此控制逻辑或演出语句，请先整理为文本/选项/静态跳转脚本。");
                text = String(line, code, ref index);
                if (_characters.TryGetValue(variable, out var name)) { speaker = name ?? ""; narration = name == null; }
                else if (variable is "s" or "narrator") { narration = true; }
                else { speaker = variable; narration = false; Warn($"第 {line.Number} 行的角色 {variable} 未在本文件定义，暂以代号作为姓名。" ); }
            }
            Skip(code, ref index);
            if (index != code.Length) throw Error(line, "暂不支持 say 的属性、with、参数或多个附加表达式。");
            if (string.IsNullOrWhiteSpace(text)) throw Error(line, "空白 say 文本无法作为可编辑剧情导入。");
            var segment = new Segment { Text = text, Speaker = speaker, Kind = narration ? SegmentKind.Narration : SegmentKind.Dialogue,
                Paragraph = line.Number, Source = line.Source, Reviewed = false };
            if (_markup) segment.Warnings.Add("原脚本包含文本标签或插值。此版本按字面导入，重新导出不会执行其格式/表达式，请人工核对。");
            return segment;
        }

        private void Skip(string value, ref int position) { while (position < value.Length && char.IsWhiteSpace(value[position])) position++; }
        private string String(Line line, string code, ref int position)
        {
            _markup = false;
            Skip(code, ref position);
            if (position == code.Length || code[position] is not ('\'' or '"')) throw Error(line, "需要同一行内的静态引号字符串，暂不支持字符串表达式或多行字符串。");
            var quote = code[position++];
            if (position + 1 < code.Length && code[position] == quote && code[position + 1] == quote) throw Error(line, "暂不支持三引号字符串，请转换为普通字符串和 \\n。");
            var result = new StringBuilder(); bool closed = false;
            while (position < code.Length)
            {
                var c = code[position++];
                if (c == quote) { closed = true; break; }
                if (c == '\\')
                {
                    if (position == code.Length) throw Error(line, "字符串转义不完整。");
                    var escaped = code[position++];
                    // Ren'Py's lexer is not Python's: \t becomes t, while \n is a newline.
                    // Backslash-escaped brackets/tags are literal, not interpolated.
                    if (escaped == 'u')
                    {
                        var begin = position;
                        while (position < code.Length && position - begin < 4 && Uri.IsHexDigit(code[position])) position++;
                        if (position == begin) throw Error(line, "Unicode 转义缺少十六进制数字。");
                        result.Append((char)Convert.ToInt32(code[begin..position], 16));
                    }
                    else result.Append(escaped == 'n' ? '\n' : escaped);
                }
                else if (c == ' ') { result.Append(' '); while (position < code.Length && code[position] == ' ') position++; }
                else if (c is '[' or '{' or '%' or '〖' && position < code.Length && code[position] == c) { result.Append(c); position++; }
                else { if (c is '[' or '{') _markup = true; result.Append(c); }
            }
            if (!closed) throw Error(line, "字符串缺少结尾引号；暂不支持跨物理行字符串。");
            return result.ToString();
        }
    }
}
