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
    ManuscriptParser.Parse(text, new ParseOptions { Mode = mode, Split = split, AutoDetectKinds = true });

Test("默认旁白：小说和剧本保留姓名、引号与标点", () =>
{
    foreach (var mode in new[] { ImportMode.Novel, ImportMode.Script })
    {
        const string source = "林夏：\"你确定？\"\n她说：“我确定。”\n【场景：雨夜】";
        var result = ManuscriptParser.Parse(source, new ParseOptions { Mode = mode });
        True(result.Segments.Count >= 3, "应按句子拆分原稿");
        True(result.Segments.All(s => s.Kind == SegmentKind.Narration && s.Speaker.Length == 0), "所有结果应默认旁白");
        Equal(WithoutWhitespace(source), WithoutWhitespace(string.Concat(result.Segments.Select(s => s.Text))));
    }
});
Test("默认旁白：工程保存后保留分类设置", () =>
{
    var path = PathFor("narration-default.jumu");
    ProjectStorage.Save(path, new ProjectDocument { SourceText = "林夏：你好。" });
    var loaded = ProjectStorage.Load(path);
    True(!loaded.Options.AutoDetectKinds, "重新打开工程仍应采用默认旁白分类");
});

Test("逐句顺序：移动句子时前后选项树跟随，保存后仍可导出", () =>
{
    var first = Narration("第一句。");
    var second = Narration("第二句。");
    var third = Narration("第三句。");
    var project = new ProjectDocument { SourceText = "第一句。第二句。第三句。", Segments = [first, second, third] };
    ChoiceTreeEditor.AddQuickChoice(project, "句前选项", second.Id, ChoicePlacement.Before);
    ChoiceTreeEditor.AddQuickChoice(project, "句后选项", second.Id, ChoicePlacement.After);
    True(SegmentListEditor.Move(project, second.Id, 1), "中间句应能下移");
    Equal("第一句。,第三句。,第二句。", string.Join(',', project.Segments.Select(s => s.Text)));
    InOrder(RenpyExporter.Generate(project.Segments, project.Export, project.ChoiceTrees),
        "第一句。", "第三句。", "句前选项", "第二句。", "句后选项");
    var path = PathFor("reordered.jumu");
    ProjectStorage.Save(path, project);
    var loaded = ProjectStorage.Load(path);
    Equal(second.Id, loaded.Segments[^1].Id);
    Equal(2, loaded.ChoiceTrees.Count(tree => tree.AnchorSegmentId == second.Id));
    True(!SegmentListEditor.Move(loaded, second.Id, 1), "末句不能继续下移");
    True(SegmentListEditor.Move(loaded, second.Id, -1), "末句应能上移");
});

Test("逐句顺序：删除句子仅清理其关联选项树并保留原稿", () =>
{
    var first = Narration("第一句。");
    var second = Narration("第二句。");
    var third = Narration("第三句。");
    var project = new ProjectDocument { SourceText = "第一句。第二句。第三句。", Segments = [first, second, third] };
    ChoiceTreeEditor.AddQuickChoice(project, "删除的分支", second.Id);
    ChoiceTreeEditor.AddQuickChoice(project, "保留的分支", third.Id);
    Equal(1, SegmentListEditor.Delete(project, second.Id));
    Equal("第一句。第二句。第三句。", project.SourceText);
    Equal("第一句。,第三句。", string.Join(',', project.Segments.Select(s => s.Text)));
    Equal(1, project.ChoiceTrees.Count);
    Equal(third.Id, project.ChoiceTrees.Single().AnchorSegmentId);
    var script = RenpyExporter.Generate(project.Segments, project.Export, project.ChoiceTrees);
    True(!script.Contains("第二句。", StringComparison.Ordinal) && !script.Contains("删除的分支", StringComparison.Ordinal), "删除的句子与分支不应导出");
    InOrder(script, "第一句。", "第三句。", "保留的分支");
    UserError(() => SegmentListEditor.Delete(project, second.Id));
});

Test("批量删除：非连续正文及其菜单一次删除，原稿与其他正文保留", () =>
{
    var project = new ProjectDocument { SourceText = "原稿", Segments = [Narration("甲"), Narration("乙"), Narration("丙"), Narration("丁")] };
    var a = project.Segments[0]; var b = project.Segments[1]; var c = project.Segments[2];
    ChoiceTreeEditor.AddQuickChoice(project, "随甲删除", a.Id).Branch.TargetNodeId = c.Id;
    ChoiceTreeEditor.AddQuickChoice(project, "保留乙", b.Id);
    Equal(1, SegmentListEditor.DeleteMany(project, [a.Id, c.Id]));
    Equal("乙,丁", string.Join(',', project.Segments.Select(segment => segment.Text)));
    Equal(b.Id, project.ChoiceTrees.Single().AnchorSegmentId); Equal("原稿", project.SourceText);
    StoryGraphValidator.Validate(project.Segments, project.ChoiceTrees, project.Fragments);
});
Test("批量删除：外部引用、重复和失效选择均不发生部分删除", () =>
{
    var project = new ProjectDocument { Segments = [Narration("甲"), Narration("乙"), Narration("丙")] };
    var a = project.Segments[0]; var b = project.Segments[1]; var c = project.Segments[2];
    var branch = ChoiceTreeEditor.AddQuickChoice(project, "保留的外部引用", c.Id).Branch;
    branch.TargetNodeId = b.Id;
    var before = System.Text.Json.JsonSerializer.Serialize(project);
    UserError(() => SegmentListEditor.DeleteMany(project, [a.Id, b.Id]));
    UserError(() => SegmentListEditor.DeleteMany(project, [a.Id, "失效"]));
    UserError(() => SegmentListEditor.DeleteMany(project, [a.Id, a.Id]));
    UserError(() => SegmentListEditor.DeleteMany(project, []));
    Equal(before, System.Text.Json.JsonSerializer.Serialize(project));
    branch.TargetNodeId = "";
    var card = StoryFragmentEditor.AddText(project, "外部素材", "素材正文"); card.NextNodeId = b.Id;
    before = System.Text.Json.JsonSerializer.Serialize(project);
    UserError(() => SegmentListEditor.DeleteMany(project, [a.Id, b.Id]));
    Equal(before, System.Text.Json.JsonSerializer.Serialize(project));
});
Test("批量删除：允许删除全部正文，空主线工程仍可保存", () =>
{
    var project = new ProjectDocument { SourceText = "原稿", Segments = [Narration("甲"), Narration("乙")] };
    SegmentListEditor.DeleteMany(project, project.Segments.Select(segment => segment.Id).ToList());
    Equal(0, project.Segments.Count); Equal("原稿", project.SourceText);
    var path = PathFor("delete-all.jumu"); ProjectStorage.Save(path, project);
    Equal(0, ProjectStorage.Load(path).Segments.Count);
});
Test("素材多选删除：一起删除互相连接的素材，其他文本不变", () =>
{
    var project = new ProjectDocument { Segments = [Narration("主线")] };
    var a = StoryFragmentEditor.AddText(project, "甲", "甲正文");
    var b = StoryFragmentEditor.AddText(project, "乙", "乙正文");
    var c = StoryFragmentEditor.AddText(project, "丙", "丙正文");
    a.NextNodeId = b.Id; b.NextNodeId = a.Segments[0].Id;
    StoryFragmentEditor.DeleteMany(project, [a.Id, b.Id]);
    Equal(c.Id, project.Fragments.Single().Id); Equal("主线", project.Segments.Single().Text);
});
Test("素材多选删除：外部连接或失效选择阻止整个操作", () =>
{
    var project = new ProjectDocument { Segments = [Narration("主线")] };
    var a = StoryFragmentEditor.AddText(project, "甲", "甲正文");
    var b = StoryFragmentEditor.AddText(project, "乙", "乙正文");
    var c = StoryFragmentEditor.AddText(project, "丙", "丙正文");
    c.NextNodeId = b.Segments[0].Id;
    var before = System.Text.Json.JsonSerializer.Serialize(project);
    UserError(() => StoryFragmentEditor.DeleteMany(project, [a.Id, b.Id]));
    UserError(() => StoryFragmentEditor.DeleteMany(project, [a.Id, "失效"]));
    UserError(() => StoryFragmentEditor.DeleteMany(project, [a.Id, a.Id]));
    Equal(before, System.Text.Json.JsonSerializer.Serialize(project));
    c.NextNodeId = "";
    ChoiceTreeEditor.AddQuickChoice(project, "主线引用", project.Segments[0].Id).Branch.TargetNodeId = a.Id;
    before = System.Text.Json.JsonSerializer.Serialize(project);
    UserError(() => StoryFragmentEditor.DeleteMany(project, [a.Id, b.Id]));
    Equal(before, System.Text.Json.JsonSerializer.Serialize(project));
});
Test("折叠跳转目录：素材优先、正文作为整份文本，内部位置独立保留", () =>
{
    var project = new ProjectDocument { SourceFile = "第一章.txt", Segments = [Narration("开头"), Narration("结尾")] };
    var a = StoryFragmentEditor.AddText(project, "雨夜", "第一段\n第二段", SplitMode.Paragraph);
    var groups = StoryDestinationCatalog.Build(project);
    Equal(2, groups.Count); Equal(a.Id, groups[0].Whole.Id);
    Equal(project.Segments[0].Id, groups[1].Whole.Id);
    True(groups[1].Whole.Name.Contains("当前修改文本", StringComparison.Ordinal), "正文应作为可折叠整块文本");
    Equal(a.Segments[1].Id, groups[0].TextPositions[1].Id);
    Equal(project.Segments[1].Id, groups[1].TextPositions[1].Id);
    ChoiceTreeEditor.AddQuickChoice(project, "去整份雨夜文本", project.Segments[0].Id).Branch.TargetNodeId = groups[0].Whole.Id;
    InOrder(RenpyExporter.Generate(project.Segments, project.Export, project.ChoiceTrees, project.Fragments), "jump jumu_node_", "第一段", "第二段");
});
Test("折叠跳转目录：正在修改的素材优先、整份正文跳转可生成 label", () =>
{
    var project = new ProjectDocument { Segments = [Narration("开头"), Narration("后文")] };
    var a = StoryFragmentEditor.AddText(project, "甲", "甲正文");
    var b = StoryFragmentEditor.AddText(project, "乙", "乙正文");
    var groups = StoryDestinationCatalog.Build(project, b.Id);
    Equal(b.Id, groups[0].Whole.Id); Equal(a.Id, groups[1].Whole.Id);
    ChoiceTreeEditor.AddQuickChoice(project, "重读正文", project.Segments[1].Id).Branch.TargetNodeId = groups[^1].Whole.Id;
    InOrder(RenpyExporter.Generate(project.Segments, project.Export, project.ChoiceTrees, project.Fragments), "label jumu_node_", "开头", "后文", "jump jumu_node_");
    project.Segments.Clear(); project.ChoiceTrees.Clear();
    Equal(2, StoryDestinationCatalog.Build(project).Count);
});

Test("素材袋：新建长文本只生成一张卡片，默认旁白且可保存取出", () =>
{
    var project = new ProjectDocument();
    const string text = "林夏：我们出发吧。\n雨落在窗前。她关上灯，沿着走廊往前走。\n第二天，故事继续。";
    var card = StoryFragmentEditor.AddText(project, "第一章", text);
    Equal(1, project.Fragments.Count);
    True(card.Segments.Count > 1, "内部播放块可拆分，外部仍只是一张文本卡片");
    True(card.Segments.All(segment => segment.Kind == SegmentKind.Narration && segment.Speaker == ""), "添加文本默认旁白");
    Equal(WithoutWhitespace(text), WithoutWhitespace(string.Concat(card.Segments.Select(segment => segment.Text))));
    var path = PathFor("whole-text.jumu"); ProjectStorage.Save(path, project);
    var loaded = ProjectStorage.Load(path);
    StoryFragmentEditor.Restore(loaded, card.Id, 0);
    Equal(0, loaded.Fragments.Count);
    Equal(card.Segments.Count, loaded.Segments.Count);
    True(RenpyExporter.Generate(loaded.Segments, loaded.Export).Contains("第二天", StringComparison.Ordinal), "整份取出后可导出");
});
Test("素材袋：整篇已校正正文可收纳，保留全部设置和选项树", () =>
{
    var dialogue = new Segment { Text = "已经修改的对白。", Source = "原对白", Kind = SegmentKind.Dialogue, Speaker = "林夏", Reviewed = true };
    var ending = Narration("已修改结尾。"); ending.Include = false;
    var project = new ProjectDocument { SourceText = "原稿保持不变", Segments = [dialogue, ending] };
    var branch = ChoiceTreeEditor.AddQuickChoice(project, "结尾", dialogue.Id).Branch;
    branch.TargetNodeId = ending.Id;
    var card = StoryFragmentEditor.StashMain(project, "校正版第一章");
    Equal(0, project.Segments.Count); Equal(1, project.Fragments.Count); Equal(2, card.Segments.Count);
    True(ReferenceEquals(dialogue, card.Segments[0]) && dialogue.Reviewed, "校正字段与身份应保留");
    True(!card.Segments[1].Include, "排除设置应保留");
    Equal(ending.Id, card.ChoiceTrees[0].Branches[0].TargetNodeId);
    var path = PathFor("stash-all.jumu"); ProjectStorage.Save(path, project);
    StoryFragmentEditor.Restore(project, card.Id, 0);
    Equal("林夏", project.Segments[0].Speaker); Equal("原稿保持不变", project.SourceText);
});
Test("素材袋：完整文本编辑保留角色、句子身份及跳转锚点", () =>
{
    var project = new ProjectDocument { Segments = [Narration("入口")] };
    var card = StoryFragmentEditor.AddText(project, "路线", "开始。\n林夏的对白。\n结束。", SplitMode.Paragraph);
    var dialogue = card.Segments[1]; dialogue.Kind = SegmentKind.Dialogue; dialogue.Speaker = "林夏"; dialogue.Reviewed = true;
    ChoiceTreeEditor.AddQuickChoice(project, "跳转", project.Segments[0].Id).Branch.TargetNodeId = dialogue.Id;
    var first = card.Segments[0];
    StoryFragmentEditor.UpdateText(project, card, "开始。\n\n改过的对白。\n\n结束。");
    True(ReferenceEquals(dialogue, card.Segments[1]), "修改应保留链接目标对象");
    Equal("林夏", dialogue.Speaker); True(!dialogue.Reviewed && ReferenceEquals(first, card.Segments[0]), "只有改写的块重置确认");
    Equal(dialogue.Id, project.ChoiceTrees[0].Branches[0].TargetNodeId);
    StoryGraphValidator.Validate(project.Segments, project.ChoiceTrees, project.Fragments);
});
Test("素材袋：完整文本增添正文保留旧块，不能误删除关联位置", () =>
{
    var project = new ProjectDocument { Segments = [Narration("入口")] };
    var card = StoryFragmentEditor.AddText(project, "路线", "甲\n乙\n丙", SplitMode.Paragraph);
    var middle = card.Segments[1]; var ending = card.Segments[2];
    ChoiceTreeEditor.AddQuickChoice(project, "跳转", project.Segments[0].Id).Branch.TargetNodeId = middle.Id;
    StoryFragmentEditor.UpdateText(project, card, "新开头\n\n甲\n\n乙\n\n丙");
    True(card.Segments.Contains(middle) && card.Segments.Contains(ending), "添加新块时已有身份保留");
    var before = System.Text.Json.JsonSerializer.Serialize(project);
    UserError(() => StoryFragmentEditor.UpdateText(project, card, "新开头\n\n甲\n\n丙"));
    Equal(before, System.Text.Json.JsonSerializer.Serialize(project));
    UserError(() => StoryFragmentEditor.UpdateText(project, card, ""));
    UserError(() => StoryFragmentEditor.AddText(project, "空文本", "  "));
    Equal(before, System.Text.Json.JsonSerializer.Serialize(project));
});
Test("素材袋：改写不得移除承载选项树的块", () =>
{
    var project = new ProjectDocument();
    var card = StoryFragmentEditor.AddText(project, "路线", "甲\n乙\n丙", SplitMode.Paragraph);
    var tree = new ChoiceTree { AnchorSegmentId = card.Segments[1].Id, Branches = [new ChoiceBranch { Label = "继续" }] };
    card.ChoiceTrees.Add(tree);
    var before = System.Text.Json.JsonSerializer.Serialize(project);
    UserError(() => StoryFragmentEditor.UpdateText(project, card, "甲\n\n丙"));
    Equal(before, System.Text.Json.JsonSerializer.Serialize(project));
    StoryFragmentEditor.UpdateText(project, card, "甲\n\n改写乙\n\n丙");
    Equal(card.Segments[1].Id, tree.AnchorSegmentId);
});

Test("素材袋：收纳连续文本时保留身份、角色、选项树与后续连接", () =>
{
    var opening = Narration("入口。");
    var dialogue = new Segment { Text = "你好。", Speaker = "林夏", Kind = SegmentKind.Dialogue, Reviewed = true };
    var scene = Narration("雨中走廊。");
    var ending = Narration("共同结局。");
    var project = new ProjectDocument { SourceText = "原始稿件", Segments = [opening, dialogue, scene, ending] };
    var attached = ChoiceTreeEditor.AddQuickChoice(project, "继续", dialogue.Id).Tree;
    var fragment = StoryFragmentEditor.Stash(project, [dialogue.Id, scene.Id], "雨夜片段");
    Equal("原始稿件", project.SourceText);
    Equal(2, project.Segments.Count);
    True(ReferenceEquals(dialogue, fragment.Segments[0]), "收纳应保留原句及其已确认字段");
    Equal("林夏", fragment.Segments[0].Speaker);
    True(fragment.Segments[0].Reviewed, "校正状态应保留");
    Equal(attached.Id, fragment.ChoiceTrees.Single().Id);
    Equal(ending.Id, fragment.NextNodeId);
    Equal(0, project.ChoiceTrees.Count);
});
Test("素材袋：非连续选择或收纳全部正文报错且不改变工程", () =>
{
    var project = new ProjectDocument { Segments = [Narration("甲"), Narration("乙"), Narration("丙")] };
    var before = System.Text.Json.JsonSerializer.Serialize(project);
    UserError(() => StoryFragmentEditor.Stash(project, [project.Segments[0].Id, project.Segments[2].Id]));
    UserError(() => StoryFragmentEditor.Stash(project, project.Segments.Select(segment => segment.Id).ToList()));
    UserError(() => StoryFragmentEditor.Stash(project, ["不存在"]));
    Equal(before, System.Text.Json.JsonSerializer.Serialize(project));
});
Test("素材袋：取回正文后跳转目标仍指向原片段第一句，顺序及菜单恢复", () =>
{
    var project = new ProjectDocument { Segments = [Narration("入口"), Narration("片段甲"), Narration("片段乙"), Narration("结尾")] };
    var firstId = project.Segments[1].Id;
    ChoiceTreeEditor.AddQuickChoice(project, "片段内选择", firstId);
    var fragment = StoryFragmentEditor.Stash(project, [firstId, project.Segments[2].Id]);
    var entry = ChoiceTreeEditor.AddQuickChoice(project, "去片段", project.Segments[0].Id).Branch;
    entry.TargetNodeId = fragment.Id;
    StoryFragmentEditor.Restore(project, fragment.Id, 1);
    Equal(firstId, entry.TargetNodeId);
    Equal("入口,片段甲,片段乙,结尾", string.Join(',', project.Segments.Select(segment => segment.Text)));
    Equal(0, project.Fragments.Count);
    Equal(2, project.ChoiceTrees.Count);
    StoryGraphValidator.Validate(project.Segments, project.ChoiceTrees, project.Fragments, true);
});
Test("剧情跳转：正文与素材袋目标生成独立 label，片段结束继续原后续", () =>
{
    var opening = Narration("主线入口");
    var route = new Segment { Text = "雨夜对白", Speaker = "林夏", Kind = SegmentKind.Dialogue };
    var ending = Narration("共同结局");
    var project = new ProjectDocument { Segments = [opening, route, ending] };
    var fragment = StoryFragmentEditor.Stash(project, [route.Id], "雨夜");
    var choice = ChoiceTreeEditor.AddQuickChoice(project, "进入雨夜", opening.Id).Branch;
    choice.TargetNodeId = fragment.Id;
    var skip = ChoiceTreeEditor.AddQuickChoice(project, "直接结局", opening.Id).Branch;
    skip.TargetNodeId = ending.Id;
    var script = RenpyExporter.Generate(project.Segments, new ExportOptions { CreateStartLabel = true }, project.ChoiceTrees, project.Fragments);
    Contains("define 林夏 = Character(\"林夏\")", script);
    Equal(3, script.Split("jump jumu_node_", StringSplitOptions.None).Length - 1);
    InOrder(script, "主线入口", "进入雨夜", "jump jumu_node_", "共同结局", "return", "雨夜对白", "jump jumu_node_");
    var jumpLabels = script.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("jump ")).Select(line => line[5..]).ToList();
    foreach (var label in jumpLabels) Equal(1, script.Split("label " + label + ":", StringSplitOptions.None).Length - 1);
});
Test("剧情跳转：目标丢失拒绝保存与导出，目标句子不可直接删除", () =>
{
    var project = new ProjectDocument { Segments = [Narration("入口"), Narration("目标")] };
    var branch = ChoiceTreeEditor.AddQuickChoice(project, "去目标", project.Segments[0].Id).Branch;
    branch.TargetNodeId = project.Segments[1].Id;
    UserError(() => SegmentListEditor.Delete(project, project.Segments[1].Id));
    Equal(2, project.Segments.Count);
    branch.TargetNodeId = "失效的标识";
    UserError(() => ProjectStorage.Save(PathFor("missing-jump.jumu"), project));
    UserError(() => RenpyExporter.Generate(project.Segments, project.Export, project.ChoiceTrees));
});
Test("素材袋：保存读取恢复片段、跳转及片段内选项，旧工程仍可打开", () =>
{
    var project = new ProjectDocument { Segments = [Narration("开场"), Narration("分支"), Narration("收尾")] };
    ChoiceTreeEditor.AddQuickChoice(project, "分支选项", project.Segments[1].Id);
    var fragment = StoryFragmentEditor.Stash(project, [project.Segments[1].Id]);
    ChoiceTreeEditor.AddQuickChoice(project, "进入分支", project.Segments[0].Id).Branch.TargetNodeId = fragment.Id;
    var path = PathFor("fragment-project.jumu"); ProjectStorage.Save(path, project);
    var loaded = ProjectStorage.Load(path);
    Equal(2, loaded.Version); Equal(fragment.Id, loaded.Fragments[0].Id);
    Equal(fragment.Id, loaded.ChoiceTrees[0].Branches[0].TargetNodeId);
    Equal(fragment.NextNodeId, loaded.Fragments[0].NextNodeId);
    Equal(1, loaded.Fragments[0].ChoiceTrees.Count);
    var old = PathFor("v1-project.jumu");
    File.WriteAllText(old, "{\"Version\":1,\"SourceText\":\"\",\"Options\":{},\"Export\":{},\"Segments\":[]}");
    Equal(0, ProjectStorage.Load(old).Fragments.Count);
});
Test("剧情跳转：重新解析后只清除失效连接，保留素材袋内的有效连接", () =>
{
    var project = new ProjectDocument { Segments = [Narration("开场"), Narration("分支"), Narration("后续")] };
    var fragment = StoryFragmentEditor.Stash(project, [project.Segments[1].Id]);
    var tree = new ChoiceTree { AnchorSegmentId = fragment.Segments[0].Id, Branches = [new ChoiceBranch { Label = "重复此段", TargetNodeId = fragment.Id }] };
    fragment.ChoiceTrees.Add(tree);
    project.Segments = [Narration("新主线")];
    Equal(1, StoryFragmentEditor.ClearMissingTargets(project));
    Equal("", fragment.NextNodeId); Equal(fragment.Id, tree.Branches[0].TargetNodeId);
    StoryGraphValidator.Validate(project.Segments, project.ChoiceTrees, project.Fragments);
});

Test("素材袋：未连接的草稿不导出，跨片段的后续连接可正确导出", () =>
{
    var main = Narration("开场");
    var a = new StoryFragment { Title = "路线甲", Segments = [Narration("甲路线")] };
    var b = new StoryFragment { Title = "路线乙", Segments = [Narration("乙路线")] };
    a.NextNodeId = b.Id;
    var unused = new StoryFragment { Title = "未使用草稿", Segments = [Narration("不应导出的草稿")] };
    unused.ChoiceTrees.Add(new ChoiceTree { AnchorSegmentId = unused.Segments[0].Id });
    var tree = new ChoiceTree { AnchorSegmentId = main.Id, Branches = [new ChoiceBranch { Label = "出发", TargetNodeId = a.Id }] };
    var script = RenpyExporter.Generate([main], new ExportOptions(), [tree], [a, b, unused]);
    InOrder(script, "甲路线", "jump jumu_node_", "乙路线");
    True(!script.Contains("不应导出的草稿", StringComparison.Ordinal), "未使用素材不应进入导出脚本");
});
Test("素材袋：删除被引用片段被阻止，解除连接后可以删除", () =>
{
    var project = new ProjectDocument { Segments = [Narration("入口"), Narration("路线"), Narration("结尾")] };
    var fragment = StoryFragmentEditor.Stash(project, [project.Segments[1].Id]);
    var branch = ChoiceTreeEditor.AddQuickChoice(project, "出发", project.Segments[0].Id).Branch;
    branch.TargetNodeId = fragment.Segments[0].Id;
    UserError(() => StoryFragmentEditor.Delete(project, fragment.Id));
    Equal(1, project.Fragments.Count);
    branch.TargetNodeId = ""; StoryFragmentEditor.Delete(project, fragment.Id);
    Equal(0, project.Fragments.Count);
});

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
        AutoDetectKinds = true,
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
Test("导出：自动生成对话人代号，旁白使用 s，手动映射优先", () =>
{
    var item = new Segment { Kind = SegmentKind.Dialogue, Speaker = "小明", Text = "你好。" };
    var script = RenpyExporter.Generate([Narration("开场。"), item,
        new Segment { Kind = SegmentKind.Dialogue, Speaker = "小明", Text = "再说一句。" }], new ExportOptions());
    Contains("define s = Character(None)", script);
    Contains("define 小明 = Character(\"小明\")", script);
    Contains("s \"开场。\"", script);
    Contains("小明 \"你好。\"", script);
    Equal(1, script.Split("define 小明 = Character", StringSplitOptions.None).Length - 1);
    var mapped = RenpyExporter.Generate([Narration("旁白。"), item], new ExportOptions
    {
        CharacterVariables = new Dictionary<string, string> { ["小明"] = "ming" }
    });
    Contains("ming \"你好。\"", mapped);
    True(!mapped.Contains("define 小明 =", StringComparison.Ordinal), "已有角色变量不应被重复定义");
    UserError(() => RenpyExporter.Generate([item], new ExportOptions
    {
        CharacterVariables = new Dictionary<string, string> { ["小明"] = "s" }
    }));
});
Test("导出：复杂姓名安全转为代号，选项分支中的人物也自动定义", () =>
{
    var anchor = Narration("抉择。");
    var tree = new ChoiceTree { AnchorSegmentId = anchor.Id, Branches = [
        new ChoiceBranch { Label = "继续", Items = [
            new ChoiceItem { Segment = new Segment { Kind = SegmentKind.Dialogue, Speaker = "Dr. Smith", Text = "Hello." } },
            new ChoiceItem { Segment = new Segment { Kind = SegmentKind.Dialogue, Speaker = "s", Text = "I am s." } },
            new ChoiceItem { Segment = new Segment { Kind = SegmentKind.Dialogue, Speaker = "return", Text = "Reserved." } }
        ] }
    ] };
    var script = RenpyExporter.Generate([anchor], new ExportOptions(), [tree]);
    Contains("define Dr_Smith = Character(\"Dr.\\ Smith\")", script);
    Contains("define s_2 = Character(\"s\")", script);
    Contains("define return_2 = Character(\"return\")", script);
    Contains("Dr_Smith \"Hello.\"", script);
    Contains("s_2 \"I\\ am\\ s.\"", script);
    Contains("return_2 \"Reserved.\"", script);
});
Test("导出：未确定说话人的对白用 s 且继续提示人工确认", () =>
{
    var script = RenpyExporter.Generate([new Segment { Kind = SegmentKind.Dialogue, Text = "是谁？", Paragraph = 3 }], new ExportOptions());
    Contains("# 待确认：原文第 3 段的说话人未确定。", script);
    Contains("s \"是谁？\"", script);
});
Test("导出：可为新项目生成唯一的 start 入口", () =>
{
    var options = new ExportOptions { CreateStartLabel = true };
    var script = RenpyExporter.Generate([Narration("开场。")], options);
    InOrder(script, "label start:\n    call imported_story\n    return", "label imported_story:", "s \"开场。\"");
    Equal(1, script.Split("label start:", StringSplitOptions.None).Length - 1);
    var direct = RenpyExporter.Generate([Narration("开场。")], new ExportOptions { Label = "start", CreateStartLabel = true });
    Equal(1, direct.Split("label start:", StringSplitOptions.None).Length - 1);
    True(!direct.Contains("call start", StringComparison.Ordinal), "入口本身是 start 时不应调用自己");
    var path = PathFor("start-setting.jumu");
    ProjectStorage.Save(path, new ProjectDocument { SourceText = "开场。", Segments = [Narration("开场。")], Export = options });
    True(ProjectStorage.Load(path).Export.CreateStartLabel, "工程应保留启动入口设置");
});
Test("导出：检查 game 文件夹中已有的 start，忽略注释和即将覆盖的文件", () =>
{
    var game = PathFor("inspect-game");
    var nested = Path.Combine(game, "chapter");
    Directory.CreateDirectory(nested);
    var target = Path.Combine(game, "imported_story.rpy");
    File.WriteAllText(target, "label start:\n    return\n");
    File.WriteAllText(Path.Combine(game, "comment.rpy"), "# label start:\nlabel another:\n    return\n");
    Equal<string?>(null, RenpyProjectInspector.FindStartLabel(target));
    var existing = Path.Combine(nested, "script.rpy");
    File.WriteAllText(existing, "label start:\n    return\n");
    Equal(existing, RenpyProjectInspector.FindStartLabel(target));
});
Test("导出：角色代号冲突自动编号，恶意姓名不能插入脚本", () =>
{
    var script = RenpyExporter.Generate([
        new Segment { Kind = SegmentKind.Dialogue, Speaker = "Dr. Smith", Text = "A" },
        new Segment { Kind = SegmentKind.Dialogue, Speaker = "Dr-Smith", Text = "B" },
        new Segment { Kind = SegmentKind.Dialogue, Speaker = "narrator", Text = "C" },
        new Segment { Kind = SegmentKind.Dialogue, Speaker = "Eve\")\nlabel injected:\n    $ malicious()", Text = "D" }
    ], new ExportOptions());
    Contains("define Dr_Smith =", script);
    Contains("define Dr_Smith_2 =", script);
    Contains("define narrator_2 =", script);
    True(!script.Split('\n').Any(line => line.StartsWith("label injected:", StringComparison.Ordinal)), "角色名不能插入 label");
    Contains("Eve_", script);
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
    True(loaded.Options.AutoDetectKinds, "旧工程应保留原先的自动识别行为");
});
Test("工程：保存读取保留人工修正、选项与原文", () =>
{
    var path = PathFor("project.rnscribe");
    var project = new ProjectDocument
    {
        SourceFile = "稿件.docx", SourceText = "原文。", RequiresReparse = true,
        Options = new ParseOptions { Mode = ImportMode.Novel, Split = SplitMode.ReadingLength, MaxLength = 50, AutoDetectKinds = true },
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
    var standalone = Path.Combine(Path.GetFullPath(args[1]), "standalone", "game");
    Directory.CreateDirectory(standalone);
    File.WriteAllText(Path.Combine(standalone, "imported_story.rpy"),
        RenpyExporter.Generate(tricky, new ExportOptions { CreateStartLabel = true }, [fixtureTree]), new UTF8Encoding(false));
    Console.WriteLine("Ren'Py start fixture: " + Path.GetDirectoryName(standalone));
    var graphGame = Path.Combine(Path.GetFullPath(args[1]), "graph", "game");
    Directory.CreateDirectory(graphGame);
    var opening = Narration("入口：雨夜选择。");
    var rain = new Segment { Text = "雨夜，你终于来了。", Speaker = "林夏", Kind = SegmentKind.Dialogue, Reviewed = true };
    var rainEnd = Narration("雨停前，我们做出决定。");
    var ending = Narration("共同结局：天亮了。");
    var graph = new ProjectDocument
    {
        SourceText = "入口：雨夜选择。\n雨夜，你终于来了。\n雨停前，我们做出决定。\n共同结局：天亮了。",
        Segments = [opening, rain, rainEnd, ending], Export = new ExportOptions { CreateStartLabel = true }
    };
    var rainFragment = StoryFragmentEditor.Stash(graph, [rain.Id, rainEnd.Id], "雨夜路线");
    var echo = new StoryFragment { Title = "回声路线", Segments = [Narration("回声路线：另一条分支。")] , NextNodeId = ending.Id };
    graph.Fragments.Add(echo);
    ChoiceTreeEditor.AddQuickChoice(graph, "进入雨夜", opening.Id).Branch.TargetNodeId = rainFragment.Id;
    ChoiceTreeEditor.AddQuickChoice(graph, "直接结局", opening.Id).Branch.TargetNodeId = ending.Id;
    ChoiceTreeEditor.AddQuickChoice(graph, "重新开始", opening.Id).Branch.TargetNodeId = opening.Id;
    rainFragment.ChoiceTrees.Add(new ChoiceTree
    {
        AnchorSegmentId = rainEnd.Id,
        Branches = [new ChoiceBranch { Label = "前往回声", TargetNodeId = echo.Id }, new ChoiceBranch { Label = "回到主线", TargetNodeId = ending.Id }]
    });
    File.WriteAllText(Path.Combine(graphGame, "imported_story.rpy"),
        RenpyExporter.Generate(graph.Segments, graph.Export, graph.ChoiceTrees, graph.Fragments), new UTF8Encoding(false));
    ProjectStorage.Save(Path.Combine(Path.GetFullPath(args[1]), "graph-project.jumu"), graph);
    foreach (var harness in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "RenpyGraph"), "*.rpy"))
        File.Copy(harness, Path.Combine(graphGame, Path.GetFileName(harness)), overwrite: true);
    Console.WriteLine("Ren'Py graph fixture: " + Path.GetDirectoryName(graphGame));
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
