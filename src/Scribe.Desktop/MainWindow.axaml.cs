using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Scribe.Core;

namespace Scribe.Desktop;

public partial class MainWindow : Window
{
    private const string ShortcutPrompt = "选择已有姓名…";
    private ProjectDocument _project = new();
    private Segment? _selected;
    private List<Segment> _editorTargets = [];
    private SegmentEditSnapshot _editorSnapshot;
    private int _dragAnchor = -1;
    private Point _dragStart;
    private bool _dragSelecting;
    private HashSet<Segment> _dragBase = [];
    private readonly Stack<string> _undo = new();
    private bool _loading, _refreshing, _dirty, _manualEdits, _sourceStale, _closingAllowed, _busy;
    private string? _projectPath;
    private T C<T>(string name) where T : Control => this.FindControl<T>(name)!;
    private TextBox Source => C<TextBox>("SourceBox");
    private ListBox Rows => C<ListBox>("SegmentList");

    public MainWindow()
    {
        InitializeComponent();
        Rows.SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle;
        On("ImportButton", ImportAsync); On("SampleButton", LoadSampleAsync);
        On("ParseButton", ParseAsync); On("ExportButton", ExportAsync);
        On("SaveProjectButton", SaveAsync); On("OpenProjectButton", OpenProjectAsync);
        On("SettingsButton", SettingsAsync); On("HelpButton", HelpAsync);
        On("ChoiceTreeButton", () => OpenChoiceTreeAsync());
        On("QuickAddChoiceButton", QuickAddChoiceAsync);
        On("AddChoiceBeforeButton", () => OpenChoiceTreeAsync(ChoicePlacement.Before));
        On("AddChoiceAfterButton", () => OpenChoiceTreeAsync(ChoicePlacement.After));
        On("ApplyButton", () =>
        {
            var ids = _editorTargets.Select(s => s.Id).ToHashSet();
            var currentId = _selected?.Id;
            CommitEditor(true);
            Refresh(currentId, ids);
            return Task.CompletedTask;
        });
        On("SplitButton", SplitAsync); On("MergeButton", MergeAsync);
        On("UndoButton", UndoAsync);
        On("SelectVisibleButton", SelectVisibleAsync); On("ReviewSelectedButton", ReviewSelectedAsync);
        On("ClearSelectionButton", ClearSelectionAsync);
        Rows.SelectionChanged += (_, e) =>
        {
            if (_refreshing) return;
            CommitEditor();
            _selected = e.AddedItems.OfType<Segment>().LastOrDefault() ?? Rows.SelectedItems?.OfType<Segment>().LastOrDefault();
            ShowEditor(); UpdatePreview(); UpdateSelectionCount();
        };
        Rows.AddHandler(InputElement.PointerPressedEvent, DragPointerPressed, RoutingStrategies.Tunnel);
        Rows.AddHandler(InputElement.PointerMovedEvent, DragPointerMoved, RoutingStrategies.Tunnel);
        Rows.AddHandler(InputElement.PointerReleasedEvent, DragPointerReleased, RoutingStrategies.Tunnel);
        C<TextBox>("SearchBox").TextChanged += (_, _) => { if (!_loading) { CommitEditor(); Refresh(_selected?.Id); } };
        C<CheckBox>("PendingOnly").IsCheckedChanged += (_, _) => { if (!_loading) { CommitEditor(); Refresh(_selected?.Id); } };
        C<RadioButton>("NovelMode").IsCheckedChanged += (_, _) => OptionsChanged();
        C<RadioButton>("ScriptMode").IsCheckedChanged += (_, _) => OptionsChanged();
        C<ComboBox>("SplitChoice").SelectionChanged += (_, _) => OptionsChanged();
        C<NumericUpDown>("LengthInput").ValueChanged += (_, _) => OptionsChanged();
        C<ComboBox>("KindChoice").SelectionChanged += (_, _) =>
        {
            var dialogue = C<ComboBox>("KindChoice").SelectedIndex == 1;
            C<TextBox>("SpeakerBox").IsEnabled = dialogue;
            C<ComboBox>("SpeakerShortcutChoice").IsEnabled = dialogue;
        };
        C<ComboBox>("SpeakerShortcutChoice").SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (C<ComboBox>("SpeakerShortcutChoice").SelectedItem is string name && name != ShortcutPrompt)
                C<TextBox>("SpeakerBox").Text = name;
        };
        C<TextBox>("SpeakerBox").LostFocus += (_, _) =>
        {
            if (!_loading && C<ComboBox>("KindChoice").SelectedIndex == 1)
                RegisterSpeakerShortcut(C<TextBox>("SpeakerBox").Text);
        };
        Source.TextChanged += (_, _) =>
        {
            if (_loading) return;
            _dirty = true; _sourceStale = true;
            C<TextBlock>("SourceCount").Text = $"{(Source.Text ?? "").Length:N0} 字";
            C<TextBlock>("SourceHint").Text = "原文已修改，请重新解析后导出。";
            SetStatus("原文已修改，识别结果尚未更新。");
        };
        Closing += async (_, e) =>
        {
            if (_closingAllowed) return;
            CommitEditor();
            if (!_dirty) return;
            e.Cancel = true;
            if (await Dialog("尚未保存的工程", "原文、解析结果或校正内容尚未保存。可以取消退出并点击「保存工程」。", "放弃修改并退出", "取消"))
            { _closingAllowed = true; Close(); }
        };
        Opened += async (_, _) =>
        {
            if (Program.Arguments.Contains("--demo")) await LoadSampleAsync();
            var path = Program.Arguments.FirstOrDefault(x => !x.StartsWith("--") && File.Exists(x));
            if (path != null) await Safe(() => LoadPathAsync(path));
        };
        RefreshSpeakerChoices();
    }

    private void On(string name, Func<Task> action) => C<Button>(name).Click += async (_, _) => await Safe(action);
    private async Task Safe(Func<Task> action) { try { if (!_busy) await action(); } catch (Exception ex) { await ReportError(ex); } }
    private void SetStatus(string text) => C<TextBlock>("StatusText").Text = text;
    private void UpdateSelectionCount() => C<TextBlock>("SelectionCount").Text = $"已选 {Rows.SelectedItems?.Count ?? 0} 条";

    private int HitRow(PointerEventArgs e)
    {
        var hit = Rows.InputHitTest(e.GetPosition(Rows)) as Visual;
        var container = hit as ListBoxItem ?? hit?.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        return container is null ? -1 : Rows.IndexFromContainer(container);
    }

    private void DragPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_refreshing || e.Pointer.Type != PointerType.Mouse || !e.GetCurrentPoint(Rows).Properties.IsLeftButtonPressed) return;
        _dragAnchor = HitRow(e);
        if (_dragAnchor < 0) return;
        _dragStart = e.GetPosition(Rows);
        _dragSelecting = false;
        _dragBase = Rows.SelectedItems?.OfType<Segment>().ToHashSet() ?? [];
        e.Pointer.Capture(Rows);
    }

    private void DragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragAnchor < 0) return;
        var position = e.GetPosition(Rows);
        if (!_dragSelecting)
        {
            if (Math.Abs(position.X - _dragStart.X) + Math.Abs(position.Y - _dragStart.Y) < 5) return;
            _dragSelecting = true;
        }
        var index = HitRow(e);
        if (index < 0) index = position.Y < 0 ? 0 : position.Y > Rows.Bounds.Height ? Rows.ItemCount - 1 : -1;
        if (index < 0) return;
        var items = Rows.ItemsSource?.OfType<Segment>().ToList() ?? [];
        var selected = _dragBase.Concat(items.Skip(Math.Min(index, _dragAnchor)).Take(Math.Abs(index - _dragAnchor) + 1)).ToHashSet();
        _refreshing = true;
        try
        {
            Rows.SelectedItems?.Clear();
            foreach (var item in items.Where(selected.Contains)) Rows.SelectedItems?.Add(item);
        }
        finally { _refreshing = false; }
        _selected = items[index];
        UpdateSelectionCount();
    }

    private void DragPointerReleased(object? sender, PointerReleasedEventArgs e) => EndDrag(e);

    private void EndDrag(PointerEventArgs e)
    {
        if (_dragAnchor < 0) return;
        e.Pointer.Capture(null);
        if (_dragSelecting)
        {
            ShowEditor(); UpdatePreview(); UpdateSelectionCount();
        }
        _dragAnchor = -1;
        _dragSelecting = false;
        _dragBase.Clear();
    }

    private Task SelectVisibleAsync()
    {
        CommitEditor();
        _refreshing = true;
        try
        {
            Rows.SelectedItems?.Clear();
            foreach (var segment in Rows.ItemsSource?.OfType<Segment>() ?? []) Rows.SelectedItems?.Add(segment);
        }
        finally { _refreshing = false; }
        _selected = Rows.SelectedItem as Segment; ShowEditor(); UpdateSelectionCount();
        return Task.CompletedTask;
    }

    private Task ClearSelectionAsync()
    {
        CommitEditor();
        _refreshing = true;
        try { Rows.SelectedItems?.Clear(); }
        finally { _refreshing = false; }
        _selected = null; ShowEditor(); UpdateSelectionCount();
        return Task.CompletedTask;
    }

    private Task ReviewSelectedAsync()
    {
        CommitEditor();
        var selected = Rows.SelectedItems?.OfType<Segment>().ToList() ?? [];
        if (selected.Count == 0) throw new UserFacingException("请先点击需要审核的条目进行多选，或点击「全选当前结果」。");
        var invalid = selected.Count(s => s.Include && string.IsNullOrWhiteSpace(s.Text) ||
            s.Kind == SegmentKind.Dialogue && string.IsNullOrWhiteSpace(s.Speaker));
        if (invalid > 0) throw new UserFacingException($"所选内容中有 {invalid} 条缺少正文或对白说话人。请先逐条补全，再批量审核。");
        PushUndo();
        foreach (var segment in selected) segment.Reviewed = true;
        _dirty = true; _manualEdits = true;
        Refresh();
        SetStatus($"已审核 {selected.Count} 条内容。请保存工程以保留结果。");
        return Task.CompletedTask;
    }

    private void RefreshSpeakerChoices()
    {
        var names = _project.SpeakerShortcuts.Concat(_project.Options.Aliases.Values)
            .Concat(_project.Export.CharacterVariables.Keys)
            .Concat(_project.Segments.Select(s => s.Speaker))
            .Concat(ChoiceSegments(_project.ChoiceTrees).Select(s => s.Speaker))
            .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var previous = _loading; _loading = true;
        var box = C<ComboBox>("SpeakerShortcutChoice");
        box.ItemsSource = new[] { ShortcutPrompt }.Concat(names).ToList();
        box.SelectedIndex = 0;
        _loading = previous;
    }

    private static IEnumerable<Segment> ChoiceSegments(IEnumerable<ChoiceTree> trees)
    {
        foreach (var tree in trees)
            foreach (var branch in tree.Branches)
                foreach (var item in branch.Items)
                {
                    if (item.Segment is not null) yield return item.Segment;
                    if (item.Tree is not null)
                        foreach (var nested in ChoiceSegments([item.Tree])) yield return nested;
                }
    }

    private void RegisterSpeakerShortcut(string? value)
    {
        var name = value?.Trim() ?? "";
        if (name.Length is 0 or > 100 || _project.SpeakerShortcuts.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        _project.SpeakerShortcuts.Add(name);
        _dirty = true;
        RefreshSpeakerChoices();
    }
    public async Task ReportError(Exception ex)
    {
        var friendly = ex is UserFacingException || ex is IOException || ex is UnauthorizedAccessException;
        var message = friendly ? ex.Message : "操作没有完成。请检查输入内容和文件权限，然后重试。\n\n" + ex.Message;
        if (!friendly) message += "\n\n诊断日志：" + ErrorLog.Write(ex);
        SetStatus("操作失败：" + ex.Message);
        await Dialog("操作未完成", message, "知道了");
    }

    private ParseOptions CurrentOptions() => new()
    {
        Mode = C<RadioButton>("ScriptMode").IsChecked == true ? ImportMode.Script : ImportMode.Novel,
        Split = (SplitMode)Math.Max(0, C<ComboBox>("SplitChoice").SelectedIndex),
        MaxLength = (int)(C<NumericUpDown>("LengthInput").Value ?? 80),
        Aliases = new Dictionary<string, string>(_project.Options.Aliases, StringComparer.OrdinalIgnoreCase)
    };

    private void OptionsChanged()
    {
        if (_loading) return;
        C<TextBlock>("ModeHint").Text = C<RadioButton>("ScriptMode").IsChecked == true ? "优先识别角色名、台词与舞台说明" : "结合引号与说话动作，分离对白和旁白";
        if (string.IsNullOrWhiteSpace(Source.Text)) return;
        _sourceStale = true; _dirty = true;
        SetStatus("解析设置已修改，点击「解析文本」应用。重新解析前会提示已有校正。");
    }

    private void PushUndo()
    {
        _undo.Push(JsonSerializer.Serialize(_project));
        if (_undo.Count > 30)
        {
            var keep = _undo.Take(30).Reverse().ToArray(); _undo.Clear(); foreach (var item in keep) _undo.Push(item);
        }
    }

    private async Task<bool> CanReplace()
    {
        CommitEditor();
        return !_dirty || await Dialog("替换当前工程？", "当前内容尚未保存。继续会替换正在编辑的稿件；取消后可先保存工程。", "继续替换", "取消");
    }

    private async Task ImportAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择小说或剧本", AllowMultiple = false,
            FileTypeFilter = [new("稿件（TXT / DOCX / Markdown）") { Patterns = ["*.txt", "*.text", "*.docx", "*.md", "*.markdown"] }, FilePickerFileTypes.All]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath(); if (path == null) return;
        if (!await CanReplace()) return;
        await LoadPathAsync(path);
    }

    private async Task LoadPathAsync(string path)
    {
        if (path.EndsWith(".jumu", StringComparison.OrdinalIgnoreCase))
        {
            var loaded = ProjectStorage.Load(path); _project = loaded; _projectPath = path;
            _dirty = false; _manualEdits = true; _sourceStale = loaded.RequiresReparse; _undo.Clear(); SetProjectControls(); Refresh();
            SetStatus(_sourceStale ? "工程已恢复。原文或设置已改变，请重新解析后导出。" : "工程已打开，已恢复校正内容。"); return;
        }
        SetStatus("正在读取稿件…");
        var doc = await Task.Run(() => DocumentImporter.Read(path));
        var options = CurrentOptions();
        _project = new ProjectDocument { SourceFile = path, SourceText = doc.Text, Options = options };
        _projectPath = null; _undo.Clear(); _manualEdits = false; _dirty = true; _sourceStale = true;
        _selected = null; SetProjectControls();
        await ParseAsync();
        if (doc.Warnings.Count > 0) await Dialog("稿件导入提示", string.Join("\n\n", doc.Warnings), "知道了");
    }

    private void SetProjectControls()
    {
        _loading = true;
        Source.Text = _project.SourceText;
        C<RadioButton>("NovelMode").IsChecked = _project.Options.Mode == ImportMode.Novel;
        C<RadioButton>("ScriptMode").IsChecked = _project.Options.Mode == ImportMode.Script;
        C<ComboBox>("SplitChoice").SelectedIndex = (int)_project.Options.Split;
        C<NumericUpDown>("LengthInput").Value = Math.Clamp(_project.Options.MaxLength, 20, 500);
        C<TextBlock>("FileNameText").Text = string.IsNullOrWhiteSpace(_project.SourceFile) ? "粘贴的稿件" : Path.GetFileName(_project.SourceFile);
        C<TextBlock>("SourceCount").Text = $"{_project.SourceText.Length:N0} 字";
        C<TextBlock>("SourceHint").Text = "原文可编辑 · 修改后点击「解析文本」";
        RefreshSpeakerChoices();
        _loading = false;
    }

    private async Task ParseAsync()
    {
        CommitEditor();
        var text = Source.Text ?? "";
        if (string.IsNullOrWhiteSpace(text)) throw new UserFacingException("请先导入文件，或者将稿件粘贴到左侧原文框。");
        if (text.Length > 2_000_000) throw new UserFacingException("稿件超过 200 万字符，请按章节分开导入。");
        if ((_manualEdits || _project.ChoiceTrees.Count > 0) && !await Dialog("重新解析？", "重新解析会替换手动校正的结果和选项树。建议先保存工程，也可以在解析后使用「撤销」。", "重新解析", "取消")) return;
        var options = CurrentOptions();
        _busy = true; IsEnabled = false; SetStatus("正在解析，请稍候…");
        try
        {
            var result = await Task.Run(() => ManuscriptParser.Parse(text, options));
            PushUndo(); _project.SourceText = text; _project.Options = options; _project.Segments = result.Segments;
            _project.ChoiceTrees.Clear();
            _selected = null; _dirty = true; _manualEdits = false; _sourceStale = false; _project.RequiresReparse = false;
            Refresh(); C<TextBlock>("SourceHint").Text = "点击结果即可定位原文；所有原文保存在工程中。";
            SetStatus($"解析完成：{result.Segments.Count} 条内容。请检查待确认项。" + (result.Warnings.Count > 0 ? " " + string.Join("；", result.Warnings) : ""));
        }
        finally { _busy = false; IsEnabled = true; }
    }

    private void CommitEditor(bool reviewed = false)
    {
        if (_loading || _editorTargets.Count == 0) return;
        var text = C<TextBox>("DialogueBox").Text ?? "";
        var speaker = (C<TextBox>("SpeakerBox").Text ?? "").Trim();
        var kind = (SegmentKind)Math.Max(0, C<ComboBox>("KindChoice").SelectedIndex);
        var include = C<CheckBox>("IncludeCheck").IsChecked == true;
        var edit = new SegmentEdit(
            text != _editorSnapshot.Text ? text : null,
            speaker != _editorSnapshot.Speaker ? speaker : null,
            kind != _editorSnapshot.Kind ? kind : null,
            include != _editorSnapshot.Include ? include : null);
        if (!edit.HasChanges && !reviewed) return;
        SegmentBatchEditor.Validate(_editorTargets, edit, reviewed);
        PushUndo();
        var affected = SegmentBatchEditor.Apply(_editorTargets, edit, reviewed);
        if (affected == 0) { _undo.Pop(); return; }
        if (edit.Speaker is not null && speaker.Length > 0 && _editorTargets.Any(s => s.Kind == SegmentKind.Dialogue))
            RegisterSpeakerShortcut(speaker);
        _dirty = true; _manualEdits = true;
        if (reviewed) SetStatus($"已应用并确认 {affected} 条内容。请保存工程以保留校正。");
        UpdatePreview();
    }

    private void Refresh(string? selectId = null, IReadOnlySet<string>? selectedIds = null)
    {
        _refreshing = true;
        var query = (C<TextBox>("SearchBox").Text ?? "").Trim();
        var pending = C<CheckBox>("PendingOnly").IsChecked == true;
        var display = _project.Segments.Where(s => (!pending || (!s.Reviewed && (s.Warnings.Count > 0 || (s.Kind == SegmentKind.Dialogue && s.Speaker.Length == 0)))) &&
            (query.Length == 0 || s.Text.Contains(query, StringComparison.OrdinalIgnoreCase) || s.Speaker.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
        Rows.SelectedItems?.Clear();
        Rows.ItemsSource = display;
        Rows.SelectedItems?.Clear();
        if (selectedIds is not null)
            foreach (var segment in display.Where(s => selectedIds.Contains(s.Id))) Rows.SelectedItems?.Add(segment);
        else if (selectId is not null)
        {
            var segment = display.FirstOrDefault(s => s.Id == selectId);
            if (segment is not null) Rows.SelectedItems?.Add(segment);
        }
        _selected = selectId is not null ? display.FirstOrDefault(s => s.Id == selectId) : Rows.SelectedItems?.OfType<Segment>().LastOrDefault();
        C<StackPanel>("EmptyState").IsVisible = _project.Segments.Count == 0;
        var branchSegments = ChoiceSegments(_project.ChoiceTrees).ToList();
        var p = _project.Segments.Count(s => s.Include && !s.Reviewed && (s.Warnings.Count > 0 || (s.Kind == SegmentKind.Dialogue && s.Speaker.Length == 0)));
        var branchPending = branchSegments.Count(s => s.Include && !s.Reviewed && (s.Warnings.Count > 0 || (s.Kind == SegmentKind.Dialogue && s.Speaker.Length == 0)));
        var chars = _project.Segments.Concat(branchSegments).Where(s => s.Kind == SegmentKind.Dialogue && s.Speaker.Length > 0).Select(s => s.Speaker).Distinct().Count();
        C<TextBlock>("StatsText").Text = $"{_project.Segments.Count} 条内容  ·  {chars} 位角色  ·  {p} 条待确认  ·  {_project.ChoiceTrees.Count} 棵选项树" +
            (branchPending > 0 ? $"  ·  分支另有 {branchPending} 条待确认" : "") +
            (display.Count != _project.Segments.Count ? $"  /  显示 {display.Count} 条" : "");
        C<Button>("UndoButton").IsEnabled = _undo.Count > 0;
        C<Button>("ChoiceTreeButton").IsEnabled = _project.Segments.Count > 0;
        C<Button>("QuickAddChoiceButton").IsEnabled = _project.Segments.Count > 0;
        RefreshSpeakerChoices();
        _refreshing = false; ShowEditor(); UpdatePreview(); UpdateSelectionCount();
    }

    private void ShowEditor()
    {
        _loading = true;
        _editorTargets = Rows.SelectedItems?.OfType<Segment>().ToList() ?? [];
        if (_selected is null || !_editorTargets.Contains(_selected)) _selected = _editorTargets.LastOrDefault();
        _editorSnapshot = new SegmentEditSnapshot(_selected?.Text ?? "", _selected?.Speaker ?? "", _selected?.Kind ?? SegmentKind.Narration, _selected?.Include ?? true);
        C<StackPanel>("EditorPanel").IsEnabled = _selected != null;
        C<TextBox>("DialogueBox").Text = _selected?.Text ?? "";
        C<TextBox>("SpeakerBox").Text = _selected?.Speaker ?? "";
        C<ComboBox>("KindChoice").SelectedIndex = (int)(_selected?.Kind ?? SegmentKind.Narration);
        var shortcut = C<ComboBox>("SpeakerShortcutChoice");
        shortcut.SelectedItem = shortcut.ItemsSource?.OfType<string>().FirstOrDefault(name =>
            name != ShortcutPrompt && string.Equals(name, _selected?.Speaker, StringComparison.OrdinalIgnoreCase)) ?? ShortcutPrompt;
        C<CheckBox>("IncludeCheck").IsChecked = _selected?.Include ?? true;
        C<TextBlock>("SelectionInfo").Text = _selected == null ? "选择一条识别结果" : _editorTargets.Count > 1
            ? $"已选 {_editorTargets.Count} 条 · 当前显示第 {_project.Segments.IndexOf(_selected) + 1} 条；仅把改动过的字段应用到所有选中条目"
            : $"第 {_project.Segments.IndexOf(_selected) + 1} 条  /  原文第 {_selected.Paragraph} 段";
        C<TextBlock>("WarningText").Text = _selected == null ? "" : _editorTargets.Count > 1
            ? "批量确认会检查每一条对白的说话人及正文。不同条目原有的字段保持不变，除非你在右侧修改了该字段。"
            : _selected.Reviewed ? "已人工确认。" : _selected.Warnings.Count > 0 ? string.Join("\n", _selected.Warnings) : "规则识别完成，仍可根据故事语境调整。";
        C<TextBlock>("OriginalSnippet").Text = _selected?.Source ?? "";
        if (_selected != null && _selected.Source.Length > 0)
        {
            var offset = 0;
            var lines = (Source.Text ?? "").Split('\n');
            for (var i = 0; i < Math.Min(_selected.Paragraph - 1, lines.Length); i++) offset += lines[i].Length + 1;
            var start = (Source.Text ?? "").IndexOf(_selected.Source, Math.Min(offset, (Source.Text ?? "").Length), StringComparison.Ordinal);
            if (start < 0) start = (Source.Text ?? "").IndexOf(_selected.Source, StringComparison.Ordinal);
            if (start >= 0) { Source.SelectionStart = start; Source.SelectionEnd = start + _selected.Source.Length; Source.CaretIndex = start; }
        }
        _loading = false;
    }

    private readonly record struct SegmentEditSnapshot(string Text, string Speaker, SegmentKind Kind, bool Include);

    private void UpdatePreview()
    {
        try { C<TextBox>("RpyBox").Text = _project.Segments.Count == 0 ? "解析后会在这里显示 .rpy 内容。" : RenpyExporter.Generate(_project.Segments, _project.Export, _project.ChoiceTrees); }
        catch (Exception ex) { C<TextBox>("RpyBox").Text = "导出设置需要调整：" + ex.Message; }
    }

    private async Task SplitAsync()
    {
        if (_selected == null) return;
        var caret = C<TextBox>("DialogueBox").CaretIndex;
        CommitEditor(); var s = _selected;
        if (caret <= 0 || caret >= s.Text.Length) throw new UserFacingException("请先在正文输入框中，将光标放到需要拆分的位置（不要放在开头或末尾）。");
        if (char.IsLowSurrogate(s.Text[caret])) throw new UserFacingException("光标位于一个字符内部，请移动后再拆分。");
        var before = s.Text[..caret].TrimEnd(); var after = s.Text[caret..].TrimStart();
        if (before.Length == 0 || after.Length == 0) throw new UserFacingException("拆分后不能出现空白条目，请重新选择位置。");
        PushUndo();
        var next = JsonSerializer.Deserialize<Segment>(JsonSerializer.Serialize(s))!;
        next.Id = Guid.NewGuid().ToString("N"); next.Text = s.Text[caret..].TrimStart(); next.Reviewed = false;
        s.Text = before; s.Reviewed = false;
        foreach (var tree in _project.ChoiceTrees.Where(t => t.AnchorSegmentId == s.Id && t.Placement == ChoicePlacement.After))
            tree.AnchorSegmentId = next.Id;
        _project.Segments.Insert(_project.Segments.IndexOf(s) + 1, next); _dirty = true; _manualEdits = true; Refresh(s.Id); SetStatus("已拆成两条，可分别校正。"); await Task.CompletedTask;
    }

    private async Task MergeAsync()
    {
        if (_selected == null) return;
        CommitEditor(); var i = _project.Segments.IndexOf(_selected);
        if (i < 0 || i == _project.Segments.Count - 1) throw new UserFacingException("当前已经是最后一条，后面没有可合并的内容。");
        var next = _project.Segments[i + 1];
        if (_project.ChoiceTrees.Any(t => t.AnchorSegmentId == _selected.Id && t.Placement == ChoicePlacement.After ||
            t.AnchorSegmentId == next.Id && t.Placement == ChoicePlacement.Before))
            throw new UserFacingException("这两句之间已有选项树。请先在「选项树」里移动或删除该选项，再合并句子。");
        if ((_selected.Kind != next.Kind || _selected.Speaker != next.Speaker) && !await Dialog("合并不同角色或类型？", $"下一条属于「{next.SpeakerLabel}」。合并后采用当前条目的「{_selected.SpeakerLabel}」，请确认符合原文。", "合并", "取消")) return;
        PushUndo(); _selected.Text += "\n" + next.Text;
        foreach (var tree in _project.ChoiceTrees.Where(t => t.AnchorSegmentId == next.Id)) tree.AnchorSegmentId = _selected.Id;
        if (_selected.Source != next.Source) _selected.Source += "\n" + next.Source;
        _selected.Warnings = _selected.Warnings.Concat(next.Warnings).Distinct().ToList(); _selected.Reviewed = false;
        _project.Segments.RemoveAt(i + 1); _dirty = true; _manualEdits = true; Refresh(_selected.Id); SetStatus("已合并下一条，可继续编辑。");
    }

    private Task QuickAddChoiceAsync()
    {
        CommitEditor();
        var label = (C<TextBox>("QuickChoiceText").Text ?? "").Trim();
        if (label.Length == 0) throw new UserFacingException("请先在识别结果上方输入选项文字，再点击「添加选项」。");
        if (label.Length > 4096) throw new UserFacingException("选项文字过长，请缩短后重试。");
        if (_project.Segments.Count == 0) throw new UserFacingException("请先解析稿件，再添加选项。");
        PushUndo();
        var added = ChoiceTreeEditor.AddQuickChoice(_project, label, _selected?.Id);
        _dirty = true; _manualEdits = true;
        C<TextBox>("QuickChoiceText").Text = "";
        Refresh(_selected?.Id);
        var anchorIndex = _project.Segments.FindIndex(s => s.Id == added.Tree.AnchorSegmentId) + 1;
        SetStatus($"已在第 {anchorIndex} 句后添加「{label}」。点击「选项树」可拖动或删除。请保存工程。");
        return Task.CompletedTask;
    }

    private async Task OpenChoiceTreeAsync(ChoicePlacement? createAt = null)
    {
        CommitEditor();
        if (_project.Segments.Count == 0) throw new UserFacingException("请先解析稿件，再为句子添加选项树。");
        if (createAt is not null && _selected is null)
            throw new UserFacingException("请先在识别结果中选中一句，再选择在句前或句后添加选项。");
        var selectedId = _selected?.Id;
        var window = new ChoiceTreeWindow(_project, selectedId, createAt);
        if (!await window.ShowDialog<bool>(this) || !window.HasChanges) return;
        PushUndo();
        _project.ChoiceTrees = window.EditedTrees!;
        _dirty = true; _manualEdits = true;
        Refresh(selectedId);
        SetStatus($"选项树已更新，共 {_project.ChoiceTrees.Count} 棵。请保存工程以保留分支。");
    }

    private Task UndoAsync()
    {
        if (_undo.Count == 0) return Task.CompletedTask;
        _project = JsonSerializer.Deserialize<ProjectDocument>(_undo.Pop())!;
        _dirty = true; _manualEdits = true; _sourceStale = _project.RequiresReparse; _selected = null;
        SetProjectControls(); Refresh(); SetStatus("已撤销上一步解析或校正。"); return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        CommitEditor();
        _project.SourceText = Source.Text ?? ""; _project.Options = CurrentOptions(); _project.RequiresReparse = _sourceStale;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "保存校正工程", SuggestedFileName = _projectPath == null ? "我的剧本.jumu" : Path.GetFileName(_projectPath), DefaultExtension = "jumu", FileTypeChoices = [new("句幕工程") { Patterns = ["*.jumu"] }] });
        var path = file?.TryGetLocalPath(); if (path == null) return;
        if (!path.EndsWith(".jumu", StringComparison.OrdinalIgnoreCase)) path += ".jumu";
        if (File.Exists(path) && !await Dialog("替换已保存的工程？", "将保存当前内容，并为已有工程保留备份。", "保存并备份", "取消")) return;
        ProjectStorage.Save(path, _project); _projectPath = path; _dirty = false; SetStatus("工程已保存：" + path);
    }

    private async Task OpenProjectAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "打开句幕工程", FileTypeFilter = [new("句幕工程") { Patterns = ["*.jumu"] }] });
        var path = files.FirstOrDefault()?.TryGetLocalPath(); if (path == null || !await CanReplace()) return;
        await LoadPathAsync(path);
    }

    private async Task ExportAsync()
    {
        CommitEditor(); Refresh(_selected?.Id);
        if (_sourceStale) throw new UserFacingException("原文或解析设置已有变更，请先点击「解析文本」，再导出。");
        if (!_project.Segments.Any(s => s.Include) && _project.ChoiceTrees.Count == 0)
            throw new UserFacingException("没有可导出的内容，请先解析稿件并检查勾选状态。");
        var blank = _project.Segments.FindIndex(s => s.Include && string.IsNullOrWhiteSpace(s.Text));
        if (blank >= 0) throw new UserFacingException($"第 {blank + 1} 条正文为空，请填写内容或取消包含在导出中。");
        var branchSegments = ChoiceSegments(_project.ChoiceTrees).ToList();
        if (branchSegments.Any(s => s.Include && string.IsNullOrWhiteSpace(s.Text)))
            throw new UserFacingException("选项树中有空白句子，请在选项树里填写内容或删除该句子。");
        var pending = _project.Segments.Concat(branchSegments).Count(s => s.Include && !s.Reviewed &&
            (s.Warnings.Count > 0 || (s.Kind == SegmentKind.Dialogue && string.IsNullOrWhiteSpace(s.Speaker))));
        if (pending > 0 && !await Dialog("还有待确认内容", $"有 {pending} 条内容需要检查。未指定说话人的对白会使用临时显示名导出。你可以返回校正，或导出当前草稿。", "导出草稿", "返回校正")) return;
        _ = RenpyExporter.Generate(_project.Segments, _project.Export, _project.ChoiceTrees);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "导出至 Ren’Py 项目的 game 文件夹", SuggestedFileName = _project.Export.Label + ".rpy", DefaultExtension = "rpy", FileTypeChoices = [new("Ren’Py 剧本") { Patterns = ["*.rpy"] }], ShowOverwritePrompt = true });
        var path = file?.TryGetLocalPath(); if (path == null) return;
        if (!path.EndsWith(".rpy", StringComparison.OrdinalIgnoreCase)) path += ".rpy";
        if (!string.IsNullOrWhiteSpace(_project.SourceFile) && Path.GetFullPath(path) == Path.GetFullPath(_project.SourceFile)) throw new UserFacingException("导出文件不能覆盖原始稿件，请换一个文件名。");
        var overwrite = File.Exists(path);
        if (overwrite && !await Dialog("覆盖已有脚本？", "将替换选定的 .rpy 文件，并在旁边保留带时间的备份。已有演出、分支等内容不会自动合并。", "备份并覆盖", "取消")) return;
        RenpyExporter.Write(path, _project.Segments, _project.Export, overwrite, _project.ChoiceTrees);
        SetStatus("导出成功：" + path);
        await Dialog("剧本已导出", "文件：" + path + "\n\n将文件放进 Ren’Py 项目的 game 文件夹，并在已有的 label start 中添加：\n\n    call " + _project.Export.Label + "\n\n同一项目内的 label 名称必须唯一。角色中文显示还需要项目配置中文字体。", "完成");
    }

    private async Task SettingsAsync()
    {
        CommitEditor();
        var label = new TextBox { Text = _project.Export.Label, Watermark = "imported_story" };
        var aliases = new TextBox { Text = string.Join("\n", _project.Options.Aliases.Select(x => x.Key + "=" + x.Value)), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 115, Watermark = "小林=林夏\n阿夏=林夏\nAlice=Alice" };
        var variables = new TextBox { Text = string.Join("\n", _project.Export.CharacterVariables.Select(x => x.Key + "=" + x.Value)), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 100, Watermark = "林夏=lx\nAlice=alice" };
        var comments = new CheckBox { Content = "舞台说明以注释导出", IsChecked = _project.Export.DirectionAsComments };
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = "入口 label", FontWeight = FontWeight.SemiBold }); body.Children.Add(label);
        body.Children.Add(new TextBlock { Text = "角色别名（每行：别名=正式名）", FontWeight = FontWeight.SemiBold }); body.Children.Add(aliases);
        body.Children.Add(new TextBlock { Text = "别名也可以帮助识别短角色名；修改后需重新解析。", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        body.Children.Add(new TextBlock { Text = "已有 Ren’Py 角色变量（可选，每行：正式名=变量）", FontWeight = FontWeight.SemiBold }); body.Children.Add(variables);
        body.Children.Add(new TextBlock { Text = "映射的变量需在 Ren’Py 项目中先行定义。留空则直接显示角色名。", TextWrapping = TextWrapping.Wrap, FontSize = 12 }); body.Children.Add(comments);
        if (!await CustomDialog("角色与导出设置", body, "应用设置", "取消", 580)) return;
        static Dictionary<string, string> Map(string? value)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in (value ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace)) throw new UserFacingException("角色映射每行需要采用「名称=名称或变量」格式：" + line);
                if (!d.TryAdd(parts[0], parts[1])) throw new UserFacingException("角色映射存在重复名称：" + parts[0]);
            }
            return d;
        }
        var newAliases = Map(aliases.Text); var newVars = Map(variables.Text);
        var settings = new ExportOptions { Label = (label.Text ?? "").Trim(), CharacterVariables = newVars, DirectionAsComments = comments.IsChecked == true };
        _ = RenpyExporter.Generate([new Segment { Text = "预览" }], settings);
        PushUndo();
        if (JsonSerializer.Serialize(_project.Options.Aliases) != JsonSerializer.Serialize(newAliases) && _project.Segments.Count > 0) _sourceStale = true;
        _project.Options.Aliases = newAliases; _project.Export = settings; _dirty = true;
        RefreshSpeakerChoices(); UpdatePreview();
        SetStatus(_sourceStale ? "设置已保存，点击「解析文本」应用角色别名。" : "导出设置已更新。");
    }

    private async Task LoadSampleAsync()
    {
        if (!await CanReplace()) return;
        var mode = CurrentOptions().Mode;
        const string novel = "雨停在傍晚 18:30，街边的灯亮了。\n\n林夏说：“你确定？现在就走？”\n\n周远回答：“当然。车票是 3.14 美元，别弄丢了。”\n\n她望着门外：“那好吧……我再等五分钟。”\n\n他只有一个念头：离开这里。\n\nAlice said, \"Don't worry, Dr. Smith will arrive at 7:30.\"\n\n她翻开标着“未完成”的手稿，网址写着 https://example.com。";
        const string script = "【场景：雨后的车站】\n林夏：你确定？现在就走？\n周远: 当然。时间是 18:30，票价是 3.14 美元。\n旁白：街边的灯亮了。\nAlice: Don't worry, Dr. Smith will arrive at 7:30.\n周远「那我们出发吧……」\n他只有一个念头：离开这里。\n网址是 https://example.com，请记下来。";
        _project = new ProjectDocument { SourceFile = mode == ImportMode.Novel ? "小说示例.txt" : "剧本示例.txt", SourceText = mode == ImportMode.Novel ? novel : script, Options = CurrentOptions() };
        _project.Options.Aliases = new(StringComparer.OrdinalIgnoreCase) { ["林夏"] = "林夏", ["周远"] = "周远", ["Alice"] = "Alice" };
        _projectPath = null; _dirty = true; _manualEdits = false; _undo.Clear(); _selected = null; SetProjectControls(); await ParseAsync();
    }

    private Task HelpAsync() => Dialog("句幕使用说明", "1. 导入 TXT、DOCX、Markdown，或将正文粘贴到左侧。\n2. 选择「小说正文」或「剧本」，设置拆分方式，点击解析。\n3. 点击条目校正角色与内容；单击可多选，点击「审核所选」可批量确认。新输入的人物姓名会进入快捷列表。\n4. 在识别结果上方输入文字并点击「添加选项」：选中一句时接在该句后，未选中时接在正文末尾。同一位置连续添加会成为同组选项。\n5. 点击「选项树」打开图形界面。将选项拖到图内绿色区域可排到同组末尾；拖到左侧底部可接在最后一棵选项树后，成为顺序播放的新选项树。可用 × 删除，最后点击「应用选项树」。\n6. 保存 .jumu 工程，保留原文、校正与选项树。\n7. 导出 .rpy 到 Ren’Py 项目的 game 文件夹，在已有 start 中 call 对应 label。\n\n小说模式结合引号与明确说话动作推断；剧本模式优先识别角色标记。两者均为离线上下文规则，不能保证理解所有小说语境。代词、引用和歧义会提示复核。\n\n选项分支结束后会回到主线；不会自动创建变量或永久跳转。旧版 .doc 请先另存为 .docx。\n\n所有处理在本机完成，无需账号或网络。", "知道了");

    private Task<bool> Dialog(string title, string text, string accept, string? cancel = null) => CustomDialog(title, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 24 }, accept, cancel);
    private async Task<bool> CustomDialog(string title, Control body, string accept, string? cancel, double width = 540)
    {
        var dialog = new Window { Title = title, Width = width, MaxHeight = 760, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush.Parse("#F8FAF6") };
        var layout = new StackPanel { Spacing = 20, Margin = new Thickness(24) };
        layout.Children.Add(new TextBlock { Text = title, FontSize = 21, FontWeight = FontWeight.SemiBold });
        layout.Children.Add(new ScrollViewer { Content = body, MaxHeight = 560 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        if (cancel != null) { var c = new Button { Content = cancel }; c.Click += (_, _) => dialog.Close(false); buttons.Children.Add(c); }
        var ok = new Button { Content = accept }; ok.Classes.Add("primary"); ok.Click += (_, _) => dialog.Close(true); buttons.Children.Add(ok);
        layout.Children.Add(buttons); dialog.Content = layout;
        return await dialog.ShowDialog<bool>(this);
    }
}
