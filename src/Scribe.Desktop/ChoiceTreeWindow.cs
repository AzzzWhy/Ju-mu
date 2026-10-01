using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Scribe.Core;

namespace Scribe.Desktop;

/// <summary>
/// An editor for all menus in a project. It works on a private copy; callers only
/// receive EditedTrees after ShowDialog returns true.
/// </summary>
public sealed class ChoiceTreeWindow : Window
{
    private const double NodeWidth = 218;
    private const double NodeHeight = 82;
    private const double ColumnPitch = 296;
    private const double RowPitch = 118;

    private readonly List<ChoiceTree> _trees;
    private readonly List<Segment> _segments;
    private readonly ProjectDocument _context;
    private readonly string? _routeFragmentId;
    private readonly List<StoryTarget> _targets;
    private readonly List<(Border Card, ChoiceBranch Branch)> _branchCards = [];
    private readonly string _initialJson;
    private readonly string? _initialSegmentId;
    private readonly StackPanel _treeList = new() { Spacing = 8 };
    private readonly Border _endDropZone = new();
    private readonly StackPanel _details = new() { Spacing = 11 };
    private readonly Canvas _canvas = new();
    private Border? _branchEndDropZone;
    private readonly TextBlock _graphHint = new();
    private readonly TextBlock _summary = new();
    private ChoiceTree? _currentTree;
    private object? _selectedModel;
    private Action? _commitDetails;
    private readonly List<GraphNode> _nodes = [];
    private readonly List<(GraphNode From, GraphNode To)> _edges = [];
    private double _nextY;
    private int _maxColumn;

    public List<ChoiceTree>? EditedTrees { get; private set; }
    public bool HasChanges { get; private set; }

    public ChoiceTreeWindow(ProjectDocument project, string? selectedSegmentId = null, ChoicePlacement? createAt = null,
        string? routeFragmentId = null)
    {
        _context = project;
        _routeFragmentId = routeFragmentId;
        var route = project.Fragments.FirstOrDefault(fragment => fragment.Id == routeFragmentId);
        _targets = StoryTargets.For(project);
        _initialJson = JsonSerializer.Serialize(route?.ChoiceTrees ?? project.ChoiceTrees);
        _trees = JsonSerializer.Deserialize<List<ChoiceTree>>(_initialJson) ?? [];
        _segments = (route?.Segments ?? project.Segments).ToList();
        _initialSegmentId = selectedSegmentId;
        _currentTree = _trees.LastOrDefault(t => t.AnchorSegmentId == selectedSegmentId) ?? TreesInStoryOrder().LastOrDefault();
        _selectedModel = _currentTree;
        if (createAt is { } placement && _segments.Any(s => s.Id == selectedSegmentId))
        {
            var created = NewTree();
            created.AnchorSegmentId = selectedSegmentId!;
            created.Placement = placement;
            _trees.Add(created);
            _currentTree = created;
            _selectedModel = created;
        }

        Title = route is null ? "选项树 · 句幕" : $"{route.Title} · 选项树 · 句幕";
        Width = 1380;
        Height = 860;
        MinWidth = 1080;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#F8FAF6");
        BuildWindow();
        RenderAll();
        UiMotion.Attach(this);
    }

    private void BuildWindow()
    {
        var layout = new Grid { Margin = new Thickness(20, 18, 20, 16) };
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        layout.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        layout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(240)));
        layout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        layout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(322)));

        var header = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock { Text = "选项树", FontSize = 24, FontWeight = FontWeight.Bold });
        header.Children.Add(new TextBlock
        {
            Text = "点击节点编辑。将左侧素材片段拖到选项上可建立跳转，也可在右侧选择任意正文句子作为目标。",
            Foreground = Brush.Parse("#64786D"), TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumnSpan(header, 3);
        layout.Children.Add(header);

        var left = Panel();
        left.Margin = new Thickness(0, 0, 12, 0);
        var leftBody = new DockPanel { LastChildFill = true };
        var leftTop = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 10) };
        leftTop.Children.Add(Caption("选项树列表"));
        leftTop.Children.Add(new TextBlock { Text = "每棵树连接到原文中的一句。", Foreground = Brush.Parse("#64786D"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var addTree = Button("＋ 新建选项树", primary: true);
        addTree.Click += (_, _) => AddTopLevel();
        addTree.IsEnabled = _segments.Count > 0;
        leftTop.Children.Add(addTree);
        if (_segments.Count == 0)
            leftTop.Children.Add(new TextBlock { Text = "请先解析稿件，才能选择插入位置。", Foreground = Brush.Parse("#9C6F31"), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        DockPanel.SetDock(leftTop, Dock.Top);
        leftBody.Children.Add(leftTop);
        _endDropZone.Background = Brush.Parse("#EDF5ED");
        _endDropZone.BorderBrush = Brush.Parse("#8EBDA3");
        _endDropZone.BorderThickness = new Thickness(1);
        _endDropZone.Classes.Add("motion-drop");
        _endDropZone.CornerRadius = new CornerRadius(8);
        _endDropZone.Padding = new Thickness(9);
        _endDropZone.Margin = new Thickness(0, 8, 0, 0);
        _endDropZone.Child = new TextBlock
        {
            Text = "⇢ 拖到这里：接在整棵选项树之后（变为下一棵树）",
            FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse("#315D48")
        };
        DockPanel.SetDock(_endDropZone, Dock.Bottom);
        leftBody.Children.Add(_endDropZone);
        leftBody.Children.Add(new ScrollViewer { Content = _treeList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        left.Child = leftBody;
        Grid.SetRow(left, 1);
        layout.Children.Add(left);

        var center = Panel();
        center.Margin = new Thickness(0, 0, 12, 0);
        var centerGrid = new Grid();
        centerGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        centerGrid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        _graphHint.Foreground = Brush.Parse("#64786D");
        _graphHint.FontSize = 12;
        _graphHint.Margin = new Thickness(0, 0, 0, 10);
        _graphHint.TextWrapping = TextWrapping.Wrap;
        centerGrid.Children.Add(_graphHint);
        var graphScroll = new ScrollViewer
        {
            Content = _canvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brush.Parse("#F4F8F3")
        };
        Grid.SetRow(graphScroll, 1);
        centerGrid.Children.Add(graphScroll);
        center.Child = centerGrid;
        Grid.SetRow(center, 1);
        Grid.SetColumn(center, 1);
        layout.Children.Add(center);

        var right = Panel();
        var rightGrid = new Grid();
        rightGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        rightGrid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        rightGrid.Children.Add(Caption("节点编辑"));
        var detailScroll = new ScrollViewer { Content = _details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, 0, 0) };
        Grid.SetRow(detailScroll, 1);
        rightGrid.Children.Add(detailScroll);
        right.Child = rightGrid;
        Grid.SetRow(right, 1);
        Grid.SetColumn(right, 2);
        layout.Children.Add(right);

        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _summary.Foreground = Brush.Parse("#64786D");
        _summary.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(_summary);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Button("取消");
        cancel.Click += (_, _) => Close(false);
        var apply = Button("应用选项树", primary: true);
        apply.Click += (_, _) =>
        {
            CommitDetails();
            try
            {
                var copy = JsonSerializer.Deserialize<ProjectDocument>(JsonSerializer.Serialize(_context))!;
                if (_routeFragmentId is null) copy.ChoiceTrees = _trees;
                else copy.Fragments.Single(fragment => fragment.Id == _routeFragmentId).ChoiceTrees = _trees;
                StoryGraphValidator.Validate(copy.Segments, copy.ChoiceTrees, copy.Fragments);
            }
            catch (UserFacingException ex)
            {
                _summary.Text = "无法应用：" + ex.Message;
                _summary.Foreground = Brush.Parse("#B3403A");
                return;
            }
            HasChanges = JsonSerializer.Serialize(_trees) != _initialJson;
            EditedTrees = _trees;
            Close(true);
        };
        actions.Children.Add(cancel);
        actions.Children.Add(apply);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 2);
        Grid.SetColumnSpan(footer, 3);
        layout.Children.Add(footer);
        Content = layout;
    }

    private static Border Panel() => new()
    {
        Background = Brushes.White,
        BorderBrush = Brush.Parse("#DDE8DC"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(14)
    };

    private static TextBlock Caption(string text) => new()
    {
        Text = text, FontSize = 12, FontWeight = FontWeight.Bold,
        Foreground = Brush.Parse("#496556"), LetterSpacing = 1
    };

    private static Button Button(string text, bool primary = false)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary) button.Classes.Add("primary");
        return button;
    }

    private void RenderAll()
    {
        _endDropZone.IsVisible = _trees.Count > 0;
        RenderTreeList();
        RenderGraph();
        RenderDetails();
        _summary.Text = $"{_trees.Count} 棵选项树  ·  {_trees.Sum(CountBranches)} 个选项  ·  所有编辑在点击「应用选项树」后才写回工程";
    }

    private static int CountBranches(ChoiceTree tree) =>
        tree.Branches.Count + tree.Branches.Sum(b => b.Items.Where(i => i.Tree != null).Sum(i => CountBranches(i.Tree!)));

    private string AnchorLabel(ChoiceTree tree)
    {
        var index = _segments.FindIndex(s => s.Id == tree.AnchorSegmentId);
        return index < 0 ? "锚点已失效" : $"第 {index + 1} 句 · {(_segments[index].Text.Length > 18 ? _segments[index].Text[..18] + "…" : _segments[index].Text)}";
    }

    private IEnumerable<ChoiceTree> TreesInStoryOrder() => _trees.Select((tree, insertionIndex) => new
        {
            Tree = tree,
            InsertionIndex = insertionIndex,
            SegmentIndex = _segments.FindIndex(s => s.Id == tree.AnchorSegmentId)
        })
        .OrderBy(x => x.SegmentIndex)
        .ThenBy(x => x.Tree.Placement)
        .ThenBy(x => x.InsertionIndex)
        .Select(x => x.Tree);

    private void RenderTreeList()
    {
        _treeList.Children.Clear();
        _endDropZone.IsVisible = _trees.Count > 0;
        if (_trees.Count == 0)
        {
            _treeList.Children.Add(new TextBlock { Text = "还没有选项树。点击上方按钮创建。", TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#64786D"), Margin = new Thickness(2, 8) });
            RenderFragmentPalette();
            return;
        }
        foreach (var (tree, index) in TreesInStoryOrder().Select((tree, index) => (tree, index)))
        {
            var selected = ReferenceEquals(tree, _currentTree);
            var card = new Border
            {
                Background = Brush.Parse(selected ? "#E7F2EA" : "#F7F9F6"),
                BorderBrush = Brush.Parse(selected ? "#368469" : "#E3EAE0"),
                BorderThickness = new Thickness(selected ? 2 : 1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            var body = new StackPanel { Spacing = 4 };
            body.Children.Add(new TextBlock { Text = $"{index + 1:00}  {(string.IsNullOrWhiteSpace(tree.Title) ? "无标题选项" : tree.Title)}", FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            body.Children.Add(new TextBlock { Text = $"{(tree.Placement == ChoicePlacement.Before ? "句前" : "句后")} · {AnchorLabel(tree)}", FontSize = 11, Foreground = Brush.Parse("#64786D"), TextTrimming = TextTrimming.CharacterEllipsis });
            card.Child = body;
            card.Classes.Add("motion-card");
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            row.Children.Add(card);
            var remove = Button("×");
            remove.Width = 27;
            remove.Padding = new Thickness(0);
            remove.Margin = new Thickness(6, 0, 0, 0);
            ToolTip.SetTip(remove, "删除整棵选项树");
            remove.Click += (_, _) =>
            {
                CommitDetails();
                ChoiceTreeEditor.DeleteTree(_trees, _segments, tree.Id);
                if (ReferenceEquals(_currentTree, tree)) _currentTree = _trees.FirstOrDefault();
                _selectedModel = _currentTree;
                RenderAll();
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            AttachTreeDrag(card, tree);
            _treeList.Children.Add(row);
        }
        RenderFragmentPalette();
    }

    private void RenderFragmentPalette()
    {
        if (_context.Fragments.Count == 0) return;
        _treeList.Children.Add(Caption("素材袋 · 拖到选项建立跳转"));
        foreach (var fragment in _context.Fragments)
        {
            var card = new Border
            {
                Background = Brush.Parse("#EAF0FB"), BorderBrush = Brush.Parse("#91A9D0"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = $"▣ {fragment.Title}\n{fragment.Segments.Count} 句", TextWrapping = TextWrapping.Wrap }
            };
            Point? start = null;
            card.Classes.Add("motion-card");
            var dragged = false;
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
                dragged = true; card.Opacity = 0.7; card.RenderTransform = new TranslateTransform(delta.X, delta.Y);
                _summary.Text = "将片段放到图中的黄色选项节点上。";
            };
            card.PointerReleased += (_, e) =>
            {
                if (start is null) return;
                var target = dragged ? _branchCards.FirstOrDefault(pair => Inside(pair.Card, e)).Branch : null;
                start = null; card.Opacity = 1; card.RenderTransform = null; e.Pointer.Capture(null);
                if (target is not null)
                {
                    CommitDetails(); target.TargetNodeId = fragment.Id; _selectedModel = target;
                }
                RenderAll(); e.Handled = true;
            };
            _treeList.Children.Add(card);
        }
    }

    private static bool Inside(Control control, PointerEventArgs e)
    {
        var point = e.GetPosition(control);
        return point.X >= 0 && point.Y >= 0 && point.X <= control.Bounds.Width && point.Y <= control.Bounds.Height;
    }

    private bool OverEndZone(PointerEventArgs e)
    {
        if (!_endDropZone.IsVisible) return false;
        var point = e.GetPosition(_endDropZone);
        return point.X >= 0 && point.Y >= 0 && point.X <= _endDropZone.Bounds.Width && point.Y <= _endDropZone.Bounds.Height;
    }

    private bool OverBranchEndZone(PointerEventArgs e)
    {
        if (_branchEndDropZone is not { IsVisible: true } zone) return false;
        var point = e.GetPosition(zone);
        return point.X >= 0 && point.Y >= 0 && point.X <= zone.Bounds.Width && point.Y <= zone.Bounds.Height;
    }

    private void ShowDropFeedback(bool active)
    {
        _endDropZone.Background = Brush.Parse(active ? "#D1EBD8" : "#EDF5ED");
        _endDropZone.BorderBrush = Brush.Parse(active ? "#167456" : "#8EBDA3");
    }

    private void AttachTreeDrag(Border card, ChoiceTree tree)
    {
        Point? start = null;
        var dragged = false;
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            start = e.GetPosition(this);
            dragged = false;
            e.Pointer.Capture(card);
            e.Handled = true;
        };
        card.PointerMoved += (_, e) =>
        {
            if (start is not { } origin) return;
            var delta = e.GetPosition(this) - origin;
            if (!dragged && Math.Abs(delta.Y) + Math.Abs(delta.X) < 8) return;
            dragged = true;
            card.RenderTransform = new TranslateTransform(0, delta.Y);
            card.Opacity = 0.7;
            ShowDropFeedback(OverEndZone(e));
        };
        card.PointerReleased += (_, e) =>
        {
            if (start is null) return;
            var toEnd = dragged && OverEndZone(e);
            start = null;
            card.RenderTransform = null;
            card.Opacity = 1;
            ShowDropFeedback(false);
            e.Pointer.Capture(null);
            if (toEnd)
            {
                CommitDetails();
                ChoiceTreeEditor.MoveTreeToEnd(_trees, _segments, tree.Id);
                _currentTree = tree;
                _selectedModel = tree;
                RenderAll();
            }
            else if (!dragged) SelectTree(tree);
            else RenderAll();
            e.Handled = true;
        };
    }

    private void SelectTree(ChoiceTree tree)
    {
        CommitDetails();
        _currentTree = tree;
        _selectedModel = tree;
        RenderAll();
    }

    private void SelectNode(object model)
    {
        CommitDetails();
        _selectedModel = model;
        RenderTreeList();
        RenderGraph();
        RenderDetails();
    }

    private void AddTopLevel()
    {
        if (_segments.Count == 0) return;
        CommitDetails();
        var anchor = _segments.FirstOrDefault(s => s.Id == _initialSegmentId) ?? _segments[0];
        var tree = NewTree();
        tree.AnchorSegmentId = anchor.Id;
        _trees.Add(tree);
        _currentTree = tree;
        _selectedModel = tree;
        RenderAll();
    }

    private static ChoiceTree NewTree() => new()
    {
        Branches = [new ChoiceBranch { Label = "选项 1" }, new ChoiceBranch { Label = "选项 2" }]
    };

    private sealed class GraphNode(object model, string heading, string body, int column, double y)
    {
        public object Model { get; } = model;
        public string Heading { get; } = heading;
        public string Body { get; } = body;
        public int Column { get; } = column;
        public double Y { get; set; } = y;
    }

    private sealed record LayoutResult(GraphNode Entry, List<GraphNode> Exits, int MaxColumn);
    private sealed record JumpLink(ChoiceBranch Branch, string TargetName);

    private GraphNode AddNode(object model, string heading, string body, int column, double y)
    {
        var node = new GraphNode(model, heading, body, column, y);
        _nodes.Add(node);
        _maxColumn = Math.Max(_maxColumn, column);
        return node;
    }

    private LayoutResult LayoutTree(ChoiceTree tree, int column)
    {
        var root = AddNode(tree, "◆ 选项节点", string.IsNullOrWhiteSpace(tree.Title) ? "无提示文字" : tree.Title, column, 0);
        var exits = new List<GraphNode>();
        var branchYs = new List<double>();
        var maxColumn = column;
        foreach (var branch in tree.Branches)
        {
            var branchY = _nextY;
            _nextY += RowPitch;
            branchYs.Add(branchY);
            var option = AddNode(branch, "◇ 选择", string.IsNullOrWhiteSpace(branch.Label) ? "未填写选项文字" : branch.Label, column + 1, branchY);
            _edges.Add((root, option));
            var previous = new List<GraphNode> { option };
            var nextColumn = column + 2;
            foreach (var item in branch.Items)
            {
                if (item.Segment != null)
                {
                    var segment = item.Segment;
                    var heading = segment.Kind == SegmentKind.Dialogue ? $"▣ {segment.Speaker.DefaultIfEmptyName()} · 对白" : segment.Kind == SegmentKind.Direction ? "▣ 场景说明" : "▣ 旁白 / 动作";
                    var content = string.IsNullOrWhiteSpace(segment.Text) ? "未填写分支内容" : segment.Text;
                    var node = AddNode(item, heading, content, nextColumn, branchY);
                    foreach (var prior in previous) _edges.Add((prior, node));
                    previous = [node];
                    nextColumn++;
                }
                else if (item.Tree != null)
                {
                    var nested = LayoutTree(item.Tree, nextColumn);
                    foreach (var prior in previous) _edges.Add((prior, nested.Entry));
                    previous = nested.Exits;
                    nextColumn = nested.MaxColumn + 1;
                }
            }
            if (branch.TargetNodeId.Length > 0)
            {
                var targetName = _targets.FirstOrDefault(target => target.Id == branch.TargetNodeId)?.Name ?? "目标已失效";
                var targetNode = AddNode(new JumpLink(branch, targetName), "↗ 跳转到文本", targetName, nextColumn, branchY);
                foreach (var prior in previous) _edges.Add((prior, targetNode));
                nextColumn++;
            }
            else exits.AddRange(previous);
            maxColumn = Math.Max(maxColumn, nextColumn - 1);
        }
        if (branchYs.Count == 0)
        {
            root.Y = _nextY;
            _nextY += RowPitch;
            exits.Add(root);
        }
        else root.Y = branchYs.Average();
        return new LayoutResult(root, exits, maxColumn);
    }

    private void RenderGraph()
    {
        _canvas.Children.Clear();
        _branchEndDropZone = null;
        _nodes.Clear();
        _edges.Clear();
        _branchCards.Clear();
        _nextY = 50;
        _maxColumn = 0;
        if (_currentTree == null)
        {
            _graphHint.Text = "从左侧新建一棵选项树，随后点击节点添加分支内容。";
            _canvas.Width = 520;
            _canvas.Height = 240;
            _canvas.Children.Add(new TextBlock { Text = "尚无选项树", FontSize = 20, Foreground = Brush.Parse("#879A8B"), Margin = new Thickness(28) });
            return;
        }
        _graphHint.Text = $"当前：{AnchorLabel(_currentTree)} {(_currentTree.Placement == ChoicePlacement.Before ? "之前" : "之后")} · 点击编辑；拖动选项框可调整顺序。";
        LayoutTree(_currentTree, 0);
        _canvas.Width = Math.Max(540, 70 + _maxColumn * ColumnPitch + NodeWidth);
        _canvas.Height = Math.Max(300, _nextY + 80);
        foreach (var (from, to) in _edges)
            DrawEdge(from, to);
        foreach (var node in _nodes)
            DrawNode(node);
        _branchEndDropZone = new Border
        {
            Width = NodeWidth, Height = 46,
            Background = Brush.Parse("#EDF5ED"),
            BorderBrush = Brush.Parse("#8EBDA3"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Child = new TextBlock { Text = "↓ 拖到这里：同组选项末尾", FontSize = 11, Foreground = Brush.Parse("#315D48"), VerticalAlignment = VerticalAlignment.Center }
        };
        Canvas.SetLeft(_branchEndDropZone, 28 + ColumnPitch);
        Canvas.SetTop(_branchEndDropZone, _nextY + 4);
        _canvas.Children.Add(_branchEndDropZone);
        _branchEndDropZone.Classes.Add("motion-drop");
        UiMotion.Reveal(_canvas);
    }

    private static double NodeX(GraphNode node) => 28 + node.Column * ColumnPitch;
    private static double NodeY(GraphNode node) => node.Y;

    private void DrawEdge(GraphNode from, GraphNode to)
    {
        var x1 = NodeX(from) + NodeWidth;
        var y1 = NodeY(from) + NodeHeight / 2;
        var x2 = NodeX(to);
        var y2 = NodeY(to) + NodeHeight / 2;
        var joint = x1 + Math.Max(16, (x2 - x1) / 2);
        AddLine(x1, y1, joint, y1);
        AddLine(joint, y1, joint, y2);
        AddLine(joint, y2, x2, y2);
    }

    private void AddLine(double x1, double y1, double x2, double y2) => _canvas.Children.Add(new Line
    {
        StartPoint = new Point(x1, y1),
        EndPoint = new Point(x2, y2),
        Stroke = Brush.Parse("#9BBBA8"),
        StrokeThickness = 2
    });

    private void DrawNode(GraphNode node)
    {
        var isSelected = ReferenceEquals(node.Model, _selectedModel);
        var isMenu = node.Model is ChoiceTree;
        var isBranch = node.Model is ChoiceBranch;
        var card = new Border
        {
            Width = NodeWidth, Height = NodeHeight,
            Background = Brush.Parse(isMenu ? "#E6F2E9" : isBranch ? "#FFF4DF" : node.Model is JumpLink ? "#EAF0FB" : "#FFFFFF"),
            BorderBrush = Brush.Parse(isSelected ? "#167456" : isMenu ? "#8EBDA3" : isBranch ? "#D4B980" : "#C8D8CD"),
            BorderThickness = new Thickness(isSelected ? 3 : 1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(11, 8),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(new TextBlock { Text = node.Heading, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#476356"), TextTrimming = TextTrimming.CharacterEllipsis });
        body.Children.Add(new TextBlock { Text = node.Body.Replace('\n', ' '), FontSize = 13, MaxLines = 2, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis });
        card.Child = body;
        card.Classes.Add("motion-card");
        if (node.Model is ChoiceBranch branch)
        {
            _branchCards.Add((card, branch));
            AttachBranchDrag(card, branch);
        }
        else
            card.PointerPressed += (_, e) => { SelectNode(node.Model is JumpLink jump ? jump.Branch : node.Model); e.Handled = true; };
        Canvas.SetLeft(card, NodeX(node));
        Canvas.SetTop(card, NodeY(node));
        _canvas.Children.Add(card);
        if (node.Model is ChoiceBranch removable)
        {
            var delete = Button("×");
            delete.Width = 22;
            delete.Height = 22;
            delete.Padding = new Thickness(0);
            ToolTip.SetTip(delete, "删除此选项及其分支");
            delete.Click += (_, _) =>
            {
                CommitDetails();
                ChoiceTreeEditor.DeleteBranch(_trees, _segments, removable.Id);
                if (_currentTree != null && !_trees.Contains(_currentTree)) _currentTree = _trees.LastOrDefault();
                _selectedModel = _currentTree;
                RenderAll();
            };
            Canvas.SetLeft(delete, NodeX(node) + NodeWidth - 27);
            Canvas.SetTop(delete, NodeY(node) + 5);
            _canvas.Children.Add(delete);
        }
    }

    private void ShowBranchDropFeedback(bool active)
    {
        if (_branchEndDropZone is not { } zone) return;
        zone.Background = Brush.Parse(active ? "#D1EBD8" : "#EDF5ED");
        zone.BorderBrush = Brush.Parse(active ? "#167456" : "#8EBDA3");
    }

    private void AttachBranchDrag(Border card, ChoiceBranch branch)
    {
        Point? start = null;
        var dragged = false;
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            start = e.GetPosition(this);
            dragged = false;
            e.Pointer.Capture(card);
            e.Handled = true;
        };
        card.PointerMoved += (_, e) =>
        {
            if (start is not { } origin) return;
            var delta = e.GetPosition(this) - origin;
            if (!dragged && Math.Abs(delta.Y) + Math.Abs(delta.X) < 8) return;
            dragged = true;
            card.RenderTransform = new TranslateTransform(delta.X, delta.Y);
            card.Opacity = 0.7;
            var isTopLevel = TryFindBranchOwner(branch, out var owner) && _trees.Contains(owner);
            ShowDropFeedback(isTopLevel && OverEndZone(e));
            ShowBranchDropFeedback(ReferenceEquals(owner, _currentTree) && OverBranchEndZone(e));
        };
        card.PointerReleased += (_, e) =>
        {
            if (start is null) return;
            var hasOwner = TryFindBranchOwner(branch, out var owner);
            var toTreeEnd = dragged && hasOwner && _trees.Contains(owner) && OverEndZone(e);
            var toBranchEnd = dragged && ReferenceEquals(owner, _currentTree) && OverBranchEndZone(e);
            start = null;
            card.RenderTransform = null;
            card.Opacity = 1;
            ShowDropFeedback(false);
            ShowBranchDropFeedback(false);
            e.Pointer.Capture(null);
            if (toTreeEnd)
            {
                CommitDetails();
                var moved = ChoiceTreeEditor.MoveBranchAfterLastTree(_trees, _segments, branch.Id);
                _currentTree = moved.Tree;
                _selectedModel = branch;
                RenderAll();
            }
            else if (toBranchEnd)
            {
                CommitDetails();
                ChoiceTreeEditor.MoveBranchToEnd(_trees, _segments, branch.Id);
                _selectedModel = branch;
                RenderAll();
            }
            else if (!dragged) SelectNode(branch);
            else RenderAll();
            e.Handled = true;
        };
    }

    private void RenderDetails()
    {
        UiMotion.Reveal(_details);
        _details.Children.Clear();
        _commitDetails = null;
        if (_selectedModel is ChoiceTree tree) RenderTreeDetails(tree);
        else if (_selectedModel is ChoiceBranch branch) RenderBranchDetails(branch);
        else if (_selectedModel is ChoiceItem item && item.Segment != null) RenderSegmentDetails(item);
        else _details.Children.Add(new TextBlock { Text = "点击图中的节点开始编辑。", TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#64786D") });
    }

    private void RenderTreeDetails(ChoiceTree tree)
    {
        var topLevel = _trees.Contains(tree);
        _details.Children.Add(new TextBlock { Text = topLevel ? "选项树起点" : "嵌套选项", FontSize = 17, FontWeight = FontWeight.SemiBold });
        _details.Children.Add(Note("选项提示可留空；每个选项会进入各自的分支。"));
        _details.Children.Add(Caption("提示文字（可选）"));
        var title = new TextBox { Text = tree.Title, Watermark = "例如：接下来怎么办？" };
        _details.Children.Add(title);

        ComboBox? anchor = null;
        ComboBox? placement = null;
        if (topLevel)
        {
            _details.Children.Add(Caption("插入在原文哪一句"));
            var choices = _segments.Select((segment, index) => new AnchorOption(segment.Id, $"{index + 1:00}  {Short(segment.Text, 26)}")).ToList();
            anchor = new ComboBox { ItemsSource = choices, HorizontalAlignment = HorizontalAlignment.Stretch, SelectedItem = choices.FirstOrDefault(x => x.Id == tree.AnchorSegmentId) };
            _details.Children.Add(anchor);
            _details.Children.Add(Caption("插入位置"));
            placement = new ComboBox { ItemsSource = new[] { "这一句之前", "这一句之后" }, SelectedIndex = tree.Placement == ChoicePlacement.Before ? 0 : 1, HorizontalAlignment = HorizontalAlignment.Stretch };
            _details.Children.Add(placement);
        }
        _commitDetails = () =>
        {
            tree.Title = (title.Text ?? "").Trim();
            if (topLevel)
            {
                if (anchor?.SelectedItem is AnchorOption option) tree.AnchorSegmentId = option.Id;
                tree.Placement = placement?.SelectedIndex == 0 ? ChoicePlacement.Before : ChoicePlacement.After;
            }
        };

        var addOption = Button("＋ 添加一个选项", primary: true);
        addOption.Click += (_, _) =>
        {
            CommitDetails();
            var branch = new ChoiceBranch { Label = $"选项 {tree.Branches.Count + 1}" };
            tree.Branches.Add(branch);
            _selectedModel = branch;
            RenderAll();
        };
        _details.Children.Add(addOption);
        if (tree.Branches.Count == 0)
            _details.Children.Add(Note("这棵树还没有选项；保存草稿可以，但导出 .rpy 前需要至少一个选项。"));
        if (topLevel)
            AddAction("⇢ 接在最后一棵选项树之后", () => ChoiceTreeEditor.MoveTreeToEnd(_trees, _segments, tree.Id));
        var delete = Button(topLevel ? "删除整棵选项树" : "删除嵌套选项");
        delete.Click += (_, _) =>
        {
            CommitDetails();
            ChoiceTreeEditor.DeleteTree(_trees, _segments, tree.Id);
            if (topLevel && ReferenceEquals(_currentTree, tree)) _currentTree = _trees.LastOrDefault();
            _selectedModel = _currentTree;
            RenderAll();
        };
        _details.Children.Add(delete);
    }

    private void RenderBranchDetails(ChoiceBranch branch)
    {
        _details.Children.Add(new TextBlock { Text = "选项分支", FontSize = 17, FontWeight = FontWeight.SemiBold });
        _details.Children.Add(Note("玩家点击这行文字后，将依次播放该分支里的内容。"));
        _details.Children.Add(Note("拖到图中绿色区域只调整同组选项顺序；拖到左侧底部会变成顺序播放的下一棵选项树。"));
        _details.Children.Add(Caption("选项文字"));
        var label = new TextBox { Text = branch.Label, Watermark = "例如：打开那扇门" };
        _details.Children.Add(label);
        _details.Children.Add(Caption("分支结束后"));
        var targets = new List<StoryTarget> { new("", "继续当前主线（不跳转）") };
        targets.AddRange(_targets);
        var target = new ComboBox
        {
            ItemsSource = targets, HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedItem = targets.FirstOrDefault(item => item.Id == branch.TargetNodeId) ?? targets[0]
        };
        _details.Children.Add(target);
        _details.Children.Add(Note("选择目标后，播放本分支内容，再跳到目标位置继续剧情。可跳到正文句子或素材袋片段。"));
        _commitDetails = () =>
        {
            branch.Label = (label.Text ?? "").Trim();
            branch.TargetNodeId = (target.SelectedItem as StoryTarget)?.Id ?? "";
        };
        var update = Button("更新跳转连接", primary: true);
        update.Click += (_, _) => { CommitDetails(); RenderAll(); };
        _details.Children.Add(update);
        AddAction("＋ 添加旁白 / 动作", () => AddSegment(branch, SegmentKind.Narration));
        AddAction("＋ 添加对白", () => AddSegment(branch, SegmentKind.Dialogue));
        AddAction("＋ 添加场景说明", () => AddSegment(branch, SegmentKind.Direction));
        AddAction("＋ 添加嵌套选项", () =>
        {
            var nested = NewTree();
            branch.Items.Add(new ChoiceItem { Tree = nested });
            _selectedModel = nested;
        });
        _details.Children.Add(Note(branch.Items.Count == 0 ? "分支没有内容时，导出后相当于跳过（pass）。" : $"此分支有 {branch.Items.Count} 个内容节点，按顺序执行。"));
        AddAction("↑ 将此选项上移", () => ShiftBranch(branch, -1));
        AddAction("↓ 将此选项下移", () => ShiftBranch(branch, 1));
        AddAction("↓ 移到同组选项末尾", () => ChoiceTreeEditor.MoveBranchToEnd(_trees, _segments, branch.Id));
        if (TryFindBranchOwner(branch, out var owner) && _trees.Contains(owner))
            AddAction("⇢ 接在最后一棵选项树之后", () =>
            {
                var moved = ChoiceTreeEditor.MoveBranchAfterLastTree(_trees, _segments, branch.Id);
                _currentTree = moved.Tree;
                _selectedModel = branch;
            });
        AddAction("删除此选项及其分支", () =>
        {
            ChoiceTreeEditor.DeleteBranch(_trees, _segments, branch.Id);
            if (_currentTree != null && !_trees.Contains(_currentTree)) _currentTree = _trees.LastOrDefault();
            _selectedModel = _currentTree;
        });
    }

    private void AddSegment(ChoiceBranch branch, SegmentKind kind)
    {
        var item = new ChoiceItem { Segment = new Segment { Kind = kind, Text = "", Reviewed = false } };
        branch.Items.Add(item);
        _selectedModel = item;
    }

    private void RenderSegmentDetails(ChoiceItem item)
    {
        var segment = item.Segment!;
        _details.Children.Add(new TextBlock { Text = "分支内容", FontSize = 17, FontWeight = FontWeight.SemiBold });
        _details.Children.Add(Caption("内容类型"));
        var kind = new ComboBox { ItemsSource = new[] { "旁白/人物动作或其他", "对白", "场景 / 舞台说明" }, SelectedIndex = (int)segment.Kind, HorizontalAlignment = HorizontalAlignment.Stretch };
        _details.Children.Add(kind);
        _details.Children.Add(Caption("说话人（对白时使用）"));
        var speaker = new TextBox { Text = segment.Speaker, Watermark = "输入人物姓名" };
        _details.Children.Add(speaker);
        _details.Children.Add(Caption("正文"));
        var text = new TextBox { Text = segment.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140, MaxHeight = 250, Watermark = "在分支中播放的内容" };
        _details.Children.Add(text);
        var include = new CheckBox { Content = "包含在导出中", IsChecked = segment.Include };
        _details.Children.Add(include);
        _details.Children.Add(Note("正文为空，或对白没有说话人时，会保留为待确认；请补全后再导出。"));
        _commitDetails = () =>
        {
            segment.Kind = (SegmentKind)Math.Max(0, kind.SelectedIndex);
            segment.Speaker = segment.Kind == SegmentKind.Dialogue ? (speaker.Text ?? "").Trim() : "";
            segment.Text = text.Text ?? "";
            segment.Include = include.IsChecked == true;
            segment.Reviewed = !string.IsNullOrWhiteSpace(segment.Text) &&
                (segment.Kind != SegmentKind.Dialogue || !string.IsNullOrWhiteSpace(segment.Speaker));
        };
        if (!TryFindItemOwner(item, out var owner)) return;
        AddAction("＋ 在此节点之前添加选项", () => InsertNested(owner, item, before: true));
        AddAction("＋ 在此节点之后添加选项", () => InsertNested(owner, item, before: false));
        AddAction("↑ 将节点上移", () => ShiftItem(item, -1));
        AddAction("↓ 将节点下移", () => ShiftItem(item, 1));
        AddAction("删除这个内容节点", () =>
        {
            owner.Items.Remove(item);
            _selectedModel = owner;
        });
    }

    private void InsertNested(ChoiceBranch owner, ChoiceItem reference, bool before)
    {
        var index = owner.Items.IndexOf(reference);
        if (index < 0) return;
        var nested = NewTree();
        owner.Items.Insert(index + (before ? 0 : 1), new ChoiceItem { Tree = nested });
        _selectedModel = nested;
    }

    private void AddAction(string caption, Action action)
    {
        var button = Button(caption);
        button.Click += (_, _) => { CommitDetails(); action(); RenderAll(); };
        _details.Children.Add(button);
    }

    private void CommitDetails()
    {
        _commitDetails?.Invoke();
    }

    private void ShiftBranch(ChoiceBranch branch, int delta)
    {
        if (!TryFindBranchOwner(branch, out var owner)) return;
        var i = owner.Branches.IndexOf(branch);
        var next = i + delta;
        if (next < 0 || next >= owner.Branches.Count) return;
        (owner.Branches[i], owner.Branches[next]) = (owner.Branches[next], owner.Branches[i]);
    }

    private void ShiftItem(ChoiceItem item, int delta)
    {
        if (!TryFindItemOwner(item, out var owner)) return;
        var i = owner.Items.IndexOf(item);
        var next = i + delta;
        if (next < 0 || next >= owner.Items.Count) return;
        (owner.Items[i], owner.Items[next]) = (owner.Items[next], owner.Items[i]);
    }

    private bool TryFindBranchOwner(ChoiceBranch target, out ChoiceTree owner)
    {
        ChoiceTree? found = null;
        foreach (var tree in _trees)
            if (Search(tree)) break;
        owner = found!;
        return found != null;

        bool Search(ChoiceTree tree)
        {
            if (tree.Branches.Contains(target)) { found = tree; return true; }
            foreach (var nested in tree.Branches.SelectMany(b => b.Items).Where(i => i.Tree != null).Select(i => i.Tree!))
                if (Search(nested)) return true;
            return false;
        }
    }

    private bool TryFindItemOwner(ChoiceItem target, out ChoiceBranch owner)
    {
        ChoiceBranch? found = null;
        foreach (var tree in _trees)
            if (Search(tree)) break;
        owner = found!;
        return found != null;

        bool Search(ChoiceTree tree)
        {
            foreach (var branch in tree.Branches)
            {
                if (branch.Items.Contains(target)) { found = branch; return true; }
                foreach (var nested in branch.Items.Where(i => i.Tree != null).Select(i => i.Tree!))
                    if (Search(nested)) return true;
            }
            return false;
        }
    }

    private bool TryFindItemForNestedTree(ChoiceTree target, out ChoiceBranch owner, out ChoiceItem item)
    {
        ChoiceBranch? foundOwner = null;
        ChoiceItem? foundItem = null;
        foreach (var tree in _trees)
            if (Search(tree)) break;
        owner = foundOwner!;
        item = foundItem!;
        return foundOwner != null && foundItem != null;

        bool Search(ChoiceTree tree)
        {
            foreach (var branch in tree.Branches)
                foreach (var child in branch.Items)
                {
                    if (ReferenceEquals(child.Tree, target)) { foundOwner = branch; foundItem = child; return true; }
                    if (child.Tree != null && Search(child.Tree)) return true;
                }
            return false;
        }
    }

    private static TextBlock Note(string text) => new()
    {
        Text = text, FontSize = 12, Foreground = Brush.Parse("#667B6E"), TextWrapping = TextWrapping.Wrap, LineHeight = 20
    };

    private static string Short(string text, int length)
    {
        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length > length ? normalized[..length] + "…" : normalized;
    }

    private sealed record AnchorOption(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}

file static class ChoiceTreeStringExtensions
{
    public static string DefaultIfEmptyName(this string? value) => string.IsNullOrWhiteSpace(value) ? "未指定角色" : value;
}
