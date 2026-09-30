using System.Text;
using System.Text.RegularExpressions;

namespace Scribe.Core;

/// <summary>Offline, conservative manuscript rules. Ambiguity is exposed for review, never sent to a service.</summary>
public static class ManuscriptParser
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
    private static Regex Pattern(string pattern, RegexOptions options = RegexOptions.None) =>
        new(pattern, options | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex Label = Pattern(@"^(?<name>[^:：\r\n]{1,48})\s*[:：]\s*(?<body>[\s\S]*)$");
    private static readonly Regex Heading = Pattern(@"^(?:#{1,6}\s+|第[零〇一二三四五六七八九十百千万两\d]+[章节幕场卷回部]|(?:chapter|scene|act)\s+(?:\d+|[IVX]+)\b)", RegexOptions.IgnoreCase);
    private static readonly Regex NetworkToken = Pattern(@"(?:https?://|ftp://|www\.)[^\s<>“”「」『』，。！？；：、（）【】]+|[\p{L}\p{N}._%+\-]+@[\p{L}\p{N}.\-]+\.[A-Za-z]{2,}", RegexOptions.IgnoreCase);
    private static readonly Regex ChineseBefore = Pattern(@"(?<name>[\p{L}\p{N}_·]{1,20}?)(?:回答道|反问道|解释道|补充道|喃喃道|说道|问道|答道|喊道|叫道|回答|低语|嘀咕|说|问|答|喊|叫)\s*[：:,，]?\s*$");
    private static readonly Regex ChineseAfter = Pattern(@"^\s*[，,。.!！?？]*\s*(?<name>[\p{L}\p{N}_·]{1,20}?)(?:回答道|反问道|解释道|补充道|喃喃道|说道|问道|答道|喊道|叫道|回答|低语|嘀咕|说|问|答|喊|叫)");
    private const string EnglishName = @"(?:[A-Z][A-Za-z'’\-]*(?:\s+[A-Z][A-Za-z'’\-]*){0,2}|(?i:he|she|they|we|you|I|the man|the woman|the girl|the boy))";
    private const string SpeechVerb = @"(?i:said|says|asked|asks|replied|replies|whispered|whispers|shouted|shouts|answered|answers|murmured|murmurs|cried|added|called|exclaimed)";
    private static readonly Regex EnglishBefore = Pattern(@"(?<name>" + EnglishName + @")\s+(?:(?i:quietly|softly|loudly|gently|angrily)\s+)?" + SpeechVerb + @"\s*[:,]?\s*$");
    private static readonly Regex EnglishAfter = Pattern(@"^\s*[,.!?]*\s*(?<name>" + EnglishName + @")\s+(?:(?i:quietly|softly|loudly|gently|angrily)\s+)?" + SpeechVerb + @"\b");
    private static readonly Regex EnglishAfterInverted = Pattern(@"^\s*[,.!?]*\s*" + SpeechVerb + @"\s+(?<name>" + EnglishName + @")\b");
    private static readonly Regex EnglishSpeaker = Pattern(@"^[\p{L}_][\p{L}\p{N}_'’\-]*(?:\s+[\p{L}][\p{L}\p{N}_'’\-]*){0,3}$");
    private static readonly Regex CitationContext = Pattern(@"(?:所谓的?|名叫|叫作|称为|题为|写着|标着|印着|引用|术语|单词|按钮|选项|标题|标语|书名|词语|(?i:called|named|word|term|label|button|entitled|titled))\s*[：:]?\s*$");
    private static readonly HashSet<string> Pronouns = new(StringComparer.OrdinalIgnoreCase)
        { "他", "她", "它", "我", "你", "他们", "她们", "它们", "我们", "你们", "he", "she", "they", "we", "you", "I", "it" };
    private static readonly HashSet<string> NarratorLabels = new(StringComparer.OrdinalIgnoreCase)
        { "旁白", "叙述", "旁述", "narrator", "narration" };
    private static readonly HashSet<string> DirectionLabels = new(StringComparer.OrdinalIgnoreCase)
        { "场景", "背景", "音乐", "音效", "舞台说明", "转场", "scene", "background", "sfx", "bgm", "music", "stage direction" };
    private static readonly HashSet<string> NonSpeakerLabels = new(StringComparer.OrdinalIgnoreCase)
        { "时间", "时间是", "现在是", "网址", "链接", "地址", "邮箱", "提示", "注意", "说明", "备注", "原因", "理由", "结果", "例如", "比如", "总结", "http", "https", "ftp", "file", "mailto", "url", "email", "website", "note", "warning", "time", "location", "example", "price" };
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
        { "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st", "vs", "etc", "e.g", "i.e", "no", "fig", "vol", "inc", "dept", "a.m", "p.m", "ph.d", "u.s", "u.k" };
    private static readonly string[] SpeechModifiers =
        ["微笑着", "疑惑地", "坚定地", "认真地", "轻轻地", "慢慢地", "冷冷地", "小声地", "大声地", "低声地", "缓缓地", "笑着", "哭着", "轻声", "小声", "大声", "低声", "轻轻", "缓缓", "急忙", "接着", "又", "便"];
    private static readonly string[] Connectors = ["于是", "随后", "然后", "这时", "只见", "突然", "接着", "而", "但"];

    public static ParseResult Parse(string text, ParseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var result = new ParseResult();
        if (string.IsNullOrWhiteSpace(text))
        {
            result.Warnings.Add("文件中没有可解析的文字。");
            return result;
        }
        var paragraphs = GetParagraphs(text);
        if (!options.AutoDetectKinds)
        {
            foreach (var paragraph in paragraphs)
            {
                var warnings = paragraph.Warnings.Concat(ScanQuotes(paragraph.Text).Warnings).Distinct();
                Emit(paragraph.Text, SegmentKind.Narration, "", paragraph, options, result, warnings);
            }
            return result;
        }
        string? continuationSpeaker = null;
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.BlankBefore) continuationSpeaker = null;
            if (options.Mode == ImportMode.Script)
                ParseScript(paragraph, options, result, ref continuationSpeaker);
            else
                ParseNovel(paragraph, options, result);
        }
        return result;
    }

    public static List<string> SplitSentences(string text, SplitMode mode, int maxLength = 80)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        text = text.Trim();
        if (mode == SplitMode.Paragraph) return [text];
        var pieces = new List<string>();
        var protectedIndices = ProtectedIndices(text);
        var quotes = new Stack<char>();
        var brackets = new Stack<char>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (protectedIndices[i]) continue;
            char c = text[i];
            UpdateQuotes(text, i, quotes);
            UpdateBrackets(c, brackets);
            bool terminal = c is '。' or '！' or '？' or '!' or '?';
            if (c == '.') terminal = IsFullStop(text, i);
            if (!terminal || brackets.Count > 0) continue;
            int end = i + 1;
            while (end < text.Length && !protectedIndices[end] && text[end] is '。' or '！' or '？' or '!' or '?') end++;
            // A closing quote belongs to the sentence whose punctuation it follows.
            var simulated = new Stack<char>(quotes.Reverse());
            while (end < text.Length && simulated.Count > 0 && text[end] == simulated.Peek())
            {
                simulated.Pop();
                end++;
            }
            if (simulated.Count > 0) continue;
            AddTrimmed(pieces, text[start..end]);
            start = end;
            i = end - 1;
            quotes.Clear();
        }
        AddTrimmed(pieces, text[start..]);
        if (mode != SplitMode.ReadingLength) return pieces;
        maxLength = Math.Clamp(maxLength, 10, 1000);
        var sized = new List<string>();
        foreach (var sentence in pieces) SplitForReading(sentence, maxLength, sized);
        return sized;
    }

    private static void ParseScript(Paragraph paragraph, ParseOptions options, ParseResult result, ref string? continuationSpeaker)
    {
        string text = paragraph.Text.Trim();
        var warnings = new List<string>(paragraph.Warnings);
        var quoteScan = ScanQuotes(text);
        warnings.AddRange(quoteScan.Warnings);
        var match = Label.Match(text);
        if (match.Success)
        {
            string name = match.Groups["name"].Value.Trim();
            string body = match.Groups["body"].Value.Trim();
            if (NarratorLabels.Contains(name))
            {
                continuationSpeaker = null;
                Emit(body.Length > 0 ? body : text, SegmentKind.Narration, "", paragraph, options, result, warnings);
                return;
            }
            if (DirectionLabels.Contains(name))
            {
                continuationSpeaker = null;
                warnings.Add("识别为场景或舞台说明，请确认是否需要作为旁白显示。");
                Emit(text, SegmentKind.Direction, "", paragraph, options, result, warnings);
                return;
            }
            string speakerName = RemoveSpeakerCue(name, out string cue);
            if (IsSpeakerName(speakerName, options))
            {
                string canonical = ResolveAlias(speakerName, options);
                if (cue.Length > 0)
                    Emit(cue, SegmentKind.Direction, "", paragraph, options, result, ["角色名后的括号内容暂按舞台说明处理，请确认。"]);
                if (body.Length == 0)
                {
                    continuationSpeaker = canonical;
                    result.Warnings.Add($"第 {paragraph.Number} 段是角色标题“{name}”；后续连续段落归于该角色，遇空行或新标题结束。");
                }
                else
                {
                    continuationSpeaker = null;
                    Emit(UnwrapDialogue(body), SegmentKind.Dialogue, canonical, paragraph, options, result, warnings);
                }
                return;
            }
        }
        // Known aliases may be used as a standalone screenplay speaker heading.
        if (IsKnownAlias(text, options))
        {
            continuationSpeaker = ResolveAlias(text, options);
            result.Warnings.Add($"第 {paragraph.Number} 段按已配置的角色标题“{text}”处理。");
            return;
        }
        int opening = FindOpeningQuote(text);
        if (opening > 0 && IsSpeakerName(text[..opening].Trim(), options))
        {
            string possibleBody = text[opening..];
            var spans = ScanQuotes(possibleBody);
            if (spans.Spans.Count == 1 && spans.Spans[0].Closed && spans.Spans[0].Length == possibleBody.Length)
            {
                continuationSpeaker = null;
                Emit(UnwrapDialogue(possibleBody), SegmentKind.Dialogue, ResolveAlias(text[..opening].Trim(), options), paragraph, options, result, warnings);
                return;
            }
        }
        if (LooksLikeDirection(text))
        {
            continuationSpeaker = null;
            warnings.Add("该段可能是标题或舞台说明，请确认分类。");
            Emit(text, SegmentKind.Direction, "", paragraph, options, result, warnings);
            return;
        }
        if (continuationSpeaker is not null)
        {
            warnings.Add("沿用前面的独立角色标题；请确认该段仍属于同一角色。");
            Emit(UnwrapDialogue(text), SegmentKind.Dialogue, continuationSpeaker, paragraph, options, result, warnings);
            return;
        }
        if (quoteScan.Spans.Count > 0)
        {
            // Free-form dialogue occasionally occurs inside an otherwise labelled script.
            ParseNovel(paragraph, options, result);
            return;
        }
        Emit(text, SegmentKind.Narration, "", paragraph, options, result, warnings);
    }

    private static void ParseNovel(Paragraph paragraph, ParseOptions options, ParseResult result)
    {
        string text = paragraph.Text.Trim();
        var scan = ScanQuotes(text);
        var commonWarnings = paragraph.Warnings.Concat(scan.Warnings).Distinct().ToList();
        if (LooksLikeDirection(text))
        {
            commonWarnings.Add("该段可能是标题或括号说明，请确认是否应作为旁白显示。");
            Emit(text, SegmentKind.Direction, "", paragraph, options, result, commonWarnings);
            return;
        }
        if (scan.Spans.Count == 0)
        {
            Emit(text, SegmentKind.Narration, "", paragraph, options, result, commonWarnings);
            return;
        }
        int cursor = 0;
        var pendingNarration = new StringBuilder();
        var narrationWarnings = new List<string>(commonWarnings);
        for (int index = 0; index < scan.Spans.Count; index++)
        {
            var span = scan.Spans[index];
            string before = text[cursor..span.Start];
            int nextStart = index + 1 < scan.Spans.Count ? scan.Spans[index + 1].Start : text.Length;
            string after = text[(span.Start + span.Length)..nextStart];
            var attribution = FindAttribution(before, after, options);
            string quoted = text.Substring(span.Start, span.Length);
            string content = span.Closed ? quoted[1..^1] : quoted;
            bool citation = CitationContext.IsMatch(before);
            bool onlyQuote = string.IsNullOrWhiteSpace(before) && string.IsNullOrWhiteSpace(after);
            bool terminalQuote = content.TrimEnd().EndsWithAny('。', '！', '？', '.', '!', '?', '…');
            bool beforeBoundary = before.TrimEnd().EndsWithAny('。', '！', '？', '.', '!', '?', '：', ':');
            bool dialogue = attribution.Found || (!citation && (onlyQuote || beforeBoundary ||
                (string.IsNullOrWhiteSpace(before) && (terminalQuote || string.IsNullOrWhiteSpace(after)))));
            pendingNarration.Append(before);
            if (!dialogue)
            {
                pendingNarration.Append(quoted);
                narrationWarnings.Add("引号可能表示强调、引用或未标明角色的对白，暂按旁白保留，请确认。");
            }
            else
            {
                Emit(pendingNarration.ToString(), SegmentKind.Narration, "", paragraph, options, result, narrationWarnings);
                pendingNarration.Clear();
                narrationWarnings = new List<string>(commonWarnings);
                var warnings = new List<string>(commonWarnings);
                if (!span.Closed) warnings.Add("引号没有闭合，已保留原文，请修正后重新解析或手动编辑。");
                if (attribution.Name.Length == 0)
                    warnings.Add(attribution.Pronoun ? "说话人只有代词，无法可靠确定角色，请手动指定。" : "对白没有明确的说话人，请手动指定角色。");
                else
                    warnings.Add("角色根据附近的说话描述推断，请核对。");
                Emit(content, SegmentKind.Dialogue, attribution.Name, paragraph, options, result, warnings);
            }
            cursor = span.Start + span.Length;
        }
        pendingNarration.Append(text[cursor..]);
        Emit(pendingNarration.ToString(), SegmentKind.Narration, "", paragraph, options, result, narrationWarnings);
    }

    private static Attribution FindAttribution(string before, string after, ParseOptions options)
    {
        // A prefix ending in a colon belongs to this quotation. A suffix ending in a colon belongs to the next one.
        before = before.Length > 160 ? before[^160..] : before;
        after = after.Length > 160 ? after[..160] : after;
        foreach (var regex in new[] { EnglishBefore, ChineseBefore })
        {
            var m = regex.Match(before);
            if (m.Success) return InterpretAttribution(m.Groups["name"].Value, options);
        }
        foreach (var regex in new[] { EnglishAfter, EnglishAfterInverted, ChineseAfter })
        {
            var m = regex.Match(after);
            if (!m.Success) continue;
            string tail = after[m.Length..].TrimStart();
            if (tail.StartsWith(':') || tail.StartsWith('：')) continue;
            return InterpretAttribution(m.Groups["name"].Value, options);
        }
        return new(false, "", false);
    }

    private static Attribution InterpretAttribution(string raw, ParseOptions options)
    {
        string name = raw.Trim();
        bool changed;
        do
        {
            changed = false;
            foreach (string modifier in SpeechModifiers)
                if (name.Length > modifier.Length && name.EndsWith(modifier, StringComparison.Ordinal))
                {
                    name = name[..^modifier.Length];
                    changed = true;
                    break;
                }
        } while (changed);
        foreach (var alias in options.Aliases.Keys.OrderByDescending(x => x.Length))
            if (alias.Length > 0 && name.EndsWith(alias, StringComparison.OrdinalIgnoreCase))
                return new(true, ResolveAlias(alias, options), false);
        foreach (string connector in Connectors)
            if (name.StartsWith(connector, StringComparison.Ordinal) && name.Length > connector.Length)
                name = name[connector.Length..];
        if (Pronouns.Contains(name)) return new(true, "", true);
        if (name.Contains('的')) name = name[(name.LastIndexOf('的') + 1)..];
        if (name.Length == 0 || name.Length > 40 || NonSpeakerLabels.Contains(name) ||
            new[] { "转身", "看着", "走到", "站在", "望着", "转过", "看向", "朝着", "对着", "抬头", "低头", "拉着", "伸手" }.Any(name.Contains))
            return new(true, "", false);
        bool allCjk = name.All(IsCjk);
        if (allCjk && name.Length > 6) return new(true, "", false);
        if (!allCjk && !EnglishSpeaker.IsMatch(name)) return new(true, "", false);
        return new(true, ResolveAlias(name, options), false);
    }

    private static void Emit(string text, SegmentKind kind, string speaker, Paragraph paragraph, ParseOptions options,
        ParseResult result, IEnumerable<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        // Headings and production cues stay whole, independent of dialogue box splitting preferences.
        var pieces = kind == SegmentKind.Direction ? new List<string> { text.Trim() } : SplitSentences(text, options.Split, options.MaxLength);
        foreach (string piece in pieces)
        {
            var itemWarnings = warnings.Distinct().ToList();
            if (options.Split == SplitMode.ReadingLength && piece.Length > Math.Clamp(options.MaxLength, 10, 1000))
                itemWarnings.Add("该句超过目标长度，未找到可靠的拆分位置；已保留整句，请手动拆分。");
            result.Segments.Add(new Segment
            {
                Paragraph = paragraph.Number, Source = paragraph.Text, Text = piece, Kind = kind,
                Speaker = kind == SegmentKind.Dialogue ? speaker : "", Warnings = itemWarnings, Include = true
            });
        }
    }

    private static List<Paragraph> GetParagraphs(string input)
    {
        var lines = input.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var raw = new List<Paragraph>();
        bool blank = true;
        int number = 0;
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) { blank = true; continue; }
            raw.Add(new(++number, line, blank, []));
            blank = false;
        }
        var result = new List<Paragraph>();
        for (int i = 0; i < raw.Count; i++)
        {
            var current = raw[i];
            var scan = ScanQuotes(current.Text);
            if (scan.Spans.Count > 0 && !scan.Spans[^1].Closed)
            {
                // Join only when a matching closing mark actually appears soon. Otherwise leave each paragraph editable.
                char expected = ClosingQuote(current.Text[scan.Spans[^1].Start]);
                int closingParagraph = -1;
                for (int j = i + 1; j < raw.Count && j <= i + 32; j++)
                {
                    if (raw[j].Text.IndexOf(expected) >= 0) { closingParagraph = j; break; }
                    if (raw[j].Text.Length > 10000) break;
                }
                if (closingParagraph >= 0)
                {
                    var joined = new StringBuilder(current.Text);
                    for (int j = i + 1; j <= closingParagraph; j++)
                        joined.Append(raw[j].BlankBefore ? "\n\n" : "\n").Append(raw[j].Text);
                    current = current with { Text = joined.ToString(), Warnings = ["检测到跨段引号，已合并解析；请核对对白范围。"] };
                    i = closingParagraph;
                }
            }
            result.Add(current);
        }
        return result;
    }

    private static QuoteScan ScanQuotes(string text)
    {
        var spans = new List<QuoteSpan>();
        var warnings = new List<string>();
        var stack = new Stack<char>();
        int start = -1;
        var protectedIndices = ProtectedIndices(text);
        for (int i = 0; i < text.Length; i++)
        {
            if (protectedIndices[i] || IgnoreQuote(text, i, stack)) continue;
            char c = text[i];
            if (stack.Count > 0 && c == stack.Peek())
            {
                stack.Pop();
                if (stack.Count == 0) spans.Add(new(start, i - start + 1, true));
            }
            else if (IsOpening(c))
            {
                if (stack.Count == 0) start = i;
                stack.Push(ClosingQuote(c));
            }
            else if (c is '”' or '’' or '」' or '』' or '»' or '›')
                warnings.Add("存在未配对或顺序不一致的引号，已保留原文，请核对。");
        }
        if (stack.Count > 0)
        {
            spans.Add(new(start, text.Length - start, false));
            warnings.Add("存在未闭合的引号，已保留原文，请核对。");
        }
        return new(spans, warnings.Distinct().ToList());
    }

    private static void UpdateQuotes(string text, int index, Stack<char> stack)
    {
        if (IgnoreQuote(text, index, stack)) return;
        char c = text[index];
        if (stack.Count > 0 && c == stack.Peek()) stack.Pop();
        else if (IsOpening(c)) stack.Push(ClosingQuote(c));
    }

    private static bool IgnoreQuote(string text, int index, Stack<char> stack)
    {
        char c = text[index];
        char previous = index > 0 ? text[index - 1] : '\0';
        char next = index + 1 < text.Length ? text[index + 1] : '\0';
        if (c is '\'' or '’')
        {
            if (char.IsLetterOrDigit(previous) && char.IsLetterOrDigit(next)) return true;
            // Plural possessives are not opening quotations. A pending single quote still closes normally.
            if (char.IsLetterOrDigit(previous) && (stack.Count == 0 || stack.Peek() != c)) return true;
            if (!char.IsLetterOrDigit(previous) && index + 1 < text.Length)
            {
                string suffix = text[(index + 1)..Math.Min(text.Length, index + 7)];
                if (Pattern(@"^(?:tis|twas|cause|em|til|bout)\b", RegexOptions.IgnoreCase).IsMatch(suffix)) return true;
            }
        }
        // Inch and foot marks following measurements do not open dialogue.
        return (c is '"' or '\'') && char.IsDigit(previous) && (stack.Count == 0 || stack.Peek() != c);
    }

    private static bool[] ProtectedIndices(string text)
    {
        var indices = new bool[text.Length];
        foreach (Match match in NetworkToken.Matches(text))
        {
            int end = match.Index + match.Length;
            while (end > match.Index && ".,!?;:，。！？；：)]}".Contains(text[end - 1])) end--;
            for (int i = match.Index; i < end; i++) indices[i] = true;
        }
        return indices;
    }

    private static bool IsFullStop(string text, int index)
    {
        char previous = index > 0 ? text[index - 1] : '\0';
        char next = index + 1 < text.Length ? text[index + 1] : '\0';
        if (previous == '.' || next == '.') return false; // One ellipsis is a pause, not three stops.
        if (char.IsDigit(previous) && char.IsDigit(next)) return false;
        if (char.IsLetter(next) && !IsCjk(next)) return false; // Domain, extension, or an internal abbreviation dot.
        int start = index - 1;
        while (start >= 0 && (char.IsLetter(text[start]) || text[start] == '.')) start--;
        string word = text[(start + 1)..index];
        if (Abbreviations.Contains(word)) return false;
        if (word.Length == 1 && char.IsUpper(word[0])) return false;
        if (word.Contains('.') && word.Split('.').All(x => x.Length == 1 && char.IsLetter(x[0]))) return false;
        return true;
    }

    private static void SplitForReading(string text, int maxLength, List<string> target)
    {
        if (text.Length <= maxLength) { AddTrimmed(target, text); return; }
        var quotes = new Stack<char>();
        var brackets = new Stack<char>();
        var protectedIndices = ProtectedIndices(text);
        int start = 0, lastBreak = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (protectedIndices[i]) continue;
            UpdateQuotes(text, i, quotes);
            UpdateBrackets(text[i], brackets);
            if (quotes.Count == 0 && brackets.Count == 0 && (char.IsWhiteSpace(text[i]) || text[i] is ',' or '，' or ';' or '；' or '、'))
                lastBreak = i + 1;
            if (i - start + 1 >= maxLength && lastBreak > start)
            {
                AddTrimmed(target, text[start..lastBreak]);
                start = lastBreak;
                lastBreak = -1;
            }
        }
        AddTrimmed(target, text[start..]);
    }

    private static void UpdateBrackets(char c, Stack<char> brackets)
    {
        char close = c switch { '(' => ')', '（' => '）', '[' => ']', '【' => '】', '{' => '}', _ => '\0' };
        if (close != '\0') brackets.Push(close);
        else if (brackets.Count > 0 && c == brackets.Peek()) brackets.Pop();
    }

    private static bool IsSpeakerName(string name, ParseOptions options)
    {
        if (IsKnownAlias(name, options)) return true;
        if (name.Length == 0 || name.Length > 40 || NonSpeakerLabels.Contains(name)) return false;
        if (name.All(IsCjk))
        {
            if (name.Length > 6) return false;
            if (new[] { "的", "因为", "所以", "只有", "一个", "认为", "觉得", "需要", "如下", "意思", "时候" }.Any(name.Contains)) return false;
            if (name.EndsWithAny('说', '问', '喊', '答', '是', '有')) return false;
            return true;
        }
        if (!EnglishSpeaker.IsMatch(name)) return false;
        if (name.Any(IsCjk)) return name.Length <= 12;
        // Capitalized names are dependable screenplay labels; short lowercase identifiers are common too.
        return name.Length <= 28 && (char.IsUpper(name[0]) || !name.Contains(' '));
    }

    private static bool IsKnownAlias(string name, ParseOptions options) =>
        options.Aliases.Keys.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)) ||
        options.Aliases.Values.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));

    private static string ResolveAlias(string name, ParseOptions options)
    {
        foreach (var pair in options.Aliases)
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value)) return pair.Value.Trim();
        return name.Trim();
    }

    private static string RemoveSpeakerCue(string name, out string cue)
    {
        cue = "";
        int open = name.IndexOfAny(['(', '（']);
        if (open > 0 && (name.EndsWith(')') || name.EndsWith('）')))
        {
            cue = name[open..];
            return name[..open].Trim();
        }
        return name;
    }

    private static bool LooksLikeDirection(string text) => Heading.IsMatch(text) ||
        text.Length >= 2 && ((text[0] == '(' && text[^1] == ')') || (text[0] == '（' && text[^1] == '）') ||
        (text[0] == '[' && text[^1] == ']') || (text[0] == '【' && text[^1] == '】'));

    private static string UnwrapDialogue(string text)
    {
        text = text.Trim();
        var scan = ScanQuotes(text);
        return scan.Spans.Count == 1 && scan.Spans[0].Start == 0 && scan.Spans[0].Length == text.Length && scan.Spans[0].Closed
            ? text[1..^1].Trim() : text;
    }

    private static int FindOpeningQuote(string text)
    {
        for (int i = 0; i < text.Length; i++)
            if (IsOpening(text[i]) && !IgnoreQuote(text, i, new Stack<char>())) return i;
        return -1;
    }

    private static bool IsOpening(char c) => c is '“' or '‘' or '「' or '『' or '«' or '‹' or '"' or '\'';
    private static char ClosingQuote(char c) => c switch { '“' => '”', '‘' => '’', '「' => '」', '『' => '』', '«' => '»', '‹' => '›', _ => c };
    private static bool IsCjk(char c) => c is >= '\u3400' and <= '\u9fff';
    private static void AddTrimmed(List<string> target, string value) { if (!string.IsNullOrWhiteSpace(value)) target.Add(value.Trim()); }
    private static bool EndsWithAny(this string text, params char[] chars) => text.Length > 0 && chars.Contains(text[^1]);
    private sealed record Paragraph(int Number, string Text, bool BlankBefore, List<string> Warnings);
    private sealed record QuoteSpan(int Start, int Length, bool Closed);
    private sealed record QuoteScan(List<QuoteSpan> Spans, List<string> Warnings);
    private sealed record Attribution(bool Found, string Name, bool Pronoun);
}
