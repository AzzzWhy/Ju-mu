namespace Scribe.Core;

/// <summary>Checks choice-tree structure before saving or generating Ren'Py.</summary>
public static class ChoiceTreeValidator
{
    private const int MaxDepth = 12;
    private const int MaxNodes = 20_000;
    private const long MaxCharacters = 30_000_000;

    public static void Validate(IReadOnlyList<ChoiceTree> roots, IReadOnlyList<Segment> segments, bool requireComplete = false)
    {
        if (roots is null || segments is null) throw new UserFacingException("选项树或正文段落数据不存在，请重新打开工程。");
        if (roots.Count == 0) return;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var activeTrees = new HashSet<ChoiceTree>(ReferenceEqualityComparer.Instance);
        int nodeCount = 0;
        long characters = 0;

        void CountNode()
        {
            if (++nodeCount > MaxNodes) throw new UserFacingException("选项树内容过多，请分章节制作。");
        }

        void CheckId(string? id, string description)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
                throw new UserFacingException($"{description}的标识无效，请重新创建该内容。");
            if (!ids.Add(id)) throw new UserFacingException($"{description}的标识重复，请重新创建重复的内容。");
        }

        void CheckSegment(Segment? segment, string description, bool countChoiceNode = true)
        {
            if (countChoiceNode) CountNode();
            if (segment is null || segment.Text is null || segment.Source is null || segment.Speaker is null || segment.Warnings is null || !Enum.IsDefined(segment.Kind))
                throw new UserFacingException($"{description}的数据不完整，请重新编辑。");
            CheckId(segment.Id, description);
            if (segment.Speaker.Length > 1024 || segment.Paragraph < 0 || segment.Warnings.Count > 100 ||
                segment.Warnings.Any(w => w is null || w.Length > 4096))
                throw new UserFacingException($"{description}的角色、位置或提示信息无效。");
            characters += segment.Text.Length + segment.Source.Length;
            if (characters > MaxCharacters) throw new UserFacingException("选项树文字总量过大，请分章节制作。");
        }

        void CheckTree(ChoiceTree? tree, bool topLevel, int depth)
        {
            if (tree is null) throw new UserFacingException("选项树中存在空节点，请删除并重新创建。");
            if (!activeTrees.Add(tree)) throw new UserFacingException("选项树出现循环引用，请检查分支连接。");
            try
            {
                CountNode();
                if (depth > MaxDepth) throw new UserFacingException($"选项树嵌套不能超过 {MaxDepth} 层，请拆分剧情。");
                CheckId(tree.Id, "选项树");
                if (tree.Title is null || tree.Title.Length > 4096 || tree.Branches is null || !Enum.IsDefined(tree.Placement))
                    throw new UserFacingException("选项树标题、位置或分支数据无效。");
                characters += tree.Title.Length;
                if (characters > MaxCharacters) throw new UserFacingException("选项树文字总量过大，请分章节制作。");
                if (topLevel)
                {
                    if (string.IsNullOrWhiteSpace(tree.AnchorSegmentId) || !anchors.Contains(tree.AnchorSegmentId))
                        throw new UserFacingException("选项树关联的正文句子已不存在，请重新选择插入位置。");
                }
                else if (!string.IsNullOrEmpty(tree.AnchorSegmentId))
                    throw new UserFacingException("分支内部的选项树不应关联正文句子，请重新创建该分支。");

                if (tree.Branches.Count > MaxNodes) throw new UserFacingException("单个选项树的分支过多，请拆分剧情。");
                if (requireComplete && tree.Branches.Count == 0)
                    throw new UserFacingException("有选项树尚未添加选项，请至少添加一个选项后导出。");
                foreach (var branch in tree.Branches)
                {
                    CountNode();
                    if (branch is null || branch.Label is null || branch.Items is null || branch.Label.Length > 4096)
                        throw new UserFacingException("选项分支的数据不完整，请重新编辑。");
                    CheckId(branch.Id, "选项分支");
                    if (requireComplete && string.IsNullOrWhiteSpace(branch.Label))
                        throw new UserFacingException("有选项名称为空，请填写后再导出。");
                    characters += branch.Label.Length;
                    if (characters > MaxCharacters) throw new UserFacingException("选项树文字总量过大，请分章节制作。");
                    if (branch.Items.Count > MaxNodes) throw new UserFacingException("单个选项包含过多内容，请拆分剧情。");
                    foreach (var item in branch.Items)
                    {
                        CountNode();
                        if (item is null || (item.Segment is null) == (item.Tree is null))
                            throw new UserFacingException("分支内容必须是一个句子或一个子选项树，请重新编辑。");
                        if (item.Segment is not null) CheckSegment(item.Segment, "分支句子");
                        else CheckTree(item.Tree, false, depth + 1);
                    }
                }
            }
            finally { activeTrees.Remove(tree); }
        }

        foreach (var segment in segments)
        {
            if (segment is null) throw new UserFacingException("正文中存在空句子，请重新解析。");
            CheckSegment(segment, "正文句子", countChoiceNode: false);
            anchors.Add(segment.Id);
        }
        foreach (var root in roots) CheckTree(root, true, 1);
    }
}
