using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Scribe.Core;

namespace Scribe.Desktop;

/// <summary>A collapsible selector of whole texts with independently expandable internal positions.</summary>
internal sealed class StoryTargetPicker : StackPanel
{
    public string SelectedId { get; private set; }

    public StoryTargetPicker(ProjectDocument project, string selectedId, string emptyName,
        string? editingFragmentId = null, string? excludedWholeId = null)
    {
        SelectedId = selectedId;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Spacing = 6;
        var groups = StoryDestinationCatalog.Build(project, editingFragmentId);
        var caption = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var toggle = new Button { Content = caption, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left };
        void UpdateCaption()
        {
            var name = SelectedId.Length == 0 ? emptyName :
                groups.Select(group => group.Whole).Concat(groups.SelectMany(group => group.TextPositions))
                    .FirstOrDefault(target => target.Id == SelectedId)?.Name ?? "目标已失效，请重新选择";
            caption.Text = StoryTargets.Short(name, 45) + "  ▾";
            ToolTip.SetTip(toggle, name);
        }
        var items = new StackPanel { Spacing = 6 };
        var list = new Border { IsVisible = false, BorderBrush = Brush.Parse("#D5DFD7"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(6),
            Child = new ScrollViewer { Content = items, MaxHeight = 360, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } };
        Children.Add(toggle); Children.Add(list);
        toggle.Click += (_, _) => list.IsVisible = !list.IsVisible;
        Button SelectButton(string id, string text)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = StoryTargets.Short(text, 42), TextWrapping = TextWrapping.Wrap },
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 6)
            };
            ToolTip.SetTip(button, text);
            button.Click += (_, _) => { SelectedId = id; UpdateCaption(); list.IsVisible = false; };
            return button;
        }
        items.Children.Add(SelectButton("", emptyName));
        items.Children.Add(new TextBlock { Text = "先选整份文本；点击 ▸ 展开内部位置", FontSize = 11, Foreground = Brush.Parse("#64786D"), TextWrapping = TextWrapping.Wrap });
        foreach (var group in groups)
        {
            var section = new StackPanel { Spacing = 4 };
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            var children = new StackPanel { IsVisible = false, Spacing = 3, Margin = new Thickness(22, 0, 0, 4) };
            var populated = false;
            var expand = new Button { Content = "▸", Padding = new Thickness(6), VerticalAlignment = VerticalAlignment.Stretch };
            ToolTip.SetTip(expand, "展开 / 折叠本文内部跳转位置");
            expand.Click += (_, _) =>
            {
                if (!populated)
                {
                    foreach (var position in group.TextPositions) children.Children.Add(SelectButton(position.Id, position.Name));
                    populated = true;
                }
                children.IsVisible = !children.IsVisible; expand.Content = children.IsVisible ? "▾" : "▸";
            };
            header.Children.Add(expand);
            var whole = SelectButton(group.Whole.Id, "▣ " + group.Whole.Name);
            whole.IsEnabled = group.Whole.Id != excludedWholeId;
            Grid.SetColumn(whole, 1); header.Children.Add(whole); section.Children.Add(header);
            section.Children.Add(children); items.Children.Add(section);
        }
        UpdateCaption();
    }
}
