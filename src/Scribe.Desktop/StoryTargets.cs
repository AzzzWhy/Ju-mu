using Scribe.Core;

namespace Scribe.Desktop;

internal sealed record StoryTarget(string Id, string Name)
{
    public override string ToString() => Name;
}

internal static class StoryTargets
{
    public static List<StoryTarget> For(ProjectDocument project)
    {
        var targets = project.Segments.Select((segment, index) =>
            new StoryTarget(segment.Id, $"主线 {index + 1} · {Short(segment.Text)}")).ToList();
        foreach (var fragment in project.Fragments)
        {
            targets.Add(new StoryTarget(fragment.Id, $"素材袋 · {Short(fragment.Title)}"));
            targets.AddRange(fragment.Segments.Select((segment, index) =>
                new StoryTarget(segment.Id, $"{Short(fragment.Title, 12)} / {index + 1} · {Short(segment.Text)}")));
        }
        return targets;
    }

    public static string Short(string text, int length = 25)
    {
        text = text.Replace('\n', ' ').Replace('\r', ' ');
        return text.Length > length ? text[..length] + "…" : text;
    }
}
