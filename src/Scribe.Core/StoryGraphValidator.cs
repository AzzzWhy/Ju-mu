namespace Scribe.Core;

/// <summary>Validates the main route, stored fragments, and explicit route links.</summary>
public static class StoryGraphValidator
{
    public static List<StoryFragment> ReachableFragments(IEnumerable<ChoiceTree> mainTrees, IReadOnlyList<StoryFragment> fragments)
    {
        var owners = new Dictionary<string, StoryFragment>(StringComparer.Ordinal);
        foreach (var fragment in fragments)
        {
            owners.Add(fragment.Id, fragment);
            foreach (var sentence in fragment.Segments) owners.Add(sentence.Id, fragment);
        }
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(StoryFragmentEditor.Branches(mainTrees).Select(branch => branch.TargetNodeId));
        while (pending.TryDequeue(out var id))
        {
            if (!owners.TryGetValue(id, out var fragment) || !reachable.Add(fragment.Id)) continue;
            pending.Enqueue(fragment.NextNodeId);
            foreach (var branch in StoryFragmentEditor.Branches(fragment.ChoiceTrees)) pending.Enqueue(branch.TargetNodeId);
        }
        return fragments.Where(fragment => reachable.Contains(fragment.Id)).ToList();
    }

    public static void Validate(IReadOnlyList<Segment> main, IReadOnlyList<ChoiceTree> mainTrees,
        IReadOnlyList<StoryFragment> fragments, bool requireComplete = false)
    {
        if (main is null || mainTrees is null || fragments is null)
            throw new UserFacingException("剧情节点数据不完整，请重新打开工程。");
        if (fragments.Count > 10_000) throw new UserFacingException("素材袋片段过多，请分章节制作。");
        if (main.Count + fragments.Sum(fragment => fragment?.Segments?.Count ?? 0) > 200_000)
            throw new UserFacingException("主线和素材袋包含过多句子，请分章节制作。");
        ChoiceTreeValidator.Validate(mainTrees, main, requireComplete);
        var destinations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segment in main)
            if (segment is null || string.IsNullOrWhiteSpace(segment.Id) || !destinations.Add(segment.Id))
                throw new UserFacingException("正文句子的标识重复或无效，请检查工程。");
        foreach (var fragment in fragments)
        {
            if (fragment is null || string.IsNullOrWhiteSpace(fragment.Id) || fragment.Id.Length > 128 ||
                fragment.Title is null || fragment.Title.Length > 4096 || fragment.Segments is null || fragment.ChoiceTrees is null ||
                fragment.NextNodeId is null || fragment.NextNodeId.Length > 128 || !destinations.Add(fragment.Id))
                throw new UserFacingException("素材袋片段的数据无效，请检查工程。");
            if (fragment.Segments.Count == 0)
                throw new UserFacingException("素材袋中有空片段，请放入文字或删除该片段。");
            ChoiceTreeValidator.Validate(fragment.ChoiceTrees, fragment.Segments, requireComplete);
            foreach (var segment in fragment.Segments)
                if (segment is null || string.IsNullOrWhiteSpace(segment.Id) || !destinations.Add(segment.Id))
                    throw new UserFacingException("素材袋与正文含有重复或无效的句子标识，请检查工程。");
        }

        void CheckTrees(IEnumerable<ChoiceTree> trees)
        {
            foreach (var tree in trees)
                foreach (var branch in tree.Branches)
                {
                    if (branch.TargetNodeId is null || branch.TargetNodeId.Length > 128 ||
                        (branch.TargetNodeId.Length > 0 && !destinations.Contains(branch.TargetNodeId)))
                        throw new UserFacingException($"选项“{branch.Label}”的跳转目标已不存在，请重新选择目标节点。");
                    CheckTrees(branch.Items.Where(item => item.Tree is not null).Select(item => item.Tree!));
                }
        }
        CheckTrees(mainTrees);
        foreach (var fragment in fragments)
        {
            if (fragment.NextNodeId.Length > 0 && !destinations.Contains(fragment.NextNodeId))
                throw new UserFacingException($"片段“{fragment.Title}”的后续目标已不存在，请重新选择。");
            CheckTrees(fragment.ChoiceTrees);
        }
    }
}
