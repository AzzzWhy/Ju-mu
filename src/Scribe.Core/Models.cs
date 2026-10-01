using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Scribe.Core;

public enum ImportMode { Novel, Script }
public enum SegmentKind { Narration, Dialogue, Direction }
public enum SplitMode { Paragraph, Sentence, ReadingLength }
public enum ChoicePlacement { Before, After }

public sealed class ParseOptions
{
    public ImportMode Mode { get; set; } = ImportMode.Novel;
    public SplitMode Split { get; set; } = SplitMode.Sentence;
    public int MaxLength { get; set; } = 80;
    // New manuscripts are narration by default; legacy projects are migrated on load.
    public bool AutoDetectKinds { get; set; }
    public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class Segment : INotifyPropertyChanged
{
    private string _text = "";
    private SegmentKind _kind;
    private string _speaker = "";
    private List<string> _warnings = [];
    private bool _reviewed;
    private bool _include = true;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Paragraph { get; set; }
    public string Source { get; set; } = "";
    public string Text { get => _text; set { if (_text == value) return; _text = value; Notify(); Notify(nameof(Preview)); } }
    public SegmentKind Kind { get => _kind; set { if (_kind == value) return; _kind = value; Notify(); Notify(nameof(KindLabel)); Notify(nameof(SpeakerLabel)); } }
    public string Speaker { get => _speaker; set { if (_speaker == value) return; _speaker = value; Notify(); Notify(nameof(SpeakerLabel)); } }
    public List<string> Warnings { get => _warnings; set { _warnings = value; Notify(); Notify(nameof(StatusLabel)); } }
    public bool Reviewed { get => _reviewed; set { if (_reviewed == value) return; _reviewed = value; Notify(); Notify(nameof(StatusLabel)); } }
    public bool Include { get => _include; set { if (_include == value) return; _include = value; Notify(); } }
    public string KindLabel => Kind switch { SegmentKind.Dialogue => "对白", SegmentKind.Direction => "说明", _ => "旁白" };
    public string SpeakerLabel => Kind == SegmentKind.Dialogue ? (string.IsNullOrWhiteSpace(Speaker) ? "待确认角色" : Speaker) : KindLabel;
    public string StatusLabel => Reviewed ? "已确认" : Warnings.Count > 0 ? "待确认" : "已识别";
    public string Preview => Text.Replace("\r", "").Replace("\n", "  ");
}

public sealed class ParseResult
{
    public List<Segment> Segments { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class ImportedDocument
{
    public string FileName { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> Warnings { get; set; } = [];
}

public sealed class ExportOptions
{
    public string Label { get; set; } = "imported_story";
    public bool CreateStartLabel { get; set; }
    public bool DirectionAsComments { get; set; } = true;
    public Dictionary<string, string> CharacterVariables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

// A top-level tree is attached to a manuscript segment. A nested tree appears as
// an item inside a branch; its anchor is empty because the branch defines order.
public sealed class ChoiceTree
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AnchorSegmentId { get; set; } = "";
    public ChoicePlacement Placement { get; set; } = ChoicePlacement.After;
    public string Title { get; set; } = "";
    public List<ChoiceBranch> Branches { get; set; } = [];
}

public sealed class ChoiceBranch
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "";
    public List<ChoiceItem> Items { get; set; } = [];
    // Empty means this branch rejoins the following story text.
    public string TargetNodeId { get; set; } = "";
}

public sealed class ChoiceItem
{
    public Segment? Segment { get; set; }
    public ChoiceTree? Tree { get; set; }
}

// A reusable route of ordered text nodes. It is not played automatically: a
// choice must jump to the fragment or to one of its sentences.
public sealed class StoryFragment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public List<Segment> Segments { get; set; } = [];
    public List<ChoiceTree> ChoiceTrees { get; set; } = [];
    // Optional continuation after the last sentence of this fragment.
    public string NextNodeId { get; set; } = "";
}

public sealed class ProjectDocument
{
    public int Version { get; set; } = 2;
    public bool RequiresReparse { get; set; }
    public string SourceFile { get; set; } = "";
    public string SourceText { get; set; } = "";
    public ParseOptions Options { get; set; } = new();
    public ExportOptions Export { get; set; } = new();
    public List<Segment> Segments { get; set; } = [];
    public List<string> SpeakerShortcuts { get; set; } = [];
    public List<ChoiceTree> ChoiceTrees { get; set; } = [];
    public List<StoryFragment> Fragments { get; set; } = [];
}

public sealed class UserFacingException(string message) : Exception(message);
