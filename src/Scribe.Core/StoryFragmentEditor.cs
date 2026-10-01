namespace Scribe.Core;

/// <summary>Moves a contiguous group of sentences between the main route and the fragment bag.</summary>
public static class StoryFragmentEditor
{
    public static StoryFragment Stash(ProjectDocument project, IReadOnlyCollection<string> segmentIds, string? title = null)
    {
        StoryGraphValidator.Validate(project.Segments, project.ChoiceTrees, project.Fragments);
        if (segmentIds is null || segmentIds.Count == 0) throw new UserFacingException("请先选择要收纳的句子。");
        var chosen = segmentIds.ToHashSet(StringComparer.Ordinal);
        if (chosen.Count != segmentIds.Count) throw new UserFacingException("收纳句子中有重复选择。");
        var indexes = project.Segments.Select((segment, index) => (segment, index))
            .Where(pair => chosen.Contains(pair.segment.Id)).Select(pair => pair.index).ToList();
        if (indexes.Count == 0 || indexes.Count != chosen.Count || indexes[^1] - indexes[0] + 1 != indexes.Count)
            throw new UserFacingException("请只选择连续的正文句子作为一个片段；不连续的内容可分次收纳。");
        if (indexes.Count == project.Segments.Count)
            throw new UserFacingException("主线至少要留一句作为入口，不能把全部正文收进素材袋。");
        var first = indexes[0];
        var next = indexes[^1] + 1 < project.Segments.Count ? project.Segments[indexes[^1] + 1].Id : "";
        var moved = project.Segments.GetRange(first, indexes.Count);
        var movedTrees = project.ChoiceTrees.Where(tree => chosen.Contains(tree.AnchorSegmentId)).ToList();
        var name = title?.Trim() ?? "";
        if (name.Length == 0) name = moved[0].Text.Trim();
        if (name.Length > 60) name = name[..(char.IsLowSurrogate(name[60]) ? 59 : 60)] + "…";
        if (name.Length == 0) name = "未命名片段";
        var fragment = new StoryFragment { Title = name, Segments = moved, ChoiceTrees = movedTrees, NextNodeId = next };
        project.Segments.RemoveRange(first, indexes.Count);
        project.ChoiceTrees.RemoveAll(tree => chosen.Contains(tree.AnchorSegmentId));
        project.Fragments.Add(fragment);
        return fragment;
    }

    public static void Restore(ProjectDocument project, string fragmentId, int insertBeforeIndex)
    {
        StoryGraphValidator.Validate(project.Segments, project.ChoiceTrees, project.Fragments);
        var fragment = project.Fragments.SingleOrDefault(item => item.Id == fragmentId) ??
            throw new UserFacingException("要取出的片段已不存在。");
        if (insertBeforeIndex < 0 || insertBeforeIndex > project.Segments.Count)
            throw new UserFacingException("要放回的位置无效，请重新选择。");
        // Existing links to the fragment are redirected to its first sentence,
        // so removing the bag card never leaves a dangling jump.
        foreach (var tree in project.ChoiceTrees.Concat(project.Fragments.SelectMany(item => item.ChoiceTrees)))
            Retarget(tree, fragment.Id, fragment.Segments[0].Id);
        foreach (var other in project.Fragments)
            if (other.NextNodeId == fragment.Id) other.NextNodeId = fragment.Segments[0].Id;
        project.Segments.InsertRange(insertBeforeIndex, fragment.Segments);
        project.ChoiceTrees.AddRange(fragment.ChoiceTrees);
        project.Fragments.Remove(fragment);
    }

    public static void Delete(ProjectDocument project, string fragmentId)
    {
        StoryGraphValidator.Validate(project.Segments, project.ChoiceTrees, project.Fragments);
        var fragment = project.Fragments.SingleOrDefault(item => item.Id == fragmentId) ??
            throw new UserFacingException("要删除的片段已不存在。");
        var targets = fragment.Segments.Select(sentence => sentence.Id).Append(fragment.Id).ToHashSet(StringComparer.Ordinal);
        var other = project.Fragments.Where(item => item.Id != fragmentId).ToList();
        if (Branches(project.ChoiceTrees.Concat(other.SelectMany(item => item.ChoiceTrees))).Any(branch => targets.Contains(branch.TargetNodeId)) ||
            other.Any(item => targets.Contains(item.NextNodeId)))
            throw new UserFacingException("这个片段正在被其他选项或片段连接。请先调整这些连接，再删除片段。");
        project.Fragments.Remove(fragment);
    }

    private static void Retarget(ChoiceTree tree, string oldId, string newId)
    {
        foreach (var branch in tree.Branches)
        {
            if (branch.TargetNodeId == oldId) branch.TargetNodeId = newId;
            foreach (var child in branch.Items.Where(item => item.Tree is not null)) Retarget(child.Tree!, oldId, newId);
        }
    }

    public static IEnumerable<ChoiceBranch> Branches(IEnumerable<ChoiceTree> trees)
    {
        foreach (var tree in trees)
            foreach (var branch in tree.Branches)
            {
                yield return branch;
                foreach (var nested in Branches(branch.Items.Where(item => item.Tree is not null).Select(item => item.Tree!)))
                    yield return nested;
            }
    }

    public static IEnumerable<ChoiceBranch> AllBranches(ProjectDocument project) =>
        Branches(project.ChoiceTrees.Concat(project.Fragments.SelectMany(fragment => fragment.ChoiceTrees)));

    public static void Retarget(ProjectDocument project, string oldId, string newId)
    {
        foreach (var branch in AllBranches(project))
            if (branch.TargetNodeId == oldId) branch.TargetNodeId = newId;
        foreach (var fragment in project.Fragments)
            if (fragment.NextNodeId == oldId) fragment.NextNodeId = newId;
    }

    public static bool IsReferenced(ProjectDocument project, string nodeId) =>
        AllBranches(project).Any(branch => branch.TargetNodeId == nodeId) ||
        project.Fragments.Any(fragment => fragment.NextNodeId == nodeId);

    public static int ClearMissingTargets(ProjectDocument project)
    {
        var valid = project.Segments.Select(segment => segment.Id)
            .Concat(project.Fragments.Select(fragment => fragment.Id))
            .Concat(project.Fragments.SelectMany(fragment => fragment.Segments.Select(segment => segment.Id)))
            .ToHashSet(StringComparer.Ordinal);
        var cleared = 0;
        foreach (var branch in AllBranches(project))
            if (branch.TargetNodeId.Length > 0 && !valid.Contains(branch.TargetNodeId)) { branch.TargetNodeId = ""; cleared++; }
        foreach (var fragment in project.Fragments)
            if (fragment.NextNodeId.Length > 0 && !valid.Contains(fragment.NextNodeId)) { fragment.NextNodeId = ""; cleared++; }
        return cleared;
    }
}
