namespace Scribe.Core;

/// <summary>
/// Editing operations shared by the main window and the graphical choice editor.
/// Moving a menu to the end also moves its manuscript anchor: merely changing
/// the list order would not change its position in the exported Ren'Py script.
/// </summary>
public static class ChoiceTreeEditor
{
    public sealed record AddedChoice(ChoiceTree Tree, ChoiceBranch Branch, bool CreatedTree);
    public sealed record MovedChoice(ChoiceTree Tree, ChoiceBranch Branch, bool RemovedEmptySourceTree);

    public static AddedChoice AddQuickChoice(ProjectDocument project, string optionText,
        string? selectedSegmentId = null, ChoicePlacement placement = ChoicePlacement.After)
    {
        if (project is null) throw new UserFacingException("工程不存在，请重新打开工程。");
        return AddQuickChoice(project.ChoiceTrees, project.Segments, optionText, selectedSegmentId, placement);
    }

    public static AddedChoice AddQuickChoice(List<ChoiceTree> trees, IReadOnlyList<Segment> segments,
        string optionText, string? selectedSegmentId = null, ChoicePlacement placement = ChoicePlacement.After)
    {
        Check(trees, segments);
        if (segments.Count == 0) throw new UserFacingException("请先解析稿件，再添加选项。");
        if (!Enum.IsDefined(placement)) throw new UserFacingException("选项插入位置无效，请重新选择。");
        var label = optionText?.Trim();
        if (string.IsNullOrEmpty(label)) throw new UserFacingException("请先输入选项文字。");
        if (label.Length > 4096) throw new UserFacingException("选项文字过长，请缩短后重试。");

        var anchor = selectedSegmentId is null ? segments[^1] :
            segments.FirstOrDefault(s => s.Id == selectedSegmentId) ??
            throw new UserFacingException("选中的正文句子已不存在，请重新选择插入位置。");
        // Consecutive quick additions at the same position are alternatives in
        // one menu, not a sequence of one-option menus.
        var tree = trees.LastOrDefault(t => t.AnchorSegmentId == anchor.Id && t.Placement == placement);
        var created = tree is null;
        if (created)
        {
            tree = new ChoiceTree { AnchorSegmentId = anchor.Id, Placement = placement };
            trees.Add(tree);
        }
        var branch = new ChoiceBranch { Label = label };
        tree!.Branches.Add(branch);
        return new AddedChoice(tree, branch, created);
    }

    public static bool MoveTreeToEnd(ProjectDocument project, string treeId)
    {
        if (project is null) throw new UserFacingException("工程不存在，请重新打开工程。");
        return MoveTreeToEnd(project.ChoiceTrees, project.Segments, treeId);
    }

    /// <summary>Places a top-level menu immediately after the last other menu in export order.</summary>
    public static bool MoveTreeToEnd(List<ChoiceTree> trees, IReadOnlyList<Segment> segments, string treeId)
    {
        Check(trees, segments);
        var index = FindRootIndex(trees, treeId);
        var moving = trees[index];
        var last = LastInScript(trees, segments);
        if (last is null || (ReferenceEquals(last, moving) && index == trees.Count - 1)) return false;
        var changed = index != trees.Count - 1 || moving.AnchorSegmentId != last.AnchorSegmentId || moving.Placement != last.Placement;
        if (!changed) return false;
        trees.RemoveAt(index);
        if (!ReferenceEquals(last, moving))
        {
            moving.AnchorSegmentId = last.AnchorSegmentId;
            moving.Placement = last.Placement;
        }
        trees.Add(moving);
        return true;
    }

    public static bool MoveBranchToEnd(ProjectDocument project, string branchId)
    {
        if (project is null) throw new UserFacingException("工程不存在，请重新打开工程。");
        return MoveBranchToEnd(project.ChoiceTrees, project.Segments, branchId);
    }

    /// <summary>Reorders alternatives within their existing menu, including a nested menu.</summary>
    public static bool MoveBranchToEnd(List<ChoiceTree> trees, IReadOnlyList<Segment> segments, string branchId)
    {
        Check(trees, segments);
        var owner = FindBranchOwner(trees, branchId);
        var index = owner.Branches.FindIndex(b => b.Id == branchId);
        if (index == owner.Branches.Count - 1) return false;
        var branch = owner.Branches[index];
        owner.Branches.RemoveAt(index);
        owner.Branches.Add(branch);
        return true;
    }

    public static MovedChoice MoveBranchAfterLastTree(ProjectDocument project, string branchId)
    {
        if (project is null) throw new UserFacingException("工程不存在，请重新打开工程。");
        return MoveBranchAfterLastTree(project.ChoiceTrees, project.Segments, branchId);
    }

    /// <summary>
    /// Turns an alternative in a top-level menu into a subsequent menu. This
    /// deliberately requires a drag onto the separate "after entire tree" drop
    /// target; moving alternatives within a menu uses MoveBranchToEnd instead.
    /// </summary>
    public static MovedChoice MoveBranchAfterLastTree(List<ChoiceTree> trees, IReadOnlyList<Segment> segments, string branchId)
    {
        Check(trees, segments);
        var source = FindBranchOwner(trees, branchId);
        if (!trees.Contains(source))
            throw new UserFacingException("只能把顶层选项树中的选项接到整棵选项树之后。嵌套选项请先在原分支中调整。");
        var last = LastInScript(trees, segments)!;
        var branch = source.Branches.Single(b => b.Id == branchId);
        source.Branches.Remove(branch);
        bool removedEmptySource = source.Branches.Count == 0;
        if (removedEmptySource) trees.Remove(source);

        var created = new ChoiceTree
        {
            AnchorSegmentId = last.AnchorSegmentId,
            Placement = last.Placement,
            Branches = [branch]
        };
        trees.Add(created);
        return new MovedChoice(created, branch, removedEmptySource);
    }

    public static void DeleteTree(ProjectDocument project, string treeId)
    {
        if (project is null) throw new UserFacingException("工程不存在，请重新打开工程。");
        DeleteTree(project.ChoiceTrees, project.Segments, treeId);
    }

    public static void DeleteTree(List<ChoiceTree> trees, IReadOnlyList<Segment> segments, string treeId)
    {
        Check(trees, segments);
        var rootIndex = trees.FindIndex(t => t.Id == treeId);
        if (rootIndex >= 0) { trees.RemoveAt(rootIndex); return; }
        foreach (var tree in trees)
            if (DeleteNestedTree(tree, treeId)) return;
        throw new UserFacingException("要删除的选项树已不存在，请刷新后重试。");
    }

    public static void DeleteBranch(ProjectDocument project, string branchId)
    {
        if (project is null) throw new UserFacingException("工程不存在，请重新打开工程。");
        DeleteBranch(project.ChoiceTrees, project.Segments, branchId);
    }

    public static void DeleteBranch(List<ChoiceTree> trees, IReadOnlyList<Segment> segments, string branchId)
    {
        Check(trees, segments);
        var owner = FindBranchOwner(trees, branchId);
        owner.Branches.RemoveAll(b => b.Id == branchId);
        // Deleting the only alternative must not leave an unexportable empty
        // menu. A deliberate empty draft can still be created by other means.
        if (owner.Branches.Count == 0)
            DeleteTreeUnchecked(trees, owner.Id);
    }

    private static void Check(List<ChoiceTree>? trees, IReadOnlyList<Segment>? segments)
    {
        if (trees is null || segments is null) throw new UserFacingException("选项树或正文数据不存在，请重新打开工程。");
        ChoiceTreeValidator.Validate(trees, segments);
    }

    private static int FindRootIndex(List<ChoiceTree> trees, string treeId)
    {
        var index = trees.FindIndex(t => t.Id == treeId);
        if (index < 0) throw new UserFacingException("要移动的顶层选项树已不存在，请刷新后重试。");
        return index;
    }

    private static ChoiceTree FindBranchOwner(IEnumerable<ChoiceTree> trees, string branchId) =>
        FindBranchOwnerOrNull(trees, branchId) ??
        throw new UserFacingException("要操作的选项已不存在，请刷新后重试。");

    private static ChoiceTree? FindBranchOwnerOrNull(IEnumerable<ChoiceTree> trees, string branchId)
    {
        foreach (var tree in trees)
        {
            if (tree.Branches.Any(b => b.Id == branchId)) return tree;
            foreach (var branch in tree.Branches)
            {
                var found = FindBranchOwnerOrNull(branch.Items.Where(i => i.Tree is not null).Select(i => i.Tree!), branchId);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static ChoiceTree? LastInScript(IEnumerable<ChoiceTree> trees, IReadOnlyList<Segment> segments)
    {
        var positions = segments.Select((segment, index) => (segment.Id, index))
            .ToDictionary(x => x.Id, x => x.index, StringComparer.Ordinal);
        return trees.Select((tree, index) => (tree, index))
            .OrderBy(x => positions[x.tree.AnchorSegmentId])
            .ThenBy(x => x.tree.Placement)
            .ThenBy(x => x.index)
            .Select(x => x.tree)
            .LastOrDefault();
    }

    private static void DeleteTreeUnchecked(List<ChoiceTree> trees, string treeId)
    {
        var index = trees.FindIndex(t => t.Id == treeId);
        if (index >= 0) { trees.RemoveAt(index); return; }
        foreach (var tree in trees)
            if (DeleteNestedTree(tree, treeId)) return;
    }

    private static bool DeleteNestedTree(ChoiceTree parent, string treeId)
    {
        foreach (var branch in parent.Branches)
        {
            var item = branch.Items.FirstOrDefault(i => i.Tree?.Id == treeId);
            if (item is not null) { branch.Items.Remove(item); return true; }
            foreach (var child in branch.Items.Where(i => i.Tree is not null).Select(i => i.Tree!))
                if (DeleteNestedTree(child, treeId)) return true;
        }
        return false;
    }
}
