using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Scribe.Core;

namespace Scribe.Desktop;

/// <summary>A private editing session for moving and editing whole story fragments.</summary>
public sealed class FragmentBagWindow : Window
{
    private ProjectDocument _project;
    private readonly string _initialJson;
    private readonly HashSet<string> _selection = [];
    private readonly Stack<string> _history = new();
    private readonly StackPanel _main = new() { Spacing = 7 };
    private readonly StackPanel _bag = new() { Spacing = 10 };
    private readonly StackPanel _details = new() { Spacing = 10 };
    private readonly Border _mainPanel = Panel();
    private readonly Border _bagPanel = Panel();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#64786D") };
    private readonly List<(Border Card, int Index)> _mainCards = [];
    private string? _fragmentId;
    private string? _sentenceId;
    private Action? _commit;
    public ProjectDocument? EditedProject { get; private set; }
    public bool HasChanges { get; private set; }

    public FragmentBagWindow(ProjectDocument project, IEnumerable<string>? selectedIds = null)
    {
        _initialJson = JsonSerializer.Serialize(project);
        _project = JsonSerializer.Deserialize<ProjectDocument>(_initialJson)!;
        if (selectedIds is not null) _selection.UnionWith(selectedIds);
        Title = "素材袋与剧情节点 · 句幕";
        Width = 1370; Height = 860; MinWidth = 1080; MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#F8FAF6");
        Build(); Render();
        UiMotion.Attach(this);
    }

    private static Border Panel() => new()
    {
        Classes = { "motion-drop" },
        Background = Brushes.White, BorderBrush = Brush.Parse("#DDE8DC"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12), Padding = new Thickness(14)
    };
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brush.Parse("#64786D") };
    private static TextBlock Caption(string text) => new() { Text = text, FontSize = 13, FontWeight = FontWeight.SemiBold };
    private Button ActionButton(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary) button.Classes.Add("primary");
        button.Click += (_, _) => Attempt(action);
        return button;
    }
    private void Attempt(Action action)
    {
        try { action(); }
        catch (UserFacingException ex) { _status.Text = ex.Message; _status.Foreground = Brush.Parse("#B3403A"); }
    }
    private void Commit() => _commit?.Invoke();
    private void Snapshot() { Commit(); _history.Push(JsonSerializer.Serialize(_project)); }

    private void Build()
    {
        var grid = new Grid { Margin = new Thickness(20), ColumnDefinitions = new ColumnDefinitions("1.1*,12,0.9*,12,340"), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var header = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock { Text = "素材袋与剧情节点", FontSize = 24, FontWeight = FontWeight.Bold });
        header.Children.Add(Note("勾选连续句子，拖动 ⠿ 收进中间的袋子；将片段卡片拖回左侧，可插入到目标句子前后。所有修改在应用后写回工程。"));
        Grid.SetColumnSpan(header, 5); grid.Children.Add(header);

        var mainLayout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        mainLayout.Children.Add(Caption("主线 · 选择连续句子"));
        var mainActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 10) };
        mainActions.Children.Add(ActionButton("收纳所选 →", StashSelection, true));
        mainActions.Children.Add(ActionButton("清除选择", () => { _selection.Clear(); Render(); }));
        Grid.SetRow(mainActions, 1); mainLayout.Children.Add(mainActions);
        var mainScroll = new ScrollViewer { Content = _main, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(mainScroll, 2); mainLayout.Children.Add(mainScroll);
        _mainPanel.Child = mainLayout; Grid.SetRow(_mainPanel, 1); grid.Children.Add(_mainPanel);

        var bagLayout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*") };
        bagLayout.Children.Add(Caption("素材袋 · 剧情片段节点"));
        var add = ActionButton("＋ 新建片段", () =>
        {
            Snapshot();
            var fragment = new StoryFragment { Title = "新片段", Segments = [new Segment { Text = "在这里写入片段正文。" }] };
            _project.Fragments.Add(fragment); _fragmentId = fragment.Id; _sentenceId = fragment.Segments[0].Id; Render();
        });
        add.Margin = new Thickness(0, 10, 0, 8); Grid.SetRow(add, 1); bagLayout.Children.Add(add);
        var help = Note("↓ 将主线文本拖到此区域收纳。\n袋中片段只在选项跳转时播放；结束后继续它的后续连接。");
        help.Margin = new Thickness(0, 0, 0, 10); Grid.SetRow(help, 2); bagLayout.Children.Add(help);
        var bagScroll = new ScrollViewer { Content = _bag, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(bagScroll, 3); bagLayout.Children.Add(bagScroll);
        _bagPanel.Child = bagLayout; Grid.SetColumn(_bagPanel, 2); Grid.SetRow(_bagPanel, 1); grid.Children.Add(_bagPanel);

        var editor = Panel(); editor.Child = new ScrollViewer { Content = _details, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetColumn(editor, 4); Grid.SetRow(editor, 1); grid.Children.Add(editor);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 14, 0, 0) };
        footer.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        actions.Children.Add(ActionButton("撤销袋内操作", () =>
        {
            if (_history.Count == 0) return;
            _commit = null; _project = JsonSerializer.Deserialize<ProjectDocument>(_history.Pop())!;
            _selection.RemoveWhere(id => !_project.Segments.Any(segment => segment.Id == id)); Render();
        }));
        actions.Children.Add(ActionButton("取消", () => Close(false)));
        actions.Children.Add(ActionButton("应用素材袋", () =>
        {
            Commit(); StoryGraphValidator.Validate(_project.Segments, _project.ChoiceTrees, _project.Fragments);
            HasChanges = JsonSerializer.Serialize(_project) != _initialJson;
            EditedProject = _project; Close(true);
        }, true));
        Grid.SetColumn(actions, 1); footer.Children.Add(actions); Grid.SetRow(footer, 2); Grid.SetColumnSpan(footer, 5); grid.Children.Add(footer);
        Content = grid;
    }

    private void Render()
    {
        _main.Children.Clear(); _bag.Children.Clear(); _mainCards.Clear();
        foreach (var (sentence, index) in _project.Segments.Select((sentence, index) => (sentence, index)))
        {
            var row = new Border { Background = Brush.Parse("#F7F9F6"), BorderBrush = Brush.Parse("#E1E8DF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(8) };
            row.Classes.Add("motion-card");
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            var check = new CheckBox { IsChecked = _selection.Contains(sentence.Id), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) _selection.Add(sentence.Id); else _selection.Remove(sentence.Id); };
            content.Children.Add(check);
            var body = new StackPanel { Spacing = 4 };
            body.Children.Add(Note($"{index + 1:00} · {sentence.SpeakerLabel}"));
            body.Children.Add(new TextBlock { Text = sentence.Preview, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(body, 1); content.Children.Add(body);
            var handle = new Border { Padding = new Thickness(10), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = "⠿", FontSize = 21 } };
            Grid.SetColumn(handle, 2); content.Children.Add(handle); row.Child = content;
            AttachDrag(handle, e =>
            {
                if (!Inside(_bagPanel, e)) return;
                if (!_selection.Contains(sentence.Id)) { _selection.Clear(); _selection.Add(sentence.Id); }
                StashSelection();
            });
            _mainCards.Add((row, index)); _main.Children.Add(row);
        }
        _main.Children.Add(Note("拖到最后一句下方：将素材放在主线末尾。"));
        foreach (var fragment in _project.Fragments)
        {
            var selected = fragment.Id == _fragmentId;
            var card = new Border
            {
                Background = Brush.Parse(selected ? "#DCE8FB" : "#EEF3FC"), BorderBrush = Brush.Parse(selected ? "#527EB9" : "#A7BCDD"),
                BorderThickness = new Thickness(selected ? 2 : 1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Cursor = new Cursor(StandardCursorType.Hand)
            };
            var body = new StackPanel { Spacing = 6 };
            body.Children.Add(Caption("▣ " + fragment.Title));
            body.Children.Add(Note($"{fragment.Segments.Count} 句 · {fragment.ChoiceTrees.Count} 棵选项树"));
            body.Children.Add(new TextBlock { Text = StoryTargets.Short(fragment.Segments[0].Text, 75), TextWrapping = TextWrapping.Wrap, MaxLines = 3 });
            var next = StoryTargets.For(_project).FirstOrDefault(target => target.Id == fragment.NextNodeId)?.Name;
            body.Children.Add(Note(next is null ? "后续：剧情结束" : "后续 → " + next));
            card.Child = body;
            AttachDrag(card, e =>
            {
                if (!Inside(_mainPanel, e)) return;
                Restore(fragment.Id, DropIndex(e));
            }, () => { Commit(); _fragmentId = fragment.Id; _sentenceId = fragment.Segments[0].Id; Render(); });
            _bag.Children.Add(card);
        }
        if (_project.Fragments.Count == 0) _bag.Children.Add(Note("袋子还是空的。收纳文字后会在这里出现节点卡片。"));
        RenderDetails();
        UiMotion.Reveal(_bag);
        _status.Text = $"主线 {_project.Segments.Count} 句 · 素材袋 {_project.Fragments.Count} 个片段。收纳不修改原稿，可在应用后使用主窗口撤销。";
        _status.Foreground = Brush.Parse("#64786D");
    }

    private void StashSelection()
    {
        Commit();
        // Validate before recording history; failed moves leave the project untouched.
        var snapshot = JsonSerializer.Serialize(_project);
        var fragment = StoryFragmentEditor.Stash(_project, _selection.ToList());
        _history.Push(snapshot); _selection.Clear(); _fragmentId = fragment.Id; _sentenceId = fragment.Segments[0].Id; Render();
    }
    private void Restore(string fragmentId, int index)
    {
        Snapshot(); StoryFragmentEditor.Restore(_project, fragmentId, index); _fragmentId = null; _sentenceId = null; Render();
    }
    private int DropIndex(PointerEventArgs e)
    {
        foreach (var (card, index) in _mainCards)
            if (Inside(card, e)) return index + (e.GetPosition(card).Y >= card.Bounds.Height / 2 ? 1 : 0);
        return _project.Segments.Count;
    }
    private static bool Inside(Control control, PointerEventArgs e)
    {
        var p = e.GetPosition(control);
        return p.X >= 0 && p.Y >= 0 && p.X <= control.Bounds.Width && p.Y <= control.Bounds.Height;
    }
    private void AttachDrag(Control card, Action<PointerEventArgs> drop, Action? click = null)
    {
        card.Classes.Add("motion-card");
        Point? start = null; var dragged = false;
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            start = e.GetPosition(this); dragged = false; e.Pointer.Capture(card); e.Handled = true;
        };
        card.PointerMoved += (_, e) =>
        {
            if (start is not { } origin) return;
            var delta = e.GetPosition(this) - origin;
            if (!dragged && Math.Abs(delta.X) + Math.Abs(delta.Y) < 8) return;
            dragged = true; card.Opacity = 0.65;
            _bagPanel.BorderBrush = Brush.Parse(Inside(_bagPanel, e) ? "#167456" : "#DDE8DC");
            _mainPanel.BorderBrush = Brush.Parse(Inside(_mainPanel, e) ? "#527EB9" : "#DDE8DC");
            _status.Text = Inside(_mainPanel, e) ? $"放回位置：第 {DropIndex(e) + 1} 句之前。" : "将正文拖入袋子，或将片段拖回主线。";
        };
        card.PointerReleased += (_, e) =>
        {
            if (start is null) return;
            start = null; card.Opacity = 1; e.Pointer.Capture(null);
            _bagPanel.BorderBrush = _mainPanel.BorderBrush = Brush.Parse("#DDE8DC");
            Attempt(() => { if (dragged) drop(e); else click?.Invoke(); }); e.Handled = true;
        };
        card.PointerCaptureLost += (_, _) =>
        {
            start = null; card.Opacity = 1;
            _bagPanel.BorderBrush = _mainPanel.BorderBrush = Brush.Parse("#DDE8DC");
        };
    }

    private void RenderDetails()
    {
        UiMotion.Reveal(_details);
        _details.Children.Clear(); _commit = null;
        var fragment = _project.Fragments.FirstOrDefault(item => item.Id == _fragmentId);
        if (fragment is null) { _details.Children.Add(Caption("片段编辑")); _details.Children.Add(Note("点击素材袋中的卡片，修改名称、文字、角色与后续连接。")); return; }
        _details.Children.Add(Caption("片段名称"));
        var title = new TextBox { Text = fragment.Title }; _details.Children.Add(title);
        _details.Children.Add(Caption("片段结束后继续到"));
        var targets = new List<StoryTarget> { new("", "剧情结束") };
        targets.AddRange(StoryTargets.For(_project).Where(target => target.Id != fragment.Id));
        var next = new ComboBox { ItemsSource = targets, SelectedItem = targets.FirstOrDefault(target => target.Id == fragment.NextNodeId) ?? targets[0], HorizontalAlignment = HorizontalAlignment.Stretch };
        _details.Children.Add(next);
        _details.Children.Add(Note("收纳时自动连接到原片段之后的句子。也可指定其他节点继续剧情。"));
        _details.Children.Add(ActionButton("编辑片段选项树", () => OpenFragmentChoices(fragment)));
        _details.Children.Add(Caption("片段内的句子"));
        foreach (var (sentence, index) in fragment.Segments.Select((sentence, index) => (sentence, index)))
        {
            var select = ActionButton($"{index + 1}. {StoryTargets.Short(sentence.Text, 27)}", () => { Commit(); _sentenceId = sentence.Id; RenderDetails(); });
            select.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (sentence.Id == _sentenceId) select.Classes.Add("soft");
            _details.Children.Add(select);
        }
        var selected = fragment.Segments.FirstOrDefault(sentence => sentence.Id == _sentenceId) ?? fragment.Segments[0];
        _sentenceId = selected.Id;
        var kind = new ComboBox { ItemsSource = new[] { "旁白/人物动作或其他", "对白", "场景 / 舞台说明" }, SelectedIndex = (int)selected.Kind, HorizontalAlignment = HorizontalAlignment.Stretch };
        var speaker = new TextBox { Text = selected.Speaker, Watermark = "说话人（对白时使用）" };
        var text = new TextBox { Text = selected.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140, MaxHeight = 260 };
        var include = new CheckBox { Content = "包含在导出中", IsChecked = selected.Include };
        _details.Children.Add(kind); _details.Children.Add(speaker); _details.Children.Add(text); _details.Children.Add(include);
        _commit = () =>
        {
            fragment.Title = string.IsNullOrWhiteSpace(title.Text) ? "未命名片段" : title.Text.Trim();
            fragment.NextNodeId = (next.SelectedItem as StoryTarget)?.Id ?? "";
            var newKind = (SegmentKind)Math.Max(0, kind.SelectedIndex);
            var newSpeaker = newKind == SegmentKind.Dialogue ? (speaker.Text ?? "").Trim() : "";
            if (selected.Text != text.Text || selected.Kind != newKind || selected.Speaker != newSpeaker) selected.Reviewed = false;
            selected.Text = text.Text ?? ""; selected.Kind = newKind; selected.Speaker = newSpeaker; selected.Include = include.IsChecked == true;
        };
        _details.Children.Add(ActionButton("更新片段", () => { Commit(); Render(); }, true));
        var move = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var direction in new[] { -1, 1 })
            move.Children.Add(ActionButton(direction < 0 ? "↑ 上移此句" : "↓ 下移此句", () =>
            {
                var index = fragment.Segments.IndexOf(selected); var destination = index + direction;
                if (destination < 0 || destination >= fragment.Segments.Count) return;
                Snapshot(); (fragment.Segments[index], fragment.Segments[destination]) = (fragment.Segments[destination], fragment.Segments[index]); Render();
            }));
        _details.Children.Add(move);
        _details.Children.Add(ActionButton("取出到所选正文之前", () => Restore(fragment.Id,
            _project.Segments.FindIndex(sentence => _selection.Contains(sentence.Id)) is var index && index >= 0 ? index : _project.Segments.Count)));
        _details.Children.Add(ActionButton("取出到主线末尾", () => Restore(fragment.Id, _project.Segments.Count)));
        _details.Children.Add(ActionButton("删除片段", () => ConfirmDelete(fragment)));
    }

    private async void OpenFragmentChoices(StoryFragment fragment)
    {
        try
        {
            Commit();
            var window = new ChoiceTreeWindow(_project, _sentenceId, routeFragmentId: fragment.Id);
            if (!await window.ShowDialog<bool>(this) || !window.HasChanges) { RenderDetails(); return; }
            Snapshot(); fragment.ChoiceTrees = window.EditedTrees!; Render();
        }
        catch (Exception ex) { _status.Text = "操作失败：" + ex.Message; if (ex is not UserFacingException) ErrorLog.Write(ex); }
    }

    private async void ConfirmDelete(StoryFragment fragment)
    {
        try
        {
            Commit();
            var dialog = new Window { Title = "删除素材片段？", Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var content = new StackPanel { Spacing = 15, Margin = new Thickness(22) };
            content.Children.Add(new TextBlock { Text = $"删除“{fragment.Title}”及其中的 {fragment.Segments.Count} 句和选项树？原始稿件会保留，可在此窗口撤销删除。", TextWrapping = TextWrapping.Wrap });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var cancel = new Button { Content = "取消" }; cancel.Click += (_, _) => dialog.Close(false);
            var remove = new Button { Content = "删除片段" }; remove.Click += (_, _) => dialog.Close(true);
            actions.Children.Add(cancel); actions.Children.Add(remove); content.Children.Add(actions); dialog.Content = content;
            UiMotion.Attach(dialog);
            if (!await dialog.ShowDialog<bool>(this)) { RenderDetails(); return; }
            var snapshot = JsonSerializer.Serialize(_project);
            StoryFragmentEditor.Delete(_project, fragment.Id); _history.Push(snapshot);
            _fragmentId = _sentenceId = null; Render();
        }
        catch (Exception ex) { _status.Text = "操作失败：" + ex.Message; if (ex is not UserFacingException) ErrorLog.Write(ex); }
    }
}
