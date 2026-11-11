using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace Bed;

// Content rights, part 1 (2026-11-07): the rights metadata common files carry, read safely. No tool can tell whether a
// file is someone else's work; this reads only what a file says about itself. MIXED: the tag locations are each format's
// own specification (PNG 1.2 text chunks, EXIF 2.3 tag 0x8298, Adobe XMP dc:rights, glTF 2.0 asset.copyright, OpenType
// name IDs 0, 13 and 14), BORROWED; the bounded readers and the plain-words outcome are INVENTED. A237 (Codex): every cap
// hit and every part that couldn't be read makes the scan incomplete, never silently clean.

/// <summary>What a rights tag says about a file.</summary>
public enum RightsKind
{
    /// <summary>A copyright or rights statement (PNG <c>Copyright</c>, EXIF Copyright, XMP <c>dc:rights</c>, glTF
    /// <c>asset.copyright</c>, a font's name ID 0).</summary>
    Copyright,
    /// <summary>A licence's text or link (a font's name IDs 13 and 14).</summary>
    Licence,
    /// <summary>Who made it (PNG <c>Author</c>, EXIF Artist). Counts as a rights claim only when it says so in words.</summary>
    Author,
}

/// <summary>One rights tag found in a file.</summary>
/// <param name="Format">The file format it was found in: <c>png</c>, <c>jpeg</c>, <c>gltf</c>, <c>font</c>.</param>
/// <param name="Field">Where in the file: for example <c>tEXt Copyright</c>, <c>EXIF 0x8298</c>, <c>XMP dc:rights</c>,
/// <c>asset.copyright</c>, <c>name 0</c>.</param>
/// <param name="Kind">What it says.</param>
/// <param name="Text">The whole text it carries, trimmed, up to <see cref="RightsTags.MostTextLength"/> characters (a
/// longer tag makes the scan incomplete). Judging reads all of it; <see cref="ToString"/> shows a shortened form.</param>
public sealed record RightsTag(string Format, string Field, RightsKind Kind, string Text)
{
    /// <summary>The most characters <see cref="ToString"/> shows of <see cref="Text"/>.</summary>
    public const int ShownLength = 500;

    /// <inheritdoc />
    public override string ToString() => $"{Format} {Field} \"{(Text.Length > ShownLength ? Text[..ShownLength] + "..." : Text)}\"";
}

/// <summary>What a scan of one file found.</summary>
/// <param name="Format">The format recognised, or null when the file isn't one this reader knows (then it has nothing to
/// say, which is not the same as clean).</param>
/// <param name="Tags">Every rights tag found, in file order.</param>
/// <param name="Problem">Why the scan is incomplete, or null when every rights tag the format can carry was read: a part
/// that couldn't be read (malformed, cut off, badly encoded) or a cap that was reached (size, decompression, tag count,
/// fonts in a collection). The tags found still count; an incomplete scan refuses an asset declared ours or public domain
/// (<see cref="AssetOrigins.Judge"/>).</param>
public sealed record RightsScan(string? Format, IReadOnlyList<RightsTag> Tags, string? Problem)
{
    /// <summary>True when nothing was left unread.</summary>
    public bool Complete => Problem is null;

    /// <summary>The tags that claim rights someone holds (see <see cref="RightsTags.IsClaim"/>).</summary>
    public IReadOnlyList<RightsTag> Claims(IReadOnlyCollection<string>? ourNames = null, bool publicDomainOk = false) =>
        Tags.Where(t => RightsTags.IsClaim(t, ourNames, publicDomainOk)).ToList();
}

/// <summary>
/// Reads the rights metadata that common asset files carry, defensively: PNG <c>tEXt</c>/<c>zTXt</c>/<c>iTXt</c>
/// <c>Copyright</c> and <c>Author</c> and XMP <c>dc:rights</c>; JPEG EXIF Copyright (0x8298) and Artist (0x013B) and XMP;
/// glTF (<c>.gltf</c> and <c>.glb</c>) <c>asset.copyright</c>; TrueType/OpenType (and collections) <c>name</c> IDs 0, 13
/// and 14. OBJ and MTL comments are free text, so they aren't read. Every length is checked against what's left, every
/// loop is bounded, decompression and XML are capped, and XML refuses DTDs. Whatever couldn't be read, and every cap
/// reached, is said in <see cref="RightsScan.Problem"/>: never an exception, a hang, or a scan that looks complete when it
/// isn't.
/// </summary>
public static class RightsTags
{
    /// <summary>The largest file scanned (64 MiB). A bigger one is reported as too large to scan.</summary>
    public const long MostFileBytes = 64L * 1024 * 1024;

    /// <summary>The most text judged from one tag; a longer tag makes the scan incomplete.</summary>
    public const int MostTextLength = 16 * 1024;

    /// <summary>The most rights tags read from one file; more makes the scan incomplete.</summary>
    public const int MostTags = 64;

    /// <summary>The most fonts read from one collection; more makes the scan incomplete.</summary>
    public const int MostFonts = 64;

    private const int MostInflated = 1024 * 1024;   // one compressed text chunk or XMP packet, decompressed
    private const int MostInflatedPerFile = 4 * 1024 * 1024;   // every compressed chunk in one file, together
    private const int MostCompressedChunks = 32;   // compressed chunks opened in one file
    private const int MostChunks = 100_000;

    // What one scan collects: its tags, what it couldn't read, and what decompression may still cost.
    private sealed class Scan(string format)
    {
        public readonly string Format = format;
        public readonly List<RightsTag> Tags = [];
        public readonly List<string> Unread = [];
        public int InflateLeft = MostInflatedPerFile;
        public int Compressed;

        public void Missed(string why) { if (Unread.Count < 8 && !Unread.Contains(why)) Unread.Add(why); }
    }

    /// <summary>Scans the file at <paramref name="path"/>. Never throws for the file's content; a file that can't be
    /// opened at all is a problem too.</summary>
    public static RightsScan ReadFile(string path)
    {
        try
        {
            var length = new FileInfo(path).Length;
            if (length > MostFileBytes) return new(null, [], $"it's {length} bytes, more than the {MostFileBytes} this scan reads, so its tags weren't read");
            return Read(File.ReadAllBytes(path), Path.GetFileName(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(null, [], $"it couldn't be read: {e.Message}"); }
    }

    /// <summary>Scans a file's bytes. <paramref name="fileName"/> decides only between formats the bytes can't tell apart
    /// (a <c>.gltf</c> is JSON).</summary>
    public static RightsScan Read(ReadOnlySpan<byte> bytes, string fileName = "")
    {
        if (bytes.Length > MostFileBytes) return new(null, [], $"it's {bytes.Length} bytes, more than the {MostFileBytes} this scan reads, so its tags weren't read");
        Scan? s = null;
        try
        {
            if (bytes.StartsWith(PngSignature)) Png(bytes, s = new("png"));
            else if (bytes is [0xFF, 0xD8, 0xFF, ..]) Jpeg(bytes, s = new("jpeg"));
            else if (bytes.StartsWith("glTF"u8)) Glb(bytes, s = new("gltf"));
            else if (bytes is [0x00, 0x01, 0x00, 0x00, ..] || bytes.StartsWith("OTTO"u8) || bytes.StartsWith("true"u8) || bytes.StartsWith("ttcf"u8))
                Font(bytes, s = new("font"));
            else if (fileName.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase)) GltfJson(bytes, s = new("gltf"));
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidDataException or DecoderFallbackException)
        {
            s?.Missed("it isn't a well-formed file of its kind, so its tags couldn't all be read");   // a missed bound is a clean refusal, never a crash
        }
        if (s is null) return new(null, [], null);
        return new(s.Format, s.Tags, s.Unread.Count == 0 ? null : string.Join("; ", s.Unread));
    }

    /// <summary>
    /// Whether <paramref name="tag"/> claims rights someone holds: a copyright or licence tag with any text, or any tag
    /// whose text says "©", "(c)", "copyright" or "all rights reserved". An author tag that only names someone isn't a
    /// claim. A tag isn't a claim against the project when, apart from rights boilerplate ("©", "copyright", "all rights
    /// reserved", years, "by", "inc"), its only words are one of <paramref name="ourNames"/> (the project's own rights
    /// holders, matched as whole words, each at least <see cref="ShortestOwnName"/> letters): "© 2026 Our Studio" clears
    /// for "Our Studio", but "© Big Co, ripped by Our Studio" doesn't. With <paramref name="publicDomainOk"/>, nor is a tag
    /// that's a plain public-domain or CC0 statement and says nothing else ("CC0 1.0 public domain dedication" clears;
    /// "not public domain" doesn't).
    /// </summary>
    public static bool IsClaim(RightsTag tag, IReadOnlyCollection<string>? ourNames = null, bool publicDomainOk = false)
    {
        var text = tag.Text;
        if (text.Length == 0) return false;
        if (ourNames is not null && OnlyOurs(text, ourNames)) return false;
        if (publicDomainOk && PlainPublicDomain(text)) return false;
        return tag.Kind is RightsKind.Copyright or RightsKind.Licence || SaysRights(text);
    }

    /// <summary>The fewest letters and digits an own name needs to clear a tag: a very short name would clear almost anything.</summary>
    public const int ShortestOwnName = 3;

    private static readonly HashSet<string> Boilerplate = new(StringComparer.Ordinal)
        { "c", "copyright", "copr", "all", "rights", "reserved", "by", "and", "the", "inc", "ltd", "llc", "co" };

    private static readonly HashSet<string> PublicDomainWords = new(StringComparer.Ordinal)
    {
        "this", "work", "image", "file", "model", "font", "sound", "asset", "is", "are", "has", "been", "dedicated", "released", "placed",
        "to", "into", "the", "in", "a", "an", "under", "as", "and", "worldwide", "universal", "public", "domain", "dedication", "mark",
        "waiver", "cc0", "cc", "zero", "no", "rights", "reserved", "license", "licence", "licensed",
    };

    private static bool OnlyOurs(string text, IReadOnlyCollection<string> ourNames)
    {
        var words = $" {NameText.Normalise(text)} ";
        var matched = false;
        foreach (var name in ourNames.Select(NameText.Normalise).Where(n => n.Replace(" ", "").Length >= ShortestOwnName).OrderByDescending(n => n.Length))
        {
            var replaced = words.Replace($" {name} ", " ", StringComparison.Ordinal);
            while (replaced != words) { matched = true; words = replaced; replaced = words.Replace($" {name} ", " ", StringComparison.Ordinal); }
        }
        return matched && words.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => Boilerplate.Contains(w) || w.All(char.IsAsciiDigit));
    }

    private static bool PlainPublicDomain(string text)
    {
        var normal = NameText.Normalise(text);
        var words = normal.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var says = $" {normal} ".Contains(" public domain ", StringComparison.Ordinal) || words.Contains("cc0")
            || $" {normal} ".Contains(" no rights reserved ", StringComparison.Ordinal);
        return says && words.All(w => PublicDomainWords.Contains(w) || w.All(char.IsAsciiDigit));
    }

    private static bool SaysRights(string text) => text.Contains('©') || text.Contains("(c)", StringComparison.OrdinalIgnoreCase)
        || text.Contains("copyright", StringComparison.OrdinalIgnoreCase) || text.Contains("all rights reserved", StringComparison.OrdinalIgnoreCase);

    private static void Add(Scan s, string field, RightsKind kind, string text)
    {
        text = new string(text.Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray()).Trim();
        if (text.Length == 0) return;
        if (text.Length > MostTextLength)
        {
            s.Missed($"its {field} tag is longer than {MostTextLength} characters, so only the start of it was judged");
            text = text[..MostTextLength];
        }
        var tag = new RightsTag(s.Format, field, kind, text);
        if (s.Tags.Contains(tag)) return;
        if (s.Tags.Count >= MostTags) { s.Missed($"it has more than {MostTags} rights tags, so the rest weren't read"); return; }
        s.Tags.Add(tag);
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // Text that must be UTF-8: bytes that aren't are a part that couldn't be read, never quietly replaced.
    private static string? Utf8(ReadOnlySpan<byte> bytes, Scan s, string what)
    {
        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { s.Missed($"its {what} isn't valid UTF-8, so it couldn't be read"); return null; }
    }

    // ---- PNG ----

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static void Png(ReadOnlySpan<byte> b, Scan s)
    {
        var at = 8;
        for (var n = 0; n < MostChunks; n++)
        {
            if (b.Length - at < 12) { s.Missed(at == b.Length ? "it ends without an IEND chunk" : "it's cut off inside a chunk"); return; }
            var length = BinaryPrimitives.ReadUInt32BigEndian(b[at..]);
            var type = Encoding.ASCII.GetString(b.Slice(at + 4, 4));
            if (length > (uint)(b.Length - at - 12)) { s.Missed($"its {type} chunk says it's {length} bytes, more than the file holds"); return; }
            var data = b.Slice(at + 8, (int)length);
            switch (type)
            {
                case "tEXt": PngText(data, s, compressed: false); break;
                case "zTXt": PngText(data, s, compressed: true); break;
                case "iTXt": PngIText(data, s); break;
                case "IEND": return;
            }
            at += 12 + (int)length;
        }
        s.Missed($"it has more than {MostChunks} chunks, so the rest weren't read");
    }

    private const string XmpKeyword = "XML:com.adobe.xmp";

    // Only the keywords read here are ever decompressed: every other text chunk is skipped unopened.
    private static bool Wanted(string keyword) => keyword.Equals("Copyright", StringComparison.OrdinalIgnoreCase)
        || keyword.Equals("Author", StringComparison.OrdinalIgnoreCase) || keyword == XmpKeyword;

    // A text chunk's keyword: 1 to 79 Latin-1 bytes before a NUL. Null when the chunk has none (then it can't be a rights tag).
    private static string? Keyword(ReadOnlySpan<byte> data, Scan s, string chunk, out int zero)
    {
        zero = data.IndexOf((byte)0);
        if (zero is > 0 and <= 79) return Encoding.Latin1.GetString(data[..zero]);
        s.Missed($"a {chunk} chunk has no readable keyword, so it couldn't be checked");
        return null;
    }

    private static void PngText(ReadOnlySpan<byte> data, Scan s, bool compressed)
    {
        var chunk = compressed ? "zTXt" : "tEXt";
        if (Keyword(data, s, chunk, out var zero) is not { } keyword || !Wanted(keyword) || keyword == XmpKeyword) return;   // XMP lives only in iTXt
        var rest = data[(zero + 1)..];
        if (compressed)
        {
            if (rest.Length < 1 || rest[0] != 0) { s.Missed($"its {chunk} {keyword} chunk uses an unknown compression method, so it couldn't be read"); return; }
            if (Inflate(rest[1..], s, $"{chunk} {keyword}") is not { } inflated) return;
            rest = inflated;
        }
        PngKeyword(keyword, Encoding.Latin1.GetString(rest), s, chunk);
    }

    private static void PngIText(ReadOnlySpan<byte> data, Scan s)
    {
        if (Keyword(data, s, "iTXt", out var zero) is not { } keyword || !Wanted(keyword)) return;
        var broken = $"its iTXt {keyword} chunk is malformed, so it couldn't be read";
        if (data.Length < zero + 3) { s.Missed(broken); return; }
        var flag = data[zero + 1];
        var method = data[zero + 2];
        var rest = data[(zero + 3)..];
        var lang = rest.IndexOf((byte)0);
        if (lang < 0) { s.Missed(broken); return; }
        rest = rest[(lang + 1)..];
        var translated = rest.IndexOf((byte)0);
        if (translated < 0) { s.Missed(broken); return; }
        rest = rest[(translated + 1)..];
        if (flag > 1 || flag == 1 && method != 0) { s.Missed($"its iTXt {keyword} chunk uses an unknown compression, so it couldn't be read"); return; }
        if (flag == 1)
        {
            if (Inflate(rest, s, $"iTXt {keyword}") is not { } inflated) return;
            rest = inflated;
        }
        if (Utf8(rest, s, $"iTXt {keyword} text") is not { } text) return;
        if (keyword == XmpKeyword) Xmp(text, s);
        else PngKeyword(keyword, text, s, "iTXt");
    }

    private static void PngKeyword(string keyword, string text, Scan s, string chunk)
    {
        if (keyword.Equals("Copyright", StringComparison.OrdinalIgnoreCase)) Add(s, $"{chunk} Copyright", RightsKind.Copyright, text);
        else if (keyword.Equals("Author", StringComparison.OrdinalIgnoreCase)) Add(s, $"{chunk} Author", RightsKind.Author, text);
    }

    // Decompresses one chunk within the file's budget, growing only as far as the data goes. A chunk past the file's budget
    // or its count of compressed chunks, one cut at a cap, and a broken stream are each said, so the scan is incomplete.
    private static byte[]? Inflate(ReadOnlySpan<byte> compressed, Scan s, string what)
    {
        if (s.Compressed >= MostCompressedChunks) { s.Missed($"it has more than {MostCompressedChunks} compressed rights chunks, so the rest weren't read"); return null; }
        if (s.InflateLeft <= 0) { s.Missed($"its compressed rights chunks decompress to more than {MostInflatedPerFile} bytes, so the rest weren't read"); return null; }
        s.Compressed++;
        var cap = Math.Min(MostInflated, s.InflateLeft);
        using var input = new MemoryStream(compressed.ToArray(), writable: false);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        try
        {
            int read;
            while (output.Length < cap && (read = z.Read(buffer, 0, (int)Math.Min(buffer.Length, cap - output.Length))) > 0) output.Write(buffer, 0, read);
            if (output.Length >= cap && z.Read(buffer, 0, 1) > 0) s.Missed($"its {what} chunk decompresses to more than {cap} bytes, so only the start was read");
        }
        catch (InvalidDataException) { s.Missed($"its {what} chunk's compressed data is broken, so it couldn't all be read"); }   // what came out before the break is kept
        s.InflateLeft -= (int)output.Length;
        return output.ToArray();
    }

    // ---- XMP ----

    private const string DublinCore = "http://purl.org/dc/elements/1.1/";

    private static void Xmp(string text, Scan s)
    {
        if (text.Length > MostInflated) { s.Missed($"its XMP packet is longer than {MostInflated} characters, so only the start was read"); text = text[..MostInflated]; }
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MostInflated,
            MaxCharactersFromEntities = 1, IgnoreComments = true, IgnoreProcessingInstructions = true,
        };
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), settings);
            var depth = -1;
            var found = new StringBuilder();
            while (reader.Read())
            {
                if (depth < 0)
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "rights" && reader.NamespaceURI == DublinCore && !reader.IsEmptyElement)
                    {
                        depth = reader.Depth;
                        found.Clear();
                    }
                    else if (reader.NodeType == XmlNodeType.Element && reader.GetAttribute("rights", DublinCore) is { } attribute)
                        Add(s, "XMP dc:rights", RightsKind.Copyright, attribute);
                }
                else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA) found.Append(reader.Value).Append(' ');
                else if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                {
                    Add(s, "XMP dc:rights", RightsKind.Copyright, found.ToString());
                    depth = -1;
                }
            }
        }
        catch (XmlException) { s.Missed("its XMP metadata isn't well-formed XML (or uses a DTD, which is refused), so it couldn't all be read"); }   // what was found before still counts
    }

    // ---- JPEG ----

    private static void Jpeg(ReadOnlySpan<byte> b, Scan s)
    {
        var at = 2;
        for (var n = 0; n < MostChunks; n++)
        {
            if (b.Length - at < 4) { s.Missed("it's cut off before its image data"); return; }
            if (b[at] != 0xFF) { s.Missed("a segment doesn't start where the one before it ended"); return; }
            var marker = b[at + 1];
            if (marker == 0xFF) { at++; continue; }   // fill byte
            if (marker is 0xD9 or 0xDA) return;   // end of image, or start of scan: metadata comes before
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7)) { at += 2; continue; }   // markers with no length
            var length = BinaryPrimitives.ReadUInt16BigEndian(b[(at + 2)..]);
            if (length < 2 || length > b.Length - at - 2) { s.Missed($"a segment says it's {length} bytes, more than the file holds"); return; }
            var data = b.Slice(at + 4, length - 2);
            if (marker == 0xE1)
            {
                if (data.StartsWith("Exif\0\0"u8)) Exif(data[6..], s);
                else if (data.StartsWith("http://ns.adobe.com/xap/1.0/\0"u8) && Utf8(data[29..], s, "XMP packet") is { } xmp) Xmp(xmp, s);
            }
            at += 2 + length;
        }
        s.Missed($"it has more than {MostChunks} segments, so the rest weren't read");
    }

    private static void Exif(ReadOnlySpan<byte> tiff, Scan s)
    {
        const string broken = "its EXIF block is malformed, so its Copyright and Artist couldn't all be read";
        if (tiff.Length < 8) { s.Missed(broken); return; }
        bool little;
        if (tiff[0] == 'I' && tiff[1] == 'I') little = true;
        else if (tiff[0] == 'M' && tiff[1] == 'M') little = false;
        else { s.Missed(broken); return; }
        ushort U16(ReadOnlySpan<byte> x) => little ? BinaryPrimitives.ReadUInt16LittleEndian(x) : BinaryPrimitives.ReadUInt16BigEndian(x);
        uint U32(ReadOnlySpan<byte> x) => little ? BinaryPrimitives.ReadUInt32LittleEndian(x) : BinaryPrimitives.ReadUInt32BigEndian(x);
        if (U16(tiff[2..]) != 42) { s.Missed(broken); return; }
        var ifd = U32(tiff[4..]);
        if (ifd > tiff.Length - 2) { s.Missed(broken); return; }
        var count = U16(tiff[(int)ifd..]);
        if (count > (tiff.Length - (int)ifd - 2) / 12) { s.Missed(broken); return; }   // every entry it claims must be there
        for (var i = 0; i < count; i++)   // only IFD0, where Copyright and Artist live: no chain to follow, so no loop
        {
            var entry = (int)ifd + 2 + i * 12;
            var tag = U16(tiff[entry..]);
            if (tag is not (0x8298 or 0x013B)) continue;
            var type = U16(tiff[(entry + 2)..]);
            var n = U32(tiff[(entry + 4)..]);
            if (n == 0) continue;
            if (type != 2) { s.Missed($"its EXIF {(tag == 0x8298 ? "Copyright" : "Artist")} isn't stored as text, so it couldn't be read"); continue; }
            ReadOnlySpan<byte> value;
            if (n <= 4) value = tiff.Slice(entry + 8, (int)n);
            else
            {
                var offset = U32(tiff[(entry + 8)..]);
                if (offset > tiff.Length || n > tiff.Length - offset) { s.Missed($"its EXIF {(tag == 0x8298 ? "Copyright" : "Artist")} points past the end of its block, so it couldn't be read"); continue; }
                value = tiff.Slice((int)offset, (int)n);
            }
            if (tag == 0x8298)
            {
                // Photographer and editor copyrights are separated by a NUL (EXIF 2.3); each is kept.
                foreach (var part in Encoding.Latin1.GetString(value).Split('\0', StringSplitOptions.RemoveEmptyEntries))
                    Add(s, "EXIF 0x8298 Copyright", RightsKind.Copyright, part);
            }
            else Add(s, "EXIF 0x013B Artist", RightsKind.Author, Encoding.Latin1.GetString(value).TrimEnd('\0'));
        }
    }

    // ---- glTF ----

    private static void Glb(ReadOnlySpan<byte> b, Scan s)
    {
        if (b.Length < 20) { s.Missed("it's cut off inside its header"); return; }
        // The header's version and total length must hold too; a file that lies about either may be hiding what follows (found by A237).
        var version = BinaryPrimitives.ReadUInt32LittleEndian(b[4..]);
        if (version != 2) { s.Missed($"it says it's glTF binary version {version}, and only version 2 is read"); return; }
        var total = BinaryPrimitives.ReadUInt32LittleEndian(b[8..]);
        if (total != (uint)b.Length) { s.Missed($"its header says it's {total} bytes, but the file is {b.Length}"); return; }
        var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(b[12..]);
        if (!b.Slice(16, 4).SequenceEqual("JSON"u8)) { s.Missed("its first chunk isn't JSON"); return; }
        if (chunkLength > (uint)(b.Length - 20)) { s.Missed($"its JSON chunk says it's {chunkLength} bytes, more than the file holds"); return; }
        GltfJson(b.Slice(20, (int)chunkLength), s);
    }

    private static void GltfJson(ReadOnlySpan<byte> json, Scan s)
    {
        if (json.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) json = json[3..];   // a byte-order mark isn't JSON
        try
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = 64, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Skip });
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("asset", out var asset)
                && asset.ValueKind == JsonValueKind.Object && asset.TryGetProperty("copyright", out var copyright))
            {
                if (copyright.ValueKind == JsonValueKind.String) Add(s, "asset.copyright", RightsKind.Copyright, copyright.GetString()!);
                else s.Missed("its asset.copyright isn't text, so it couldn't be read");
            }
        }
        catch (JsonException) { s.Missed("its JSON couldn't be read"); }
    }

    // ---- TrueType / OpenType ----

    private static void Font(ReadOnlySpan<byte> b, Scan s)
    {
        if (!b.StartsWith("ttcf"u8)) { FontAt(b, 0, s); return; }
        if (b.Length < 12) { s.Missed("it's cut off inside its header"); return; }
        var fonts = BinaryPrimitives.ReadUInt32BigEndian(b[8..]);
        if (fonts > (uint)(b.Length - 12) / 4) { s.Missed($"it says it holds {fonts} fonts, more than the file has room for"); return; }
        if (fonts > MostFonts) s.Missed($"it holds {fonts} fonts, more than the {MostFonts} this scan reads, so the rest weren't read");
        for (var i = 0; i < fonts && i < MostFonts; i++)
        {
            var offset = BinaryPrimitives.ReadUInt32BigEndian(b[(12 + i * 4)..]);
            if (offset > (uint)b.Length) { s.Missed($"font {i + 1} in the collection starts past the end of the file, so it couldn't be read"); continue; }
            FontAt(b, (int)offset, s);
        }
    }

    private static void FontAt(ReadOnlySpan<byte> b, int start, Scan s)
    {
        if (b.Length - start < 12) { s.Missed("it's cut off inside its table directory"); return; }
        var tables = BinaryPrimitives.ReadUInt16BigEndian(b[(start + 4)..]);
        if (tables > (b.Length - start - 12) / 16) { s.Missed($"it says it has {tables} tables, more than the file has room for"); return; }
        for (var i = 0; i < tables; i++)
        {
            var record = start + 12 + i * 16;
            if (!b.Slice(record, 4).SequenceEqual("name"u8)) continue;
            var offset = BinaryPrimitives.ReadUInt32BigEndian(b[(record + 8)..]);
            var length = BinaryPrimitives.ReadUInt32BigEndian(b[(record + 12)..]);
            if (offset > (uint)b.Length || length > (uint)b.Length - offset) { s.Missed("its name table lies past the end of the file"); return; }
            NameTable(b.Slice((int)offset, (int)length), s);
            return;
        }
        // a font with no name table has nothing to say
    }

    private static void NameTable(ReadOnlySpan<byte> t, Scan s)
    {
        if (t.Length < 6) { s.Missed("its name table is cut off"); return; }
        var count = BinaryPrimitives.ReadUInt16BigEndian(t[2..]);
        var strings = BinaryPrimitives.ReadUInt16BigEndian(t[4..]);
        if (count > (t.Length - 6) / 12) { s.Missed($"its name table says it has {count} names, more than it has room for"); return; }
        for (var i = 0; i < count; i++)
        {
            var r = t[(6 + i * 12)..];
            var platform = BinaryPrimitives.ReadUInt16BigEndian(r);
            var encoding = BinaryPrimitives.ReadUInt16BigEndian(r[2..]);
            var id = BinaryPrimitives.ReadUInt16BigEndian(r[6..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(r[8..]);
            var offset = BinaryPrimitives.ReadUInt16BigEndian(r[10..]);
            if (id is not (0 or 13 or 14)) continue;
            var at = strings + offset;
            // One bad record doesn't hide the others, but it's said: the scan is incomplete.
            if (at > t.Length || length > t.Length - at) { s.Missed($"its name {id} record points past the end of its name table, so it couldn't be read"); continue; }
            var raw = t.Slice(at, length);
            string text;
            if (platform is 0 or 3)
            {
                if (length % 2 != 0) { s.Missed($"its name {id} record has an odd length for UTF-16 text, so it couldn't be read"); continue; }
                text = Encoding.BigEndianUnicode.GetString(raw);
            }
            else if (platform == 1 && encoding == 0) text = Encoding.Latin1.GetString(raw);   // Mac Roman, near enough for ASCII-range notices
            else { s.Missed($"its name {id} record uses an encoding this scan doesn't read (platform {platform}, encoding {encoding})"); continue; }
            Add(s, $"name {id}", id == 0 ? RightsKind.Copyright : RightsKind.Licence, text);
        }
    }
}
