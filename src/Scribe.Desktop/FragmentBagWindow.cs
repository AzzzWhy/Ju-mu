using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Scribe.Core;

namespace Scribe.Desktop;

/// <summary>A private editing session for moving and editing whole story fragments.</summary>
public sealed class FragmentBagWindow : Window
{
    private ProjectDocument _project;
    private readonly string _initialJson;
    private readonly Stack<string> _history = new();
    private readonly HashSet<string> _bagSelection = [];
    private readonly Button _deleteSelected = new() { Content = "删除所选素材", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _main = new() { Spacing = 7 };
    private readonly StackPanel _bag = new() { Spacing = 10 };
    private readonly StackPanel _details = new() { Spacing = 10 };
    private readonly Border _mainPanel = Panel();
    private readonly Border _bagPanel = Panel();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#64786D") };
    private Border? _mainCard;
    private string? _fragmentId;
    private string? _sentenceId;
    private bool _lineEditor;
    private Action? _commit;
    public ProjectDocument? EditedProject { get; private set; }
    public bool HasChanges { get; private set; }

    public FragmentBagWindow(ProjectDocument project)
    {
        _initialJson = JsonSerializer.Serialize(project);
        _project = JsonSerializer.Deserialize<ProjectDocument>(_initialJson)!;
        Title = "素材袋与剧情节点 · 句幕";
        Width = 1450; Height = 900; MinWidth = 1170; MinHeight = 680;
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
        var grid = new Grid { Margin = new Thickness(20), ColumnDefinitions = new ColumnDefinitions("1*,12,0.9*,12,430"), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var header = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock { Text = "素材袋与剧情节点", FontSize = 24, FontWeight = FontWeight.Bold });
        header.Children.Add(Note("袋子存放整块文本，不逐句收纳。可以新建/导入文本，或收纳已校正的整篇正文；每份文本只有一张节点卡片，供选项树连接跳转。"));
        Grid.SetColumnSpan(header, 5); grid.Children.Add(header);

        var mainLayout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        mainLayout.Children.Add(Caption("当前已校正正文 · 整篇文本"));
        var mainActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 10) };
        var stash = ActionButton("整篇收纳 →", StashMain, true);
        mainActions.Children.Add(stash);
        Grid.SetRow(mainActions, 1); mainLayout.Children.Add(mainActions);
        var mainScroll = new ScrollViewer { Content = _main, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(mainScroll, 2); mainLayout.Children.Add(mainScroll);
        _mainPanel.Child = mainLayout; Grid.SetRow(_mainPanel, 1); grid.Children.Add(_mainPanel);

        var bagLayout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*") };
        bagLayout.Children.Add(Caption("素材袋 · 完整文本节点"));
        var add = ActionButton("＋ 新建 / 导入文本", OpenNewText);
        add.Margin = new Thickness(0, 10, 0, 8); Grid.SetRow(add, 1); bagLayout.Children.Add(add);
        var help = Note("每张卡片是一份完整文本，可包含多段正文和角色对白。拖到选项树中的选项建立跳转；也可整块拖回左侧正文。");
        help.Margin = new Thickness(0, 0, 0, 10); Grid.SetRow(help, 2); bagLayout.Children.Add(help);
        var bagActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        bagActions.Children.Add(ActionButton("全选素材", () => { Commit(); _bagSelection.UnionWith(_project.Fragments.Select(fragment => fragment.Id)); Render(); }));
        bagActions.Children.Add(ActionButton("清除多选", () => { Commit(); _bagSelection.Clear(); _fragmentId = null; Render(); }));
        bagActions.Children.Add(ActionButton("删除所选", () => ConfirmDelete(_bagSelection.ToList())));
        Grid.SetRow(bagActions, 3); bagLayout.Children.Add(bagActions);
        var bagScroll = new ScrollViewer { Content = _bag, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(bagScroll, 4); bagLayout.Children.Add(bagScroll);
        _bagPanel.Child = bagLayout; Grid.SetColumn(_bagPanel, 2); Grid.SetRow(_bagPanel, 1); grid.Children.Add(_bagPanel);

        var editor = Panel();
        var editorLayout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        _deleteSelected.Margin = new Thickness(0, 0, 0, 10);
        _deleteSelected.Click += (_, _) => ConfirmDelete(_bagSelection.ToList());
        editorLayout.Children.Add(_deleteSelected);
        var editorScroll = new ScrollViewer { Content = _details, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(editorScroll, 1); editorLayout.Children.Add(editorScroll); editor.Child = editorLayout;
        Grid.SetColumn(editor, 4); Grid.SetRow(editor, 1); grid.Children.Add(editor);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 14, 0, 0) };
        footer.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        actions.Children.Add(ActionButton("撤销袋内操作", () =>
        {
            if (_history.Count == 0) return;
            _commit = null; _project = JsonSerializer.Deserialize<ProjectDocument>(_history.Pop())!;
            Render();
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
        _main.Children.Clear(); _bag.Children.Clear(); _mainCard = null;
        _bagSelection.IntersectWith(_project.Fragments.Select(fragment => fragment.Id));
        _deleteSelected.IsEnabled = _bagSelection.Count > 0;
        _deleteSelected.Content = _bagSelection.Count > 0 ? $"删除所选 {_bagSelection.Count} 份素材" : "删除所选素材";
        if (_project.Segments.Count > 0)
        {
            var row = new Border { Background = Brush.Parse("#F7F9F6"), BorderBrush = Brush.Parse("#E1E8DF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12) };
            row.Classes.Add("motion-card");
            var body = new StackPanel { Spacing = 12 };
            var handle = new Border { Padding = new Thickness(8), Cursor = new Cursor(StandardCursorType.Hand), Child = Caption("⠿  整篇正文 · 拖入素材袋") };
            body.Children.Add(handle);
            body.Children.Add(Note($"{_project.Segments.Sum(segment => segment.Text.Length)} 字 · {_project.ChoiceTrees.Count} 棵选项树。保留已修改文字、角色、确认状态和连接。"));
            body.Children.Add(new TextBlock { Text = string.Join("\n\n", _project.Segments.Select(segment => segment.Text)), TextWrapping = TextWrapping.Wrap });
            row.Child = body;
            AttachDrag(handle, e =>
            {
                if (!Inside(_bagPanel, e)) return;
                StashMain();
            });
            _mainCard = row; _main.Children.Add(row);
        }
        else _main.Children.Add(Note("当前正文为空。可以新建多份素材文本；将卡片拖回来或点击取出，作为正文继续编辑。"));
        _main.Children.Add(Note("拖到正文卡片上半部：插入整篇文本之前；下半部或空白处：追加到末尾。"));
        foreach (var fragment in _project.Fragments)
        {
            var selected = _bagSelection.Contains(fragment.Id);
            var card = new Border
            {
                Background = Brush.Parse(selected ? "#DCE8FB" : "#EEF3FC"), BorderBrush = Brush.Parse(selected ? "#527EB9" : "#A7BCDD"),
                BorderThickness = new Thickness(selected ? 2 : 1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Cursor = new Cursor(StandardCursorType.Hand)
            };
            var body = new StackPanel { Spacing = 6 };
            var check = new CheckBox { Content = "▣ " + fragment.Title, IsChecked = selected };
            check.IsCheckedChanged += (_, _) => Attempt(() =>
            {
                Commit();
                if (check.IsChecked == true) _bagSelection.Add(fragment.Id); else _bagSelection.Remove(fragment.Id);
                _fragmentId = _bagSelection.Contains(fragment.Id) ? fragment.Id : _bagSelection.FirstOrDefault();
                _sentenceId = null; _lineEditor = false; Render();
            });
            body.Children.Add(check);
            body.Children.Add(Note($"{fragment.Segments.Sum(segment => segment.Text.Length)} 字 · {fragment.ChoiceTrees.Count} 棵选项树 · 一份完整文本"));
            body.Children.Add(new TextBlock { Text = StoryTargets.Short(StoryFragmentEditor.TextOf(fragment), 120), TextWrapping = TextWrapping.Wrap, MaxLines = 3 });
            var next = StoryTargets.For(_project).FirstOrDefault(target => target.Id == fragment.NextNodeId)?.Name;
            body.Children.Add(Note(next is null ? "后续：剧情结束" : "后续 → " + next));
            card.Child = body;
            AttachDrag(card, e =>
            {
                if (!Inside(_mainPanel, e)) return;
                Restore(fragment.Id, DropIndex(e));
            }, e =>
            {
                Commit();
                if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
                { if (!_bagSelection.Add(fragment.Id)) _bagSelection.Remove(fragment.Id); }
                else { _bagSelection.Clear(); _bagSelection.Add(fragment.Id); }
                _fragmentId = _bagSelection.Contains(fragment.Id) ? fragment.Id : _bagSelection.FirstOrDefault();
                _sentenceId = null; _lineEditor = false; Render();
            });
            var remove = new MenuItem { Header = "删除所选素材…" };
            remove.Click += (_, _) => ConfirmDelete(_bagSelection.ToList());
            card.ContextMenu = new ContextMenu { ItemsSource = new[] { remove } };
            card.ContextMenu.Opened += (_, _) => _status.Text = "右键菜单已打开：可删除所选素材，删除前会再次确认。";
            card.ContextMenu.Closed += (_, _) => Render();
            card.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(card).Properties.IsRightButtonPressed) return;
                Attempt(() =>
                {
                    Commit();
                    if (!_bagSelection.Contains(fragment.Id))
                    {
                        _bagSelection.Clear(); _bagSelection.Add(fragment.Id); _fragmentId = fragment.Id;
                        _sentenceId = null; _lineEditor = false; RenderDetails();
                        _deleteSelected.IsEnabled = true; _deleteSelected.Content = "删除所选 1 份素材";
                        card.BorderBrush = Brush.Parse("#527EB9"); card.BorderThickness = new Thickness(2);
                    }
                    remove.Header = $"删除所选 {_bagSelection.Count} 份素材…";
                });
                e.Handled = true;
            };
            card.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Right) return;
                e.Handled = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => card.ContextMenu?.Open(card));
            };
            _bag.Children.Add(card);
        }
        if (_project.Fragments.Count == 0) _bag.Children.Add(Note("袋子还是空的。新建文本，或整篇收纳已校正正文，即可生成文本节点卡片。"));
        RenderDetails();
        UiMotion.Reveal(_bag);
        _status.Text = $"素材袋 {_project.Fragments.Count} 份文本 · 已选 {_bagSelection.Count} 份。勾选 / Ctrl（Mac Cmd）点击多选；支持右键删除和右侧删除按钮。";
        _status.Foreground = Brush.Parse("#64786D");
    }

    private void StashMain()
    {
        Commit();
        // Validate before recording history; failed moves leave the project untouched.
        var snapshot = JsonSerializer.Serialize(_project);
        var fragment = StoryFragmentEditor.StashMain(_project, string.IsNullOrWhiteSpace(_project.SourceFile) ? "已校正文本" : Path.GetFileNameWithoutExtension(_project.SourceFile));
        _history.Push(snapshot); _bagSelection.Clear(); _bagSelection.Add(fragment.Id); _fragmentId = fragment.Id; _sentenceId = fragment.Segments[0].Id; Render();
    }
    private void Restore(string fragmentId, int index)
    {
        Snapshot(); StoryFragmentEditor.Restore(_project, fragmentId, index); _fragmentId = null; _sentenceId = null; Render();
    }
    private int DropIndex(PointerEventArgs e)
    {
        if (_mainCard is { } card && Inside(card, e) && e.GetPosition(card).Y < card.Bounds.Height / 2) return 0;
        return _project.Segments.Count;
    }
    private static bool Inside(Control control, PointerEventArgs e)
    {
        var p = e.GetPosition(control);
        return p.X >= 0 && p.Y >= 0 && p.X <= control.Bounds.Width && p.Y <= control.Bounds.Height;
    }
    private void AttachDrag(Control card, Action<PointerEventArgs> drop, Action<PointerEventArgs>? click = null)
    {
        card.Classes.Add("motion-card");
        Point? start = null; var dragged = false;
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            if (e.Source is Visual visual && visual.GetVisualAncestors().Prepend(visual).TakeWhile(item => !ReferenceEquals(item, card)).Any(item => item is CheckBox or Button)) return;
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
            _status.Text = Inside(_mainPanel, e) ? (DropIndex(e) == 0 ? "放回位置：正文开头。" : "放回位置：正文末尾。") : "整篇正文拖入袋子，或整块文本拖回主线。";
        };
        card.PointerReleased += (_, e) =>
        {
            if (start is null) return;
            start = null; card.Opacity = 1; e.Pointer.Capture(null);
            _bagPanel.BorderBrush = _mainPanel.BorderBrush = Brush.Parse("#DDE8DC");
            Attempt(() => { if (dragged) drop(e); else click?.Invoke(e); }); e.Handled = true;
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
        if (_bagSelection.Count > 1)
        {
            _details.Children.Add(Caption($"已选择 {_bagSelection.Count} 份完整文本"));
            _details.Children.Add(Note("可用右侧顶部按钮、右键菜单或素材列表上方按钮批量删除。若只需编辑一份，点击该卡片正文以退出多选。"));
            return;
        }
        var fragment = _project.Fragments.FirstOrDefault(item => item.Id == _fragmentId);
        if (fragment is null) { _details.Children.Add(Caption("完整文本编辑")); _details.Children.Add(Note("新建或点击一张文本卡片，在这里编辑整份文本与后续连接。不是为每句话创建一张卡片。")); return; }
        _details.Children.Add(Caption("文本名称"));
        var title = new TextBox { Text = fragment.Title }; _details.Children.Add(title);
        _details.Children.Add(Caption("片段结束后继续到"));
        var next = new StoryTargetPicker(_project, fragment.NextNodeId, "剧情结束", fragment.Id, fragment.Id);
        _details.Children.Add(next);
        _details.Children.Add(Note("文本作为一个节点连接选项树；内部的对话拆分仅用于 Ren’Py 播放和精细校正。"));
        _details.Children.Add(ActionButton("编辑本文选项树", () => OpenFragmentChoices(fragment)));
        _details.Children.Add(ActionButton(_lineEditor ? "← 返回完整文本编辑" : "展开逐句校正（角色 / 内容类型）", () => { Commit(); _lineEditor = !_lineEditor; RenderDetails(); }));
        if (!_lineEditor)
        {
            _details.Children.Add(Caption("完整文本"));
            _details.Children.Add(Note("空行分隔内部对话块；修改已有块保留角色和跳转位置，增加空行可添加新块。"));
            var fullText = new TextBox { Text = StoryFragmentEditor.TextOf(fragment), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 300, MaxHeight = 440 };
            _details.Children.Add(fullText);
            _commit = () =>
            {
                if ((title.Text?.Length ?? 0) > 4096) throw new UserFacingException("文本名称过长，请缩短名称。");
                StoryFragmentEditor.UpdateText(_project, fragment, fullText.Text ?? "");
                fragment.Title = string.IsNullOrWhiteSpace(title.Text) ? "未命名文本" : title.Text.Trim();
                fragment.NextNodeId = next.SelectedId;
            };
        }
        else
        {
            _details.Children.Add(Caption("本文内部的对话块（不单独存入素材袋）"));
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
                if ((title.Text?.Length ?? 0) > 4096) throw new UserFacingException("文本名称过长，请缩短名称。");
                fragment.Title = string.IsNullOrWhiteSpace(title.Text) ? "未命名文本" : title.Text.Trim();
                fragment.NextNodeId = next.SelectedId;
                var newKind = (SegmentKind)Math.Max(0, kind.SelectedIndex);
                var newSpeaker = newKind == SegmentKind.Dialogue ? (speaker.Text ?? "").Trim() : "";
                if (selected.Text != text.Text || selected.Kind != newKind || selected.Speaker != newSpeaker) selected.Reviewed = false;
                selected.Text = text.Text ?? ""; selected.Kind = newKind; selected.Speaker = newSpeaker; selected.Include = include.IsChecked == true;
            };
        }
        _details.Children.Add(ActionButton("更新整份文本", () => { Commit(); Render(); }, true));
        _details.Children.Add(ActionButton("整块取出到正文开头", () => Restore(fragment.Id, 0)));
        _details.Children.Add(ActionButton("整块取出到正文末尾", () => Restore(fragment.Id, _project.Segments.Count)));
        _details.Children.Add(ActionButton("删除整份文本", () => ConfirmDelete([fragment.Id])));
    }

    private async void OpenNewText()
    {
        try
        {
            Commit();
            var dialog = new Window { Title = "添加一份完整文本", Width = 760, Height = 700, MinHeight = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
            var layout = new Grid { Margin = new Thickness(22), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto") };
            var title = new TextBox { Watermark = "文本名称，例如：第一章 / 雨夜路线", Margin = new Thickness(0, 8) };
            var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Watermark = "在这里输入或粘贴整份文本，也可以导入 TXT、DOCX、Markdown。", Margin = new Thickness(0, 10) };
            var split = new ComboBox { ItemsSource = new[] { "内部按段落播放", "内部按句子播放", "内部按阅读长度播放" }, SelectedIndex = (int)_project.Options.Split };
            var hint = Note("无论内部拆分成多少条对话，素材袋中都只创建一张文本卡片。新文本默认旁白，可后续逐句校正角色。");
            layout.Children.Add(Caption("新建 / 导入完整文本"));
            Grid.SetRow(title, 1); layout.Children.Add(title);
            var import = new Button { Content = "导入文本文件…" };
            import.Click += async (_, _) =>
            {
                try
                {
                    var files = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "导入到素材袋", FileTypeFilter = [new("稿件（TXT / DOCX / Markdown）") { Patterns = ["*.txt", "*.text", "*.docx", "*.md", "*.markdown"] }] });
                    if (files.Count == 0) return;
                    if (files[0].TryGetLocalPath() is not { } path) throw new UserFacingException("请选择本地文件。");
                    if (Path.GetExtension(path).Equals(".rpy", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".rpyc", StringComparison.OrdinalIgnoreCase))
                        throw new UserFacingException("RPY 需要读取剧情结构，请在主界面使用「导入 .rpy」。");
                    var document = await Task.Run(() => DocumentImporter.Read(path));
                    text.Text = document.Text; title.Text = Path.GetFileNameWithoutExtension(document.FileName);
                    hint.Text = "文件已读入，点击添加后生成一张文本卡片。" + string.Join("；", document.Warnings);
                }
                catch (Exception ex) { hint.Text = ex.Message; if (ex is not UserFacingException) ErrorLog.Write(ex); }
            };
            Grid.SetRow(import, 2); layout.Children.Add(import);
            Grid.SetRow(text, 3); layout.Children.Add(text);
            var options = new StackPanel { Spacing = 8 }; options.Children.Add(split); options.Children.Add(hint); Grid.SetRow(options, 4); layout.Children.Add(options);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Margin = new Thickness(0, 14, 0, 0) };
            var cancel = new Button { Content = "取消" }; cancel.Click += (_, _) => dialog.Close(false);
            var add = new Button { Content = "添加整份文本", Classes = { "primary" } };
            add.Click += (_, _) =>
            {
                try
                {
                    var before = JsonSerializer.Serialize(_project);
                    var fragment = StoryFragmentEditor.AddText(_project, title.Text ?? "", text.Text ?? "", (SplitMode)Math.Max(0, split.SelectedIndex));
                    _history.Push(before); _bagSelection.Clear(); _bagSelection.Add(fragment.Id); _fragmentId = fragment.Id; _sentenceId = fragment.Segments[0].Id; _lineEditor = false;
                    dialog.Close(true);
                }
                catch (UserFacingException ex) { hint.Text = ex.Message; }
            };
            actions.Children.Add(cancel); actions.Children.Add(add); Grid.SetRow(actions, 5); layout.Children.Add(actions); dialog.Content = layout;
            UiMotion.Attach(dialog);
            if (await dialog.ShowDialog<bool>(this)) Render();
        }
        catch (Exception ex) { _status.Text = "操作失败：" + ex.Message; if (ex is not UserFacingException) ErrorLog.Write(ex); }
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

    private async void ConfirmDelete(IReadOnlyCollection<string> ids)
    {
        try
        {
            Commit();
            if (ids.Count == 0) throw new UserFacingException("请先勾选要删除的素材文本。");
            var chosen = ids.ToHashSet(StringComparer.Ordinal);
            var fragments = _project.Fragments.Where(fragment => chosen.Contains(fragment.Id)).ToList();
            var dialog = new Window { Title = "删除所选素材？", Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var content = new StackPanel { Spacing = 15, Margin = new Thickness(22) };
            content.Children.Add(new TextBlock { Text = $"删除所选 {ids.Count} 份完整文本及内部选项树？\n{string.Join("、", fragments.Take(5).Select(fragment => fragment.Title))}\n可在此窗口撤销删除；取消窗口不会写回工程。", TextWrapping = TextWrapping.Wrap });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var cancel = new Button { Content = "取消" }; cancel.Click += (_, _) => dialog.Close(false);
            var remove = new Button { Content = "删除所选" }; remove.Click += (_, _) => dialog.Close(true);
            actions.Children.Add(cancel); actions.Children.Add(remove); content.Children.Add(actions); dialog.Content = content;
            UiMotion.Attach(dialog);
            if (!await dialog.ShowDialog<bool>(this)) { RenderDetails(); return; }
            var snapshot = JsonSerializer.Serialize(_project);
            StoryFragmentEditor.DeleteMany(_project, ids); _history.Push(snapshot);
            _bagSelection.Clear(); _fragmentId = _sentenceId = null; Render();
        }
        catch (Exception ex) { _status.Text = "操作失败：" + ex.Message; if (ex is not UserFacingException) ErrorLog.Write(ex); }
    }
}
