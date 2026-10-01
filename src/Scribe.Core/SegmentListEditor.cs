namespace Scribe.Core;

/// <summary>Edits the order of main-story sentences without changing their source text.</summary>
public static class SegmentListEditor
{
    public static bool Move(ProjectDocument project, string segmentId, int direction)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        ChoiceTreeValidator.Validate(project.ChoiceTrees, project.Segments);
        var index = Find(project, segmentId);
        var destination = index + direction;
        if (destination < 0 || destination >= project.Segments.Count) return false;
        (project.Segments[index], project.Segments[destination]) =
            (project.Segments[destination], project.Segments[index]);
        // Menus are anchored by segment ID, so they travel with their sentence.
        return true;
    }

    /// <returns>The number of top-level choice trees removed with the sentence.</returns>
    public static int Delete(ProjectDocument project, string segmentId)
        => DeleteMany(project, [segmentId]);

    /// <summary>Validates the whole selection before removing anything.</summary>
    public static int DeleteMany(ProjectDocument project, IReadOnlyCollection<string> segmentIds)
    {
        ArgumentNullException.ThrowIfNull(project);
        StoryGraphValidator.Validate(project.Segments, project.ChoiceTrees, project.Fragments);
        if (segmentIds is null || segmentIds.Count == 0) throw new UserFacingException("请先选择要删除的句子。");
        var chosen = segmentIds.ToHashSet(StringComparer.Ordinal);
        if (chosen.Count != segmentIds.Count || !chosen.IsSubsetOf(project.Segments.Select(segment => segment.Id).ToHashSet(StringComparer.Ordinal)))
            throw new UserFacingException("选择中有重复或已不存在的句子，请重新选择。");
        var remainingTrees = project.ChoiceTrees.Where(tree => !chosen.Contains(tree.AnchorSegmentId))
            .Concat(project.Fragments.SelectMany(fragment => fragment.ChoiceTrees));
        if (StoryFragmentEditor.Branches(remainingTrees).Any(branch => chosen.Contains(branch.TargetNodeId)) ||
            project.Fragments.Any(fragment => chosen.Contains(fragment.NextNodeId)))
            throw new UserFacingException("所选句子中有其他选项或素材文本的跳转目标。请先调整连接，再删除；本次没有删除任何句子。");
        var removedTrees = project.ChoiceTrees.RemoveAll(tree => chosen.Contains(tree.AnchorSegmentId));
        project.Segments.RemoveAll(segment => chosen.Contains(segment.Id));
        return removedTrees;
    }

    private static int Find(ProjectDocument project, string segmentId)
    {
        var index = project.Segments.FindIndex(segment => segment.Id == segmentId);
        if (index < 0) throw new UserFacingException("要操作的句子已不存在，请重新选择一条识别结果。");
        return index;
    }
}
