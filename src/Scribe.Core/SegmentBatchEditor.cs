namespace Scribe.Core;

// Null means "leave this field unchanged". An empty string is an intentional edit.
public readonly record struct SegmentEdit(string? Text = null, string? Speaker = null,
    SegmentKind? Kind = null, bool? Include = null)
{
    public bool HasChanges => Text is not null || Speaker is not null || Kind is not null || Include is not null;
}

public static class SegmentBatchEditor
{
    public static void Validate(IReadOnlyList<Segment> targets, SegmentEdit edit, bool reviewed)
    {
        if (!reviewed) return;
        foreach (var segment in targets)
        {
            var text = edit.Text ?? segment.Text;
            var kind = edit.Kind ?? segment.Kind;
            var speaker = kind == SegmentKind.Dialogue ? edit.Speaker ?? segment.Speaker : "";
            var include = edit.Include ?? segment.Include;
            if (kind == SegmentKind.Dialogue && string.IsNullOrWhiteSpace(speaker))
                throw new UserFacingException("所选内容中有对白缺少说话人；请填写姓名，或将内容类型改为「旁白/人物动作或其他」。");
            if (include && string.IsNullOrWhiteSpace(text))
                throw new UserFacingException("所选内容中有空白正文。请填写内容，或取消「包含在导出中」。");
        }
    }

    public static int Apply(IReadOnlyList<Segment> targets, SegmentEdit edit, bool reviewed)
    {
        Validate(targets, edit, reviewed);
        var affected = 0;
        foreach (var segment in targets)
        {
            var text = edit.Text ?? segment.Text;
            var kind = edit.Kind ?? segment.Kind;
            var speaker = kind == SegmentKind.Dialogue ? edit.Speaker ?? segment.Speaker : "";
            var include = edit.Include ?? segment.Include;
            var changed = text != segment.Text || kind != segment.Kind || speaker != segment.Speaker || include != segment.Include;
            if (!changed && (!reviewed || segment.Reviewed)) continue;
            segment.Text = text;
            segment.Kind = kind;
            segment.Speaker = speaker;
            segment.Include = include;
            segment.Reviewed = reviewed;
            affected++;
        }
        return affected;
    }
}
