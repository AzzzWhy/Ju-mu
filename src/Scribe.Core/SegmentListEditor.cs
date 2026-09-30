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
    {
        ArgumentNullException.ThrowIfNull(project);
        ChoiceTreeValidator.Validate(project.ChoiceTrees, project.Segments);
        var index = Find(project, segmentId);
        var removedTrees = project.ChoiceTrees.RemoveAll(tree => tree.AnchorSegmentId == segmentId);
        project.Segments.RemoveAt(index);
        return removedTrees;
    }

    private static int Find(ProjectDocument project, string segmentId)
    {
        var index = project.Segments.FindIndex(segment => segment.Id == segmentId);
        if (index < 0) throw new UserFacingException("要操作的句子已不存在，请重新选择一条识别结果。");
        return index;
    }
}
