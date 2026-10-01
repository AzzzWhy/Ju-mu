namespace Scribe.Core;

public sealed record StoryDestination(string Id, string Name);
public sealed record StoryDestinationGroup(StoryDestination Whole, IReadOnlyList<StoryDestination> TextPositions);

/// <summary>Whole texts come first; internal positions are exposed only by expanding their group.</summary>
public static class StoryDestinationCatalog
{
    public static List<StoryDestinationGroup> Build(ProjectDocument project, string? editingFragmentId = null)
    {
        var groups = new List<StoryDestinationGroup>();
        foreach (var fragment in project.Fragments.OrderByDescending(fragment => fragment.Id == editingFragmentId))
        {
            var name = (fragment.Id == editingFragmentId ? "正在修改素材 · " : "素材 · ") + fragment.Title;
            groups.Add(new(new(fragment.Id, name), Positions(fragment.Segments, fragment.Title)));
        }
        if (project.Segments.Count > 0)
        {
            var name = string.IsNullOrWhiteSpace(project.SourceFile) ? "正文" : Path.GetFileNameWithoutExtension(project.SourceFile);
            // Jumping to a whole main text is exactly jumping to its first dialogue identity.
            groups.Add(new(new(project.Segments[0].Id, "当前修改文本 · " + name), Positions(project.Segments, name)));
        }
        return groups;
    }

    private static List<StoryDestination> Positions(IReadOnlyList<Segment> segments, string name) =>
        segments.Select((segment, index) => new StoryDestination(segment.Id, $"{name} / {index + 1} · {segment.Preview}")).ToList();
}
