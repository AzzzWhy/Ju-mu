using System.IO.Compression;
using System.Text;
using Scribe.Core;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var root = Path.Combine(Path.GetTempPath(), "renpy-scribe-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var tests = new List<(string Name, Action Body)>();
void Test(string name, Action body) => tests.Add((name, body));
string PathFor(string name) => Path.Combine(root, name);
ParseResult Parse(string text, ImportMode mode = ImportMode.Script, SplitMode split = SplitMode.Sentence) =>
    ManuscriptParser.Parse(text, new ParseOptions { Mode = mode, Split = split });

Test("剧本：中文全角冒号和英文半角冒号", () =>
{
    var result = Parse("小明：你好。\nAlice: Hello.");
    Equal(2, result.Segments.Count);
    Equal("小明", result.Segments[0].Speaker);
    Equal("你好。", result.Segments[0].Text);
    Equal("Alice", result.Segments[1].Speaker);
    True(result.Segments.All(x => x.Kind == SegmentKind.Dialogue), "角色台词应识别为对白");
});
Test("剧本：时间和解释冒号不误判为角色", () =>
{
    var result = Parse("时间是 18:30。\n他只有一个念头：离开这里。");
    True(result.Segments.All(x => x.Kind == SegmentKind.Narration && x.Speaker == ""), "旁白中的冒号不能生成角色");
    Contains("18:30", string.Join("", result.Segments.Select(x => x.Text)));
    Contains("一个念头：离开", string.Join("", result.Segments.Select(x => x.Text)));
});
Test("剧本：角色角引号格式", () =>
{
    var item = Parse("小明「我们走吧。」").Segments.Single();
    Equal("小明", item.Speaker);
    Equal(SegmentKind.Dialogue, item.Kind);
    Contains("我们走吧。", item.Text);
});
Test("剧本：角色别名映射", () =>
{
    var result = ManuscriptParser.Parse("明：你好。", new ParseOptions
    {
        Mode = ImportMode.Script,
        Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["明"] = "小明" }
    });
    Equal("小明", result.Segments.Single().Speaker);
});
Test("断句：中文和英文句末符号", () =>
{
    var pieces = ManuscriptParser.SplitSentences("你好。再见！Hello. Goodbye?", SplitMode.Sentence);
    Equal(4, pieces.Count);
    Equal("你好。再见！Hello.Goodbye?", WithoutWhitespace(string.Concat(pieces)));
});
Test("断句：英文头衔、缩写与小数", () =>
{
    var input = "Dr. Smith met Mr. Jones. It cost 3.14 dollars.";
    var pieces = ManuscriptParser.SplitSentences(input, SplitMode.Sentence);
    Equal(2, pieces.Count);
    Contains("Dr. Smith met Mr. Jones.", pieces[0]);
    Contains("3.14", pieces[1]);
});
Test("断句：网址与邮箱中的符号", () =>
{
    var input = "Visit https://example.com/a?q=1.2 or email foo.bar@example.com. Then leave.";
    var pieces = ManuscriptParser.SplitSentences(input, SplitMode.Sentence);
    Equal(2, pieces.Count);
    Contains("https://example.com/a?q=1.2", pieces[0]);
    Contains("foo.bar@example.com.", pieces[0]);
    Equal(WithoutWhitespace(input), WithoutWhitespace(string.Concat(pieces)));
});
Test("断句：撇号与连续句点保留", () =>
{
    var input = "Don't go... I'm waiting.";
    var pieces = ManuscriptParser.SplitSentences(input, SplitMode.Sentence);
    True(pieces.All(x => x != "."), "省略号不能切成孤立句点");
    Equal(WithoutWhitespace(input), WithoutWhitespace(string.Concat(pieces)));
});
Test("断句：括号内和引号内内容不丢失", () =>
{
    var input = "她说：“真的？现在就走！”（小声）然后点头。";
    var pieces = ManuscriptParser.SplitSentences(input, SplitMode.Sentence);
    Equal(WithoutWhitespace(input), WithoutWhitespace(string.Concat(pieces)));
    True(pieces.All(x => !new[] { "”", "）", "！", "。" }.Contains(x)), "不产生孤立闭引号或句末符号");
});
Test("断句：按段落保持原段落", () =>
{
    Equal(1, ManuscriptParser.SplitSentences("第一句。第二句。", SplitMode.Paragraph).Count);
});
Test("断句：阅读长度拆分不会吞字", () =>
{
    var input = string.Concat(Enumerable.Repeat("这是长篇测试，包含中文和 English 单词，", 12));
    var pieces = ManuscriptParser.SplitSentences(input, SplitMode.ReadingLength, 40);
    True(pieces.Count > 1, "阅读长度应拆分长文本");
    Equal(WithoutWhitespace(input), WithoutWhitespace(string.Concat(pieces)));
});
Test("断句：阅读长度边界不能拆坏 emoji", () =>
{
    var input = new string('甲', 39) + "😊" + new string('乙', 39) + "😊结束。";
    var pieces = ManuscriptParser.SplitSentences(input, SplitMode.ReadingLength, 40);
    Equal(input, string.Concat(pieces));
    foreach (var piece in pieces) _ = new UTF8Encoding(false, true).GetBytes(piece);
});
Test("小说：对白及前后动作旁白完整保留", () =>
{
    const string input = "小明推开门。“你好。”他放下书包。";
    var result = Parse(input, ImportMode.Novel);
    True(result.Segments.Any(x => x.Kind == SegmentKind.Dialogue), "引号内容应识别为对白");
    True(result.Segments.Any(x => x.Kind == SegmentKind.Narration), "动作应保留为旁白");
    Equal(WithoutOuterQuoteMarks(input), WithoutOuterQuoteMarks(string.Concat(result.Segments.Select(x => x.Text))));
    True(result.Segments.All(x => x.Source == input), "每个片段应能追溯原段落");
});
Test("小说：未明确说话人的对白提示确认", () =>
{
    var result = Parse("“你是谁？”", ImportMode.Novel);
    var dialogue = result.Segments.Single(x => x.Kind == SegmentKind.Dialogue);
    True(dialogue.Warnings.Count > 0 || result.Warnings.Count > 0, "未知角色需要提示");
    Contains("你是谁", dialogue.Text);
});
Test("小说：插入说话动作保留所有原文", () =>
{
    const string input = "“你确定？”她问，“现在就走？”";
    var result = Parse(input, ImportMode.Novel);
    Equal(WithoutOuterQuoteMarks(input), WithoutOuterQuoteMarks(string.Concat(result.Segments.Select(x => x.Text))));
    Contains("她问", string.Concat(result.Segments.Select(x => x.Text)));
});
Test("小说：不配对引号提示且保留内容", () =>
{
    const string input = "他看向远方。“我们还能再见吗？";
    var result = Parse(input, ImportMode.Novel);
    True(result.Warnings.Count > 0 || result.Segments.Any(x => x.Warnings.Count > 0), "不配对引号必须提示");
    Equal(WithoutWhitespace(input), WithoutWhitespace(string.Concat(result.Segments.Select(x => x.Text))));
});
Test("空输入返回明确提示", () =>
{
    var result = Parse(" \n\t ");
    Equal(0, result.Segments.Count);
    True(result.Warnings.Count > 0, "空内容应提示用户");
});
Test("小说：回答动词不成为人名的一部分", () =>
{
    var dialogue = Parse("周远回答：“当然。”", ImportMode.Novel).Segments.Single(s => s.Kind == SegmentKind.Dialogue);
    Equal("周远", dialogue.Speaker);
});
Test("断句：中文网址后紧接中文句子", () =>
{
    var pieces = ManuscriptParser.SplitSentences("访问 https://example.com。下一句话。", SplitMode.Sentence);
    Equal(2, pieces.Count);
    Equal("下一句话。", pieces[1]);
});
Test("断句：中文句子使用英文句点", () =>
{
    Equal(2, ManuscriptParser.SplitSentences("这是第一句.这是第二句。", SplitMode.Sentence).Count);
});

Test("TXT：UTF-8 中文和 emoji", () =>
{
    var path = PathFor("utf8.txt");
    File.WriteAllText(path, "你好，Ren’Py 👋\n第二段。", new UTF8Encoding(false));
    var doc = DocumentImporter.Read(path);
    Contains("你好，Ren’Py 👋", doc.Text);
    Contains("第二段。", doc.Text);
});
Test("TXT：带 BOM 的 UTF-16", () =>
{
    var path = PathFor("utf16.txt");
    File.WriteAllText(path, "小明：你好。\nAlice: Hello.", Encoding.Unicode);
    Equal("小明：你好。\nAlice: Hello.", DocumentImporter.Read(path).Text.Replace("\r\n", "\n"));
});
Test("TXT：GB18030 中文编码并提示", () =>
{
    var path = PathFor("gb18030.txt");
    File.WriteAllText(path, "旧版文稿：这是一段中文。", Encoding.GetEncoding("GB18030"));
    var doc = DocumentImporter.Read(path);
    Equal("旧版文稿：这是一段中文。", doc.Text);
    True(doc.Warnings.Count > 0, "兼容编码应提示");
});
Test("TEXT 与 MARKDOWN：导入格式和提示与界面一致", () =>
{
    var plain = PathFor("script.text");
    File.WriteAllText(plain, "小明：你好。", new UTF8Encoding(false));
    Equal("小明：你好。", DocumentImporter.Read(plain).Text);
    var markdown = PathFor("story.markdown");
    File.WriteAllText(markdown, "# 第一章\n正文。", new UTF8Encoding(false));
    var imported = DocumentImporter.Read(markdown);
    Contains("# 第一章", imported.Text);
    True(imported.Warnings.Count > 0, "Markdown 纯文本导入应提示格式标记会保留");
});
Test("DOCX：段落、表格、超链接与手动换行顺序", () =>
{
    var path = PathFor("structure.docx");
    WriteDocx(path, """
        <w:p><w:r><w:t>第一段</w:t><w:br/><w:t>手动换行</w:t></w:r></w:p>
        <w:tbl><w:tr><w:tc><w:p><w:r><w:t>表格左侧</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>表格右侧</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
        <w:p><w:hyperlink><w:r><w:t>可见链接</w:t></w:r></w:hyperlink></w:p>
        <w:p><w:r><w:t>末段</w:t></w:r></w:p>
        """);
    var text = DocumentImporter.Read(path).Text;
    Contains("第一段\n手动换行", text.Replace("\r\n", "\n"));
    InOrder(text, "第一段", "手动换行", "表格左侧", "表格右侧", "可见链接", "末段");
});
Test("DOCX：文档末段无结尾换行仍保留", () =>
{
    var path = PathFor("last-paragraph.docx");
    WriteDocx(path, "<w:p><w:r><w:t>最后一句没有句号</w:t></w:r></w:p>");
    Contains("最后一句没有句号", DocumentImporter.Read(path).Text);
});
Test("DOCX：损坏压缩包转换为可读报错", () =>
{
    var path = PathFor("corrupt.docx");
    File.WriteAllText(path, "This is not a zip archive.");
    UserError(() => DocumentImporter.Read(path));
});
Test("DOCX：畸形 XML 转换为可读报错", () =>
{
    var path = PathFor("badxml.docx");
    WriteDocx(path, "<w:p><w:r><w:t>不完整");
    UserError(() => DocumentImporter.Read(path));
});
Test("旧 DOC 格式提示另存 DOCX", () =>
{
    var path = PathFor("legacy.doc");
    File.WriteAllBytes(path, [0xD0, 0xCF, 0x11, 0xE0]);
    var error = UserError(() => DocumentImporter.Read(path));
    Contains("docx", error.Message.ToLowerInvariant());
});

Test("导出：Ren’Py 字面文本完整转义", () =>
{
    const string raw = "[player] {b}100% \"hello\" \\path\nsecond";
    var script = RenpyExporter.Generate([Narration(raw)], new ExportOptions());
    Contains("[[player]", script);
    Contains("{{b}", script);
    Contains("100%%", script);
    Contains("\\\"hello\\\"", script);
    Contains("\\\\path", script);
    Contains("\\nsecond", script);
});
Test("导出：恶意引号换行保持在字符串内", () =>
{
    const string raw = "\"\nlabel injected:\n    $ malicious()\n\"";
    var script = RenpyExporter.Generate([Narration(raw)], new ExportOptions());
    True(!script.Split('\n').Any(x => x.StartsWith("label injected:", StringComparison.Ordinal)), "原文不能插入执行语句");
    Contains("\\nlabel\\ injected:", script);
});
Test("导出：角色名作为文本，变量须显式映射", () =>
{
    var item = new Segment { Kind = SegmentKind.Dialogue, Speaker = "小明", Text = "你好。" };
    var script = RenpyExporter.Generate([item], new ExportOptions());
    Contains("\"小明\"", script);
    var mapped = RenpyExporter.Generate([item], new ExportOptions
    {
        CharacterVariables = new Dictionary<string, string> { ["小明"] = "ming" }
    });
    Contains("ming \"你好。\"", mapped);
});
Test("导出：拒绝非法 label 和变量", () =>
{
    foreach (var label in new[] { "a b", "foo:\nlabel evil", "return", "123name", "中文" })
        UserError(() => RenpyExporter.Generate([Narration("test")], new ExportOptions { Label = label }));
    var item = new Segment { Kind = SegmentKind.Dialogue, Speaker = "Alice", Text = "Hello." };
    foreach (var variable in new[] { "a.b", "$ evil()", "class", "x\ny" })
        UserError(() => RenpyExporter.Generate([item], new ExportOptions
        {
            CharacterVariables = new Dictionary<string, string> { ["Alice"] = variable }
        }));
});
Test("导出：说明多行全部注释，排除内容不导出", () =>
{
    var script = RenpyExporter.Generate([
        new Segment { Kind = SegmentKind.Direction, Text = "场景：夜晚\nlabel not_code:\n    $ not_code()" },
        new Segment { Kind = SegmentKind.Narration, Text = "SKIP_THIS", Include = false },
        Narration("保留")], new ExportOptions { DirectionAsComments = true });
    True(!script.Contains("SKIP_THIS", StringComparison.Ordinal), "排除内容不应导出");
    True(script.Split('\n').Where(x => x.Contains("not_code", StringComparison.Ordinal)).All(x => x.TrimStart().StartsWith('#')), "说明每一行都应注释");
});
Test("导出：默认不覆盖，明确覆盖保留备份", () =>
{
    var path = PathFor("overwrite.rpy");
    File.WriteAllText(path, "ORIGINAL");
    UserError(() => RenpyExporter.Write(path, [Narration("新内容")], new ExportOptions()));
    Equal("ORIGINAL", File.ReadAllText(path));
    RenpyExporter.Write(path, [Narration("新内容")], new ExportOptions(), overwrite: true);
    Contains("新内容", File.ReadAllText(path));
    True(Directory.GetFiles(root, "overwrite.rpy.bak.*").Any(x => File.ReadAllText(x) == "ORIGINAL"), "覆盖应保留原内容备份");
});
Test("选项树：同一句之前和之后可插入多个菜单，并保持顺序", () =>
{
    var first = Narration("正文第一句");
    var second = Narration("正文第二句");
    ChoiceTree Menu(string anchor, ChoicePlacement placement, string label, string content) => new()
    {
        AnchorSegmentId = anchor, Placement = placement,
        Branches = [new ChoiceBranch { Label = label, Items = [new ChoiceItem { Segment = Narration(content) }] }]
    };
    var script = RenpyExporter.Generate([first, second], new ExportOptions(), [
        Menu(first.Id, ChoicePlacement.Before, "前置选项", "前置内容"),
        Menu(first.Id, ChoicePlacement.After, "后置选项一", "后置内容一"),
        Menu(first.Id, ChoicePlacement.After, "后置选项二", "后置内容二")
    ]);
    InOrder(script, "前置选项", "前置内容", "正文第一句", "后置选项一", "后置内容一", "后置选项二", "后置内容二", "正文第二句");
    Equal(3, script.Split("menu:", StringSplitOptions.None).Length - 1);
});
Test("选项树：嵌套分支导出并在分支结束后汇合", () =>
{
    var anchor = Narration("开场。");
    var choice = new ChoiceTree
    {
        AnchorSegmentId = anchor.Id, Title = "你要去哪里？",
        Branches = [
            new ChoiceBranch
            {
                Label = "去森林", Items = [
                    new ChoiceItem { Segment = Narration("林间小路。") },
                    new ChoiceItem { Tree = new ChoiceTree
                    {
                        Title = "往哪边走？",
                        Branches = [
                            new ChoiceBranch { Label = "向左", Items = [new ChoiceItem { Segment = Narration("遇见小屋。") }] },
                            new ChoiceBranch { Label = "向右" }
                        ]
                    } },
                    new ChoiceItem { Segment = Narration("走出森林。") }
                ]
            },
            new ChoiceBranch { Label = "留在原地" }
        ]
    };
    var script = RenpyExporter.Generate([anchor, Narration("共同结局。")], new ExportOptions(), [choice]);
    Contains("    menu:\n        \"你要去哪里？\"", script);
    Contains("            menu:\n                \"往哪边走？\"", script);
    Contains("                    pass\n", script);
    InOrder(script, "开场。", "去森林", "林间小路。", "往哪边走？", "向左", "遇见小屋。", "向右", "走出森林。", "留在原地", "共同结局。");
});
Test("选项树：标题和选项文本按 Ren'Py 字面规则转义", () =>
{
    var anchor = Narration("起点");
    var tree = new ChoiceTree
    {
        AnchorSegmentId = anchor.Id, Title = "[player] 的 {b}选择？",
        Branches = [new ChoiceBranch { Label = "\"走\" 100%" }]
    };
    var script = RenpyExporter.Generate([anchor], new ExportOptions(), [tree]);
    Contains("[[player]", script);
    Contains("{{b}", script);
    Contains("\\\"走\\\"\\ 100%%", script);
});
Test("选项树：无效锚点、重复 ID、循环和损坏节点明确报错", () =>
{
    var anchor = Narration("起点");
    var missing = new ChoiceTree { AnchorSegmentId = "deleted", Branches = [new ChoiceBranch { Label = "选项" }] };
    Contains("关联", UserError(() => RenpyExporter.Generate([anchor], new ExportOptions(), [missing])).Message);

    var duplicated = new ChoiceTree { Id = anchor.Id, AnchorSegmentId = anchor.Id, Branches = [new ChoiceBranch { Label = "选项" }] };
    Contains("重复", UserError(() => RenpyExporter.Generate([anchor], new ExportOptions(), [duplicated])).Message);

    var cyclic = new ChoiceTree { AnchorSegmentId = anchor.Id, Branches = [new ChoiceBranch { Label = "循环" }] };
    cyclic.Branches[0].Items.Add(new ChoiceItem { Tree = cyclic });
    UserError(() => RenpyExporter.Generate([anchor], new ExportOptions(), [cyclic]));

    var broken = new ChoiceTree { AnchorSegmentId = anchor.Id, Branches = [new ChoiceBranch { Label = "坏项", Items = [new ChoiceItem()] }] };
    Contains("句子或一个子选项树", UserError(() => RenpyExporter.Generate([anchor], new ExportOptions(), [broken])).Message);
});
Test("选项树：不完整草稿可保存但导出需有分支和选项名", () =>
{
    var anchor = Narration("起点");
    var tree = new ChoiceTree { AnchorSegmentId = anchor.Id };
    var path = PathFor("choice-draft.jumu");
    ProjectStorage.Save(path, new ProjectDocument { SourceText = "起点", Segments = [anchor], ChoiceTrees = [tree] });
    Equal(0, ProjectStorage.Load(path).ChoiceTrees.Single().Branches.Count);
    Contains("添加选项", UserError(() => RenpyExporter.Generate([anchor], new ExportOptions(), [tree])).Message);
    tree.Branches.Add(new ChoiceBranch());
    Contains("名称为空", UserError(() => RenpyExporter.Generate([anchor], new ExportOptions(), [tree])).Message);
});
Test("快捷选项：同一插入点合并为一个菜单，未选中时接在最后一句后", () =>
{
    var first = Narration("第一句");
    var last = Narration("最后一句");
    var project = new ProjectDocument { SourceText = "第一句。最后一句。", Segments = [first, last] };
    var opening = ChoiceTreeEditor.AddQuickChoice(project, "开门", first.Id, ChoicePlacement.Before);
    var leave = ChoiceTreeEditor.AddQuickChoice(project, "离开", first.Id, ChoicePlacement.Before);
    True(opening.CreatedTree && !leave.CreatedTree, "同一位置再次添加应复用已有菜单");
    True(ReferenceEquals(opening.Tree, leave.Tree), "两个选项应在同一菜单中");
    Equal(2, opening.Tree.Branches.Count);
    var ending = ChoiceTreeEditor.AddQuickChoice(project, "继续");
    Equal(last.Id, ending.Tree.AnchorSegmentId);
    Equal(ChoicePlacement.After, ending.Tree.Placement);
    Equal(2, project.ChoiceTrees.Count);
    var script = RenpyExporter.Generate(project.Segments, project.Export, project.ChoiceTrees);
    Equal(2, script.Split("menu:", StringSplitOptions.None).Length - 1);
    InOrder(script, "开门", "离开", "第一句", "最后一句", "继续");
    UserError(() => ChoiceTreeEditor.AddQuickChoice(project, " "));
    Equal(2, project.ChoiceTrees.Count);
});
Test("选项树编辑：顶层树拖到末尾会更新锚点并保留导出顺序", () =>
{
    var first = Narration("开头");
    var last = Narration("结尾");
    var project = new ProjectDocument { SourceText = "开头。结尾。", Segments = [first, last] };
    var early = ChoiceTreeEditor.AddQuickChoice(project, "提前决定", first.Id).Tree;
    var late = ChoiceTreeEditor.AddQuickChoice(project, "最后决定", last.Id, ChoicePlacement.Before).Tree;
    True(ChoiceTreeEditor.MoveTreeToEnd(project, early.Id), "跨锚点移动应更改位置");
    Equal(last.Id, early.AnchorSegmentId);
    Equal(ChoicePlacement.Before, early.Placement);
    Equal(early.Id, project.ChoiceTrees[^1].Id);
    var path = PathFor("choice-move-end.jumu");
    ProjectStorage.Save(path, project);
    var loaded = ProjectStorage.Load(path);
    Equal(early.Id, loaded.ChoiceTrees[^1].Id);
    InOrder(RenpyExporter.Generate(loaded.Segments, loaded.Export, loaded.ChoiceTrees),
        "开头", "最后决定", "提前决定", "结尾");
    True(!ChoiceTreeEditor.MoveTreeToEnd(project, early.Id), "已经在末尾的树再次移动应是空操作");
});
Test("选项树编辑：本来就是剧情末尾的树不会误移回较早锚点", () =>
{
    var first = Narration("开头");
    var last = Narration("结尾");
    var project = new ProjectDocument { SourceText = "开头。结尾。", Segments = [first, last] };
    var later = ChoiceTreeEditor.AddQuickChoice(project, "后面的菜单", last.Id).Tree;
    ChoiceTreeEditor.AddQuickChoice(project, "前面的菜单", first.Id);
    True(ChoiceTreeEditor.MoveTreeToEnd(project, later.Id), "应修正列表顺序");
    Equal(last.Id, later.AnchorSegmentId);
    InOrder(RenpyExporter.Generate(project.Segments, project.Export, project.ChoiceTrees),
        "开头", "前面的菜单", "结尾", "后面的菜单");
});
Test("选项树编辑：同层选项可移到末尾或删除，最后一个删除时清理空菜单", () =>
{
    var anchor = Narration("起点");
    var project = new ProjectDocument { SourceText = "起点", Segments = [anchor] };
    var a = ChoiceTreeEditor.AddQuickChoice(project, "甲").Branch;
    var b = ChoiceTreeEditor.AddQuickChoice(project, "乙").Branch;
    var c = ChoiceTreeEditor.AddQuickChoice(project, "丙").Branch;
    True(ChoiceTreeEditor.MoveBranchToEnd(project, a.Id), "首项应能移到末尾");
    Equal("乙,丙,甲", string.Join(',', project.ChoiceTrees[0].Branches.Select(x => x.Label)));
    InOrder(RenpyExporter.Generate(project.Segments, project.Export, project.ChoiceTrees), "乙", "丙", "甲");
    ChoiceTreeEditor.DeleteBranch(project, c.Id);
    Equal("乙,甲", string.Join(',', project.ChoiceTrees[0].Branches.Select(x => x.Label)));
    ChoiceTreeEditor.DeleteBranch(project, b.Id);
    ChoiceTreeEditor.DeleteBranch(project, a.Id);
    Equal(0, project.ChoiceTrees.Count);
    True(!RenpyExporter.Generate(project.Segments, project.Export, project.ChoiceTrees).Contains("menu:", StringComparison.Ordinal), "删除最后选项后不应留下空菜单");
});
Test("选项树编辑：顶层选项拖到整棵树末尾，形成后续菜单并可删除", () =>
{
    var first = Narration("开头");
    var last = Narration("结尾");
    var project = new ProjectDocument { SourceText = "开头。结尾。", Segments = [first, last] };
    var moved = ChoiceTreeEditor.AddQuickChoice(project, "追加选项", first.Id).Branch;
    moved.Items.Add(new ChoiceItem { Segment = Narration("追加内容") });
    var kept = ChoiceTreeEditor.AddQuickChoice(project, "保留选项", first.Id).Branch;
    var finalTree = ChoiceTreeEditor.AddQuickChoice(project, "原末尾选项", last.Id).Tree;
    var outcome = ChoiceTreeEditor.MoveBranchAfterLastTree(project, moved.Id);
    Equal(moved.Id, outcome.Branch.Id);
    True(ReferenceEquals(moved, outcome.Tree.Branches.Single()), "移动应保留选项及其分支内容");
    Equal(finalTree.AnchorSegmentId, outcome.Tree.AnchorSegmentId);
    Equal(finalTree.Placement, outcome.Tree.Placement);
    Equal(3, project.ChoiceTrees.Count);
    var path = PathFor("choice-branch-after-tree.jumu");
    ProjectStorage.Save(path, project);
    var loaded = ProjectStorage.Load(path);
    InOrder(RenpyExporter.Generate(loaded.Segments, loaded.Export, loaded.ChoiceTrees),
        "开头", "保留选项", "结尾", "原末尾选项", "追加选项", "追加内容");
    ChoiceTreeEditor.DeleteTree(project, outcome.Tree.Id);
    Equal(2, project.ChoiceTrees.Count);
    True(!RenpyExporter.Generate(project.Segments, project.Export, project.ChoiceTrees).Contains("追加选项", StringComparison.Ordinal), "删除新菜单应移除整个后续分支");
    Equal(kept.Id, project.ChoiceTrees[0].Branches.Single().Id);
});
Test("选项树编辑：从唯一选项拆出后不会留下无法导出的空菜单，嵌套项不可误拆", () =>
{
    var anchor = Narration("起点");
    var project = new ProjectDocument { SourceText = "起点", Segments = [anchor] };
    var branch = ChoiceTreeEditor.AddQuickChoice(project, "唯一选项").Branch;
    var result = ChoiceTreeEditor.MoveBranchAfterLastTree(project, branch.Id);
    True(result.RemovedEmptySourceTree, "唯一选项被拆出时应清理原菜单");
    Equal(1, project.ChoiceTrees.Count);
    Equal(1, RenpyExporter.Generate(project.Segments, project.Export, project.ChoiceTrees).Split("menu:", StringSplitOptions.None).Length - 1);
    var nested = new ChoiceTree { Branches = [new ChoiceBranch { Label = "内部选项" }] };
    result.Branch.Items.Add(new ChoiceItem { Tree = nested });
    Contains("顶层", UserError(() => ChoiceTreeEditor.MoveBranchAfterLastTree(project, nested.Branches.Single().Id)).Message);
    Equal(1, project.ChoiceTrees.Count);
});
Test("选项树编辑：唯一且最后的选项拆出时保留原剧情位置", () =>
{
    var first = Narration("开头");
    var last = Narration("结尾");
    var project = new ProjectDocument { SourceText = "开头。结尾。", Segments = [first, last] };
    ChoiceTreeEditor.AddQuickChoice(project, "较早选项", first.Id);
    var branch = ChoiceTreeEditor.AddQuickChoice(project, "末尾选项", last.Id).Branch;
    var moved = ChoiceTreeEditor.MoveBranchAfterLastTree(project, branch.Id);
    True(moved.RemovedEmptySourceTree, "原菜单应清理");
    Equal(last.Id, moved.Tree.AnchorSegmentId);
    InOrder(RenpyExporter.Generate(project.Segments, project.Export, project.ChoiceTrees),
        "开头", "较早选项", "结尾", "末尾选项");
});
Test("工程：人物快捷姓名和嵌套选项树完整保存读取", () =>
{
    var anchor = Narration("开场");
    var tree = new ChoiceTree
    {
        AnchorSegmentId = anchor.Id, Placement = ChoicePlacement.Before, Title = "问候",
        Branches = [new ChoiceBranch
        {
            Label = "打招呼", Items = [
                new ChoiceItem { Segment = new Segment { Kind = SegmentKind.Dialogue, Speaker = "林夏", Text = "你好。", Reviewed = true } },
                new ChoiceItem { Tree = new ChoiceTree { Branches = [new ChoiceBranch { Label = "继续" }] } }
            ]
        }]
    };
    var path = PathFor("choice-roundtrip.jumu");
    ProjectStorage.Save(path, new ProjectDocument
    {
        SourceText = "开场", Segments = [anchor], SpeakerShortcuts = ["林夏", "Alice"], ChoiceTrees = [tree]
    });
    var loaded = ProjectStorage.Load(path);
    Equal("Alice", loaded.SpeakerShortcuts[1]);
    Equal(ChoicePlacement.Before, loaded.ChoiceTrees.Single().Placement);
    Equal("林夏", loaded.ChoiceTrees.Single().Branches[0].Items[0].Segment?.Speaker);
    True(loaded.ChoiceTrees.Single().Branches[0].Items[0].Segment!.Reviewed, "分支内的逐句审核状态需保留");
    Equal("继续", loaded.ChoiceTrees.Single().Branches[0].Items[1].Tree?.Branches.Single().Label);
    Contains("打招呼", RenpyExporter.Generate(loaded.Segments, loaded.Export, loaded.ChoiceTrees));
});
Test("工程：旧版工程缺少新字段时仍可打开", () =>
{
    var path = PathFor("legacy-v1.jumu");
    File.WriteAllText(path, """
        {"Version":1,"SourceFile":"旧稿.txt","SourceText":"开场。",
         "Options":{"Mode":0,"Split":1,"MaxLength":80,"Aliases":{}},
         "Export":{"Label":"imported_story","DirectionAsComments":true,"CharacterVariables":{}},
         "Segments":[{"Id":"legacy","Paragraph":1,"Source":"开场。","Text":"开场。","Kind":0,"Speaker":"","Warnings":[],"Reviewed":true,"Include":true}]}
        """, new UTF8Encoding(false));
    var loaded = ProjectStorage.Load(path);
    Equal("开场。", loaded.Segments.Single().Text);
    Equal(0, loaded.SpeakerShortcuts.Count);
    Equal(0, loaded.ChoiceTrees.Count);
});
Test("工程：保存读取保留人工修正、选项与原文", () =>
{
    var path = PathFor("project.rnscribe");
    var project = new ProjectDocument
    {
        SourceFile = "稿件.docx", SourceText = "原文。", RequiresReparse = true,
        Options = new ParseOptions { Mode = ImportMode.Novel, Split = SplitMode.ReadingLength, MaxLength = 50 },
        Segments = [new Segment { Id = "fixed", Kind = SegmentKind.Dialogue, Speaker = "小明", Text = "修正后", Reviewed = true, Include = false, Warnings = ["待确认"] }]
    };
    ProjectStorage.Save(path, project);
    var loaded = ProjectStorage.Load(path);
    Equal("原文。", loaded.SourceText);
    True(loaded.RequiresReparse, "未解析状态需随工程保存");
    Equal(SplitMode.ReadingLength, loaded.Options.Split);
    Equal(50, loaded.Options.MaxLength);
    Equal("fixed", loaded.Segments.Single().Id);
    True(loaded.Segments.Single().Reviewed && !loaded.Segments.Single().Include, "保留人工确认和排除状态");
    Equal("待确认", loaded.Segments.Single().Warnings.Single());
});
Test("工程：再次保存保留备份，损坏文件可读报错", () =>
{
    var path = PathFor("save-backup.rnscribe");
    ProjectStorage.Save(path, new ProjectDocument { SourceText = "第一次" });
    ProjectStorage.Save(path, new ProjectDocument { SourceText = "第二次" });
    True(Directory.GetFiles(root, "save-backup.rnscribe.bak.*").Any(), "工程应保留备份");
    Equal("第二次", ProjectStorage.Load(path).SourceText);
    var corrupt = PathFor("bad-project.rnscribe");
    File.WriteAllText(corrupt, "{ incomplete");
    UserError(() => ProjectStorage.Load(corrupt));
});

Test("批量校正：只覆盖修改字段并立即反映在预览", () =>
{
    var first = new Segment { Kind = SegmentKind.Dialogue, Speaker = "林夏", Text = "旧句一", Warnings = ["待检查"] };
    var second = new Segment { Kind = SegmentKind.Dialogue, Speaker = "周远", Text = "旧句二" };
    var changed = new List<string>();
    first.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");
    Equal(2, SegmentBatchEditor.Apply([first, second], new SegmentEdit(Speaker: "阿青"), true));
    Equal("阿青", first.Speaker);
    Equal("阿青", second.Speaker);
    Equal("旧句一", first.Text);
    Equal("旧句二", second.Text);
    True(first.Reviewed && second.Reviewed, "所选条目都应确认");
    Equal("已确认", first.StatusLabel);
    True(changed.Contains(nameof(Segment.SpeakerLabel)) && changed.Contains(nameof(Segment.StatusLabel)), "列表绑定应收到名称与状态变更");
    Contains("阿青", RenpyExporter.Generate([first, second], new ExportOptions()));
});
Test("批量校正：正文修改作用于全部选中项", () =>
{
    var first = Narration("旧一");
    var second = Narration("旧二");
    var changed = new List<string>();
    first.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");
    SegmentBatchEditor.Apply([first, second], new SegmentEdit(Text: "新正文"), true);
    Equal("新正文", first.Preview);
    Equal("新正文", second.Preview);
    True(changed.Contains(nameof(Segment.Preview)), "列表预览应收到正文变更");
});
Test("批量校正：校验失败不应只修改部分条目", () =>
{
    var first = new Segment { Kind = SegmentKind.Dialogue, Speaker = "林夏", Text = "你好" };
    var second = new Segment { Kind = SegmentKind.Dialogue, Text = "再见" };
    UserError(() => SegmentBatchEditor.Apply([first, second], new SegmentEdit(Text: "统一正文"), true));
    Equal("你好", first.Text);
    Equal("再见", second.Text);
    True(!first.Reviewed && !second.Reviewed, "失败时不能局部确认");
});
var failed = 0;
try
{
    foreach (var test in tests)
    {
        try { test.Body(); Console.WriteLine("PASS " + test.Name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + test.Name + "\n     " + e.GetType().Name + ": " + e.Message); }
    }
    Console.WriteLine($"\n{tests.Count - failed}/{tests.Count} passed; {failed} failed.");
}
finally { Directory.Delete(root, recursive: true); }
if (failed == 0 && args.Length == 2 && args[0] == "--renpy-fixture")
{
    var game = Path.Combine(Path.GetFullPath(args[1]), "game"); Directory.CreateDirectory(game);
    var tricky = new List<Segment>
    {
        Narration("[player] {b}100% \\ \"quote\"\n换行与 〖注音〗  空格"),
        new() { Kind = SegmentKind.Dialogue, Speaker = "林夏", Text = "Don't worry, Dr. Smith. 3.14 at 18:30!" },
        new() { Kind = SegmentKind.Direction, Text = "（舞台说明）" }
    };
    tricky.AddRange(Parse("林夏说：“你确定？现在就走？”\n她回答：“好吧。”", ImportMode.Novel).Segments);
    var fixtureTree = new ChoiceTree
    {
        AnchorSegmentId = tricky[0].Id, Title = "要去哪里？ [player] {b} 100%",
        Branches = [
            new ChoiceBranch { Label = "去森林 \"现在\"", Items = [
                new ChoiceItem { Segment = Narration("在林间遇见 [player]。") },
                new ChoiceItem { Tree = new ChoiceTree { Branches = [
                    new ChoiceBranch { Label = "向左", Items = [new ChoiceItem { Segment = Narration("抵达小屋。") }] },
                    new ChoiceBranch { Label = "向右" }
                ] } }
            ] },
            new ChoiceBranch { Label = "留在原地" }
        ]
    };
    var script = RenpyExporter.Generate(tricky, new ExportOptions(), [fixtureTree]);
    File.WriteAllText(Path.Combine(game, "imported_story.rpy"), script, new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(game, "script.rpy"), "define config.name = \"Scribe export validation\"\nlabel start:\n    call imported_story\n    return\n", new UTF8Encoding(false));
    Console.WriteLine("Ren'Py fixture: " + game);
}
return failed == 0 ? 0 : 1;

static Segment Narration(string text) => new() { Kind = SegmentKind.Narration, Text = text };
static string WithoutWhitespace(string value) => string.Concat(value.Where(x => !char.IsWhiteSpace(x)));
static string WithoutOuterQuoteMarks(string value) => WithoutWhitespace(value).Replace("“", "").Replace("”", "");
static void True(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected <{expected}>, got <{actual}>");
}
static void Contains(string expected, string actual) => True(actual.Contains(expected, StringComparison.Ordinal), $"Missing <{expected}> in <{actual}>");
static void InOrder(string text, params string[] parts)
{
    var start = 0;
    foreach (var part in parts)
    {
        var index = text.IndexOf(part, start, StringComparison.Ordinal);
        True(index >= start, "内容缺失或顺序错误：" + part);
        start = index + part.Length;
    }
}
static UserFacingException UserError(Action action)
{
    try { action(); }
    catch (UserFacingException ex) { True(!string.IsNullOrWhiteSpace(ex.Message), "报错信息不能为空"); return ex; }
    throw new InvalidOperationException("Expected a user-readable UserFacingException");
}
static void WriteDocx(string path, string body)
{
    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    void Entry(string name, string value)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(value);
    }
    Entry("[Content_Types].xml", """
        <?xml version="1.0" encoding="UTF-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
        """);
    Entry("_rels/.rels", """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
        """);
    Entry("word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" + body + "</w:body></w:document>");
}
