namespace Scribe.Core;

/// <summary>Stores whole manuscripts as bag cards while preserving internal dialogue identities.</summary>
public static class StoryFragmentEditor
{
    public static StoryFragment Stash(ProjectDocument project, IReadOnlyCollection<string> segmentIds, string? title = null)
        => StashCore(project, segmentIds, title, false);

    public static StoryFragment StashMain(ProjectDocument project, string? title = null)
        => StashCore(project, project.Segments.Select(segment => segment.Id).ToList(), title, true);

    public static StoryFragment AddText(ProjectDocument project, string title, string text, SplitMode split = SplitMode.Sentence)
    {
        StoryGraphValidator.Validate(project.Segments, project.ChoiceTrees, project.Fragments);
        CheckText(text);
        if (title.Length > 4096) throw new UserFacingException("文本名称过长，请缩短名称。");
        if (!Enum.IsDefined(split)) throw new UserFacingException("内部文本拆分方式无效，请重新选择。");
        var parsed = ManuscriptParser.Parse(text, new ParseOptions { Mode = project.Options.Mode, Split = split, MaxLength = project.Options.MaxLength });
        if (parsed.Segments.Count == 0) throw new UserFacingException("请先输入文本正文。");
        CheckCapacity(project, parsed.Segments.Count);
        var fragment = new StoryFragment { Title = string.IsNullOrWhiteSpace(title) ? "未命名文本" : title.Trim(), Segments = parsed.Segments };
        project.Fragments.Add(fragment);
        return fragment;
    }

    public static string TextOf(StoryFragment fragment) => string.Join("\n\n", fragment.Segments.Select(segment => segment.Text));

    // Blank lines delimit existing dialogue blocks, not separate bag cards.
    // Preserve unchanged prefix/suffix identities; never silently delete a linked node.
    public static void UpdateText(ProjectDocument project, StoryFragment fragment, string text)
    {
        StoryGraphValidator.Validate(project.Segments, project.ChoiceTrees, project.Fragments);
        if (!project.Fragments.Contains(fragment)) throw new UserFacingException("文本已不存在，请重新选择。");
        if (text == TextOf(fragment)) return;
        CheckText(text);
        var parts = System.Text.RegularExpressions.Regex.Split(text.Replace("\r\n", "\n").Replace('\r', '\n'), @"\n[\t ]*\n", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(2))
            .Where(part => !string.IsNullOrWhiteSpace(part)).ToList();
        var old = fragment.Segments;
        var prefix = 0;
        while (prefix < old.Count && prefix < parts.Count && old[prefix].Text == parts[prefix]) prefix++;
        var suffix = 0;
        while (suffix < old.Count - prefix && suffix < parts.Count - prefix && old[old.Count - suffix - 1].Text == parts[parts.Count - suffix - 1]) suffix++;
        var oldMiddle = old.Count - prefix - suffix;
        var newMiddle = parts.Count - prefix - suffix;
        var removed = oldMiddle == newMiddle ? [] : old.Skip(prefix).Take(oldMiddle).ToList();
        if (removed.Any(segment => IsReferenced(project, segment.Id) || fragment.ChoiceTrees.Any(tree => tree.AnchorSegmentId == segment.Id)))
            throw new UserFacingException("这次改写会移除已有跳转或选项树的文本位置。请先解除连接，或展开逐句校正修改正文（保留空行分隔）。");
        CheckCapacity(project, parts.Count - old.Count, false);
        var replacement = new List<Segment>();
        replacement.AddRange(old.Take(prefix));
        for (var index = 0; index < newMiddle; index++)
        {
            var value = parts[prefix + index];
            var segment = oldMiddle == newMiddle ? old[prefix + index] : new Segment { Source = value };
            if (segment.Text != value) { segment.Text = value; segment.Reviewed = false; }
            replacement.Add(segment);
        }
        replacement.AddRange(old.Skip(old.Count - suffix));
        fragment.Segments = replacement;
    }

    private static void CheckText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new UserFacingException("请先输入文本正文，不能保存空文本。");
        if (text.Length > DocumentImporter.MaxTextCharacters) throw new UserFacingException("文本超过 1000 万字符，请分章节添加。");
    }
    private static void CheckCapacity(ProjectDocument project, int additional, bool addingCard = true)
    {
        if ((addingCard && project.Fragments.Count >= 10_000) || project.Segments.Count + project.Fragments.Sum(fragment => fragment.Segments.Count) + additional > 200_000)
            throw new UserFacingException("素材数量过多，请分章节制作。");
    }

    private static StoryFragment StashCore(ProjectDocument project, IReadOnlyCollection<string> segmentIds, string? title, bool allowWholeMain)
    {
        StoryGraphValidator.Validate(project.Segments, project.ChoiceTrees, project.Fragments);
        if (segmentIds is null || segmentIds.Count == 0) throw new UserFacingException("请先选择要收纳的句子。");
        var chosen = segmentIds.ToHashSet(StringComparer.Ordinal);
        if (chosen.Count != segmentIds.Count) throw new UserFacingException("收纳句子中有重复选择。");
        var indexes = project.Segments.Select((segment, index) => (segment, index))
            .Where(pair => chosen.Contains(pair.segment.Id)).Select(pair => pair.index).ToList();
        if (indexes.Count == 0 || indexes.Count != chosen.Count || indexes[^1] - indexes[0] + 1 != indexes.Count)
            throw new UserFacingException("请只选择连续的正文句子作为一个片段；不连续的内容可分次收纳。");
        if (indexes.Count == project.Segments.Count && !allowWholeMain)
            throw new UserFacingException("主线至少要留一句作为入口，不能把全部正文收进素材袋。");
        CheckCapacity(project, 0);
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
        => DeleteMany(project, [fragmentId]);

    public static void DeleteMany(ProjectDocument project, IReadOnlyCollection<string> fragmentIds)
    {
        StoryGraphValidator.Validate(project.Segments, project.ChoiceTrees, project.Fragments);
        if (fragmentIds is null || fragmentIds.Count == 0) throw new UserFacingException("请先选择要删除的素材文本。");
        var chosen = fragmentIds.ToHashSet(StringComparer.Ordinal);
        if (chosen.Count != fragmentIds.Count || !chosen.IsSubsetOf(project.Fragments.Select(fragment => fragment.Id).ToHashSet(StringComparer.Ordinal)))
            throw new UserFacingException("选择中有重复或已不存在的素材文本，请重新选择。");
        var targets = project.Fragments.Where(fragment => chosen.Contains(fragment.Id))
            .SelectMany(fragment => fragment.Segments.Select(sentence => sentence.Id).Append(fragment.Id)).ToHashSet(StringComparer.Ordinal);
        var other = project.Fragments.Where(item => !chosen.Contains(item.Id)).ToList();
        if (Branches(project.ChoiceTrees.Concat(other.SelectMany(item => item.ChoiceTrees))).Any(branch => targets.Contains(branch.TargetNodeId)) ||
            other.Any(item => targets.Contains(item.NextNodeId)))
            throw new UserFacingException("所选素材文本正在被其他选项或文本连接。请先调整这些连接，再删除；本次没有删除任何素材。");
        project.Fragments.RemoveAll(fragment => chosen.Contains(fragment.Id));
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
