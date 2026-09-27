using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Scribe.Core;

/// <summary>Local-only, dependency-free text and DOCX import. Never executes document content.</summary>
public static class DocumentImporter
{
    public const long MaxFileBytes = 32L * 1024 * 1024;
    public const int MaxTextCharacters = 10_000_000;
    private const long MaxArchiveBytes = 128L * 1024 * 1024;

    public static ImportedDocument Read(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) throw new UserFacingException("请选择要导入的文件。");
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".doc") throw new UserFacingException("暂不支持旧版 Word .doc。请用 Word 或 LibreOffice 将文件另存为 .docx，或另存为 UTF-8 文本后再导入。");
            if (extension is not (".txt" or ".text" or ".md" or ".markdown" or ".docx"))
                throw new UserFacingException("不支持此文件格式。请选择 TXT、TEXT、Markdown（作为纯文本读取）或 Word DOCX 文件。");

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxFileBytes) throw new UserFacingException("文件超过 32 MB，请分章节保存后再导入。");
            var result = new ImportedDocument { FileName = Path.GetFileName(path) };
            result.Text = extension == ".docx" ? ReadDocx(stream, result.Warnings) : ReadText(stream, result.Warnings);
            result.Text = NormalizeNewlines(result.Text);
            if (result.Text.Length > MaxTextCharacters) throw new UserFacingException("正文超过 1000 万字符，请分章节导入。");
            if (string.IsNullOrWhiteSpace(result.Text)) throw new UserFacingException("文件没有可读取的正文。扫描图片或图片形式的文字需要先进行 OCR 文字识别。");
            if (extension is ".md" or ".markdown") result.Warnings.Add("Markdown 按纯文本读取；标题、链接和格式标记会保留，请在预览中检查。");
            return result;
        }
        catch (UserFacingException) { throw; }
        catch (FileNotFoundException) { throw new UserFacingException("找不到文件，文件可能已移动或删除。请重新选择。"); }
        catch (DirectoryNotFoundException) { throw new UserFacingException("找不到文件所在文件夹，请重新选择。"); }
        catch (UnauthorizedAccessException) { throw new UserFacingException("没有权限读取这个文件。请复制到有权限访问的文件夹后重试。"); }
        catch (InvalidDataException) { throw new UserFacingException("DOCX 文件可能损坏、加密、不是有效的 Word 文档，或使用了不支持的压缩格式。请用 Word 打开，取消密码保护并另存为新的 .docx。"); }
        catch (XmlException) { throw new UserFacingException("Word 文档内部结构不完整或不受支持。请用 Word 打开并另存为新的 .docx。"); }
        catch (IOException) { throw new UserFacingException("无法读取文件。请检查文件是否被占用、磁盘是否可访问，然后重试。"); }
        catch (ArgumentException) { throw new UserFacingException("文件路径无效，请重新选择文件。"); }
        catch (NotSupportedException) { throw new UserFacingException("文件路径或文件编码不受支持，请另存为 UTF-8 文本后重试。"); }
    }

    private static string ReadText(Stream stream, List<string> warnings)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        int skip = 0;
        Encoding? encoding = null;
        if (bytes.AsSpan().StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF })) { encoding = new UTF32Encoding(true, false, true); skip = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 })) { encoding = new UTF32Encoding(false, false, true); skip = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) { encoding = new UTF8Encoding(false, true); skip = 3; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) { encoding = new UnicodeEncoding(false, false, true); skip = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) { encoding = new UnicodeEncoding(true, false, true); skip = 2; }
        else if (LooksLikeUtf16(bytes, out bool bigEndian))
        {
            encoding = new UnicodeEncoding(bigEndian, false, true);
            warnings.Add("检测到无 BOM 的 UTF-16 文本，已自动解码；请检查预览是否出现乱码。");
        }

        string text;
        try
        {
            if (encoding is not null) text = encoding.GetString(bytes, skip, bytes.Length - skip);
            else
            {
                try { text = new UTF8Encoding(false, true).GetString(bytes); }
                catch (DecoderFallbackException)
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    var chinese = Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                    text = chinese.GetString(bytes);
                    warnings.Add("文件不是有效的 UTF-8，已尝试使用 GB18030（兼容常见 GBK）解码。编码无法完全自动确定，请检查中文和标点；如有乱码，请另存为 UTF-8 后重新导入。");
                }
            }
        }
        catch (DecoderFallbackException)
        {
            throw new UserFacingException("无法可靠识别文件编码，或文件包含损坏的字节。请用文本编辑器另存为 UTF-8 后重试。");
        }
        if (text.Any(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r' or '\f')))
            throw new UserFacingException("文件包含二进制数据或不支持的控制字符，可能不是纯文本。请另存为 UTF-8 文本后重试。");
        return text;
    }

    private static bool LooksLikeUtf16(byte[] bytes, out bool bigEndian)
    {
        bigEndian = false;
        if (bytes.Length < 8 || bytes.Length % 2 != 0) return false;
        int sample = Math.Min(bytes.Length, 8192), even = 0, odd = 0;
        for (int i = 0; i < sample; i++) if (bytes[i] == 0) { if (i % 2 == 0) even++; else odd++; }
        int pairs = sample / 2;
        if (odd > pairs / 3 && even < pairs / 20 + 1) return true;
        if (even > pairs / 3 && odd < pairs / 20 + 1) { bigEndian = true; return true; }
        return false;
    }

    private static string ReadDocx(Stream stream, List<string> warnings)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
        if (archive.Entries.Count > 10_000) throw new UserFacingException("Word 文档包含过多内部文件，请分章节另存为 DOCX 后再导入。");
        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > MaxArchiveBytes - totalBytes) throw new UserFacingException("Word 文档解压后的内容过大，请删除大型附件或分章节导入。");
            totalBytes += entry.Length;
        }
        var documentEntries = archive.Entries.Where(e => e.FullName == "word/document.xml").ToList();
        if (documentEntries.Count != 1) throw new UserFacingException("文件不是有效的 DOCX 文档：缺少或重复的正文数据。请使用 Word 重新另存为 .docx。");
        var main = documentEntries[0];
        if (main.Length > MaxFileBytes) throw new UserFacingException("Word 正文过大，请分章节导入。");
        var xmlSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxFileBytes,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };
        // Bound nesting before constructing a tree. This also keeps recursive run extraction
        // safe when opening documents not produced by Word.
        using (var checkStream = main.Open())
        using (var checkReader = XmlReader.Create(checkStream, xmlSettings))
        {
            int elements = 0;
            while (checkReader.Read())
                if (checkReader.Depth > 128 || (checkReader.NodeType == XmlNodeType.Element && ++elements > 1_000_000))
                    throw new UserFacingException("Word 文档结构过于复杂，请分章节另存为新 DOCX 或 UTF-8 文本后导入。");
        }
        using var xmlStream = main.Open();
        using var reader = XmlReader.Create(xmlStream, xmlSettings);
        var document = XDocument.Load(reader, LoadOptions.None);
        var root = document.Root;
        string? nsName = root?.Name.NamespaceName;
        if (root?.Name.LocalName != "document" || nsName is not ("http://schemas.openxmlformats.org/wordprocessingml/2006/main" or "http://purl.oclc.org/ooxml/wordprocessingml/main"))
            throw new UserFacingException("Word 文档的正文格式不受支持，请重新另存为标准 DOCX。");
        XNamespace w = nsName;
        var body = root.Element(w + "body") ?? throw new UserFacingException("Word 文档没有正文。");
        var paragraphs = body.Descendants(w + "p").Where(p => !p.Ancestors().Any(a =>
            (a.Name.Namespace == w && a.Name.LocalName is "txbxContent" or "drawing" or "pict" or "object" or "del" or "moveFrom") || IsAlternateDiscard(a)));
        var output = new StringBuilder();
        bool first = true;
        foreach (var paragraph in paragraphs)
        {
            if (!first) output.Append('\n');
            first = false;
            AppendWordText(paragraph, output, w);
            if (output.Length > MaxTextCharacters) throw new UserFacingException("Word 正文超过 1000 万字符，请分章节导入。");
        }

        bool Has(params string[] names) => names.Any(name => body.Descendants(w + name).Any());
        if (Has("tbl")) warnings.Add("Word 表格已按阅读顺序展开为段落，单元格布局未保留，请检查台词顺序。");
        if (Has("drawing", "pict", "object", "txbxContent", "sym")) warnings.Add("文档中的图片、文本框、嵌入对象或特殊字体符号未导入，请核对这些位置的文字。");
        if (Has("del", "ins", "moveFrom", "moveTo")) warnings.Add("检测到修订记录：保留新增和移入的文字，忽略已删除或移出的文字；建议先在 Word 中确认修订。");
        if (Has("numPr")) warnings.Add("Word 自动编号和项目符号未转成正文，列表文字已保留。");
        if (Has("instrText", "fldSimple")) warnings.Add("Word 域只保留文档中已保存的显示文字，不重新计算页码、引用或公式。");
        if (Has("altChunk")) warnings.Add("Word 文档中的外部嵌入正文未导入，请先在 Word 中将其转换为普通正文。");
        if (body.Descendants().Any(e => e.Name.LocalName == "AlternateContent")) warnings.Add("Word 兼容性替代内容只读取首选版本，请检查相关段落是否完整。");
        if (body.Descendants().Any(e => e.Name.LocalName is "oMath" or "oMathPara")) warnings.Add("Word 数学公式未转成正文，请手动补充。");
        if (archive.Entries.Any(e => e.FullName.StartsWith("word/header", StringComparison.Ordinal) || e.FullName.StartsWith("word/footer", StringComparison.Ordinal) || e.FullName is "word/footnotes.xml" or "word/endnotes.xml" or "word/comments.xml"))
            warnings.Add("只导入正文；页眉、页脚、脚注、尾注和批注未导入。");
        warnings.Add("DOCX 以文字导入，字体、颜色、粗体和页面排版不参与角色识别。");
        return output.ToString();
    }

    private static void AppendWordText(XElement element, StringBuilder output, XNamespace w)
    {
        if (IsAlternateDiscard(element)) return;
        if (element.Name.Namespace == w)
        {
            switch (element.Name.LocalName)
            {
                case "del": case "moveFrom": case "drawing": case "pict": case "object": case "txbxContent": case "instrText": return;
                case "t": output.Append(element.Value); return;
                case "tab": output.Append('\t'); return;
                case "br": case "cr": output.Append('\n'); return;
                case "noBreakHyphen": output.Append('\u2011'); return;
                case "softHyphen": output.Append('\u00ad'); return;
            }
        }
        if (element.Name.LocalName is "oMath" or "oMathPara") return;
        foreach (var child in element.Elements()) AppendWordText(child, output, w);
    }

    private static bool IsAlternateDiscard(XElement element)
    {
        XNamespace compatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        return (element.Name == compatibility + "Fallback" && element.Parent?.Elements(compatibility + "Choice").Any() == true) ||
            (element.Name == compatibility + "Choice" && element.ElementsBeforeSelf(compatibility + "Choice").Any());
    }

    internal static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u2028', '\n').Replace('\u2029', '\n').Replace('\f', '\n');
}
