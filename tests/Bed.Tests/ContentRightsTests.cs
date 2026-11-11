#pragma warning disable CS0618 // AssetRecord.Create is obsolete for imports; these cases test the low-level record writer (and old, undeclared records) on purpose.
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Agent;
using Bed.Data;
using Xunit;

namespace Bed.Tests;

/// <summary>Builds tiny self-authored files carrying rights tags (INVENTED fixtures: every byte is made here).</summary>
internal static class RightsFixtures
{
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(byte[] bytes)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in bytes) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    public static byte[] Chunk(string type, byte[] data)
    {
        var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        var b = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(b, (uint)data.Length);
        typed.CopyTo(b, 4);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8 + data.Length), Crc(typed));
        return b;
    }

    public static byte[] Zlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.SmallestSize)) z.Write(data);
        return output.ToArray();
    }

    public static byte[] Png(params byte[][] chunks)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, 1);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), 1);
        ihdr[8] = 8; ihdr[9] = 0;
        return [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Chunk("IHDR", ihdr), .. chunks.SelectMany(c => c), .. Chunk("IEND", [])];
    }

    public static byte[] Text(string keyword, string text) => Chunk("tEXt", [.. Encoding.Latin1.GetBytes(keyword), 0, .. Encoding.Latin1.GetBytes(text)]);

    public static byte[] ZText(string keyword, string text) => Chunk("zTXt", [.. Encoding.Latin1.GetBytes(keyword), 0, 0, .. Zlib(Encoding.Latin1.GetBytes(text))]);

    public static byte[] IText(string keyword, string text, bool compressed = false)
    {
        var body = Encoding.UTF8.GetBytes(text);
        return Chunk("iTXt", [.. Encoding.Latin1.GetBytes(keyword), 0, (byte)(compressed ? 1 : 0), 0, 0, 0, .. compressed ? Zlib(body) : body]);
    }

    public static string Xmp(string rights) => $"""
        <x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
        <rdf:Description xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:rights><rdf:Alt><rdf:li xml:lang="x-default">{rights}</rdf:li></rdf:Alt></dc:rights></rdf:Description>
        </rdf:RDF></x:xmpmeta>
        """;

    public static byte[] Jpeg(string? copyright = null, string? xmp = null, string? artist = null)
    {
        var segments = new List<byte>();
        void Segment(byte marker, byte[] data)
        {
            segments.AddRange([0xFF, marker, (byte)((data.Length + 2) >> 8), (byte)(data.Length + 2)]);
            segments.AddRange(data);
        }
        Segment(0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
        var entries = new List<(ushort Tag, byte[] Value)>();
        if (artist is not null) entries.Add((0x013B, [.. Encoding.ASCII.GetBytes(artist), 0]));
        if (copyright is not null) entries.Add((0x8298, [.. Encoding.ASCII.GetBytes(copyright), 0]));
        if (entries.Count > 0)
        {
            var tiff = new List<byte> { (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0 };
            var dataAt = 8 + 2 + entries.Count * 12 + 4;
            var ifd = new List<byte> { (byte)entries.Count, 0 };
            var data = new List<byte>();
            foreach (var (tag, value) in entries)
            {
                var e = new byte[12];
                BinaryPrimitives.WriteUInt16LittleEndian(e, tag);
                BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(2), 2);
                BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(4), (uint)value.Length);
                if (value.Length <= 4) value.CopyTo(e, 8);
                else { BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(8), (uint)(dataAt + data.Count)); data.AddRange(value); }
                ifd.AddRange(e);
            }
            ifd.AddRange([0, 0, 0, 0]);
            Segment(0xE1, [.. "Exif\0\0"u8, .. tiff, .. ifd, .. data]);
        }
        if (xmp is not null) Segment(0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. Encoding.UTF8.GetBytes(xmp)]);
        return [0xFF, 0xD8, .. segments, 0xFF, 0xDA, 0, 2, 0xFF, 0xD9];
    }

    public static byte[] GltfJson(string? copyright) => Encoding.UTF8.GetBytes(copyright is null
        ? """{"asset":{"version":"2.0"}}""" : "{\"asset\":{\"version\":\"2.0\",\"copyright\":" + JsonSerializer.Serialize(copyright) + "}}");

    public static byte[] Glb(string? copyright)
    {
        var json = GltfJson(copyright);
        var padded = json.Concat(Enumerable.Repeat((byte)' ', (4 - json.Length % 4) % 4)).ToArray();
        var b = new byte[20 + padded.Length];
        "glTF"u8.CopyTo(b);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), (uint)b.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), (uint)padded.Length);
        "JSON"u8.CopyTo(b.AsSpan(16));
        padded.CopyTo(b, 20);
        return b;
    }

    /// <summary>A font file holding only a name table: enough for the reader, which looks at nothing else.</summary>
    public static byte[] Font(params (ushort Id, string Text)[] names)
    {
        var strings = new List<byte>();
        var records = new List<byte>();
        foreach (var (id, text) in names)
        {
            var bytes = Encoding.BigEndianUnicode.GetBytes(text);
            var r = new byte[12];
            BinaryPrimitives.WriteUInt16BigEndian(r, 3);
            BinaryPrimitives.WriteUInt16BigEndian(r.AsSpan(2), 1);
            BinaryPrimitives.WriteUInt16BigEndian(r.AsSpan(4), 0x409);
            BinaryPrimitives.WriteUInt16BigEndian(r.AsSpan(6), id);
            BinaryPrimitives.WriteUInt16BigEndian(r.AsSpan(8), (ushort)bytes.Length);
            BinaryPrimitives.WriteUInt16BigEndian(r.AsSpan(10), (ushort)strings.Count);
            records.AddRange(r);
            strings.AddRange(bytes);
        }
        var table = new byte[6];
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), (ushort)names.Length);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), (ushort)(6 + records.Count));
        byte[] name = [.. table, .. records, .. strings];
        var head = new byte[12 + 16];
        BinaryPrimitives.WriteUInt32BigEndian(head, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(4), 1);
        "name"u8.CopyTo(head.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(20), (uint)head.Length);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(24), (uint)name.Length);
        return [.. head, .. name];
    }
}

/// <summary>Content rights: the rights tags common files carry are read safely, and malformed files never crash or hang.</summary>
public sealed class RightsTagsTests
{
    private static IReadOnlyList<string> Texts(byte[] bytes, string name = "") => RightsTags.Read(bytes, name).Tags.Select(t => $"{t.Field}={t.Text}").ToList();

    [Fact]
    public void Png_text_chunks_and_xmp_are_read()
    {
        var png = RightsFixtures.Png(RightsFixtures.Text("Copyright", "© 2026 Example Studio. All rights reserved."), RightsFixtures.Text("Author", "Sam"),
            RightsFixtures.ZText("Copyright", "Zipped Holder"), RightsFixtures.IText("Copyright", "Ünïcode Holder", compressed: true),
            RightsFixtures.IText("XML:com.adobe.xmp", RightsFixtures.Xmp("XMP Holder")), RightsFixtures.Text("Comment", "not a rights tag"));
        var scan = RightsTags.Read(png);
        Assert.Equal("png", scan.Format);
        Assert.Null(scan.Problem);
        Assert.Equal(["tEXt Copyright=© 2026 Example Studio. All rights reserved.", "tEXt Author=Sam", "zTXt Copyright=Zipped Holder",
            "iTXt Copyright=Ünïcode Holder", "XMP dc:rights=XMP Holder"], Texts(png));
    }

    [Fact]
    public void Jpeg_exif_and_xmp_are_read()
    {
        var jpeg = RightsFixtures.Jpeg(copyright: "Photo Holder 2026", xmp: RightsFixtures.Xmp("XMP Photo Holder"), artist: "Pat");
        Assert.Equal(["EXIF 0x013B Artist=Pat", "EXIF 0x8298 Copyright=Photo Holder 2026", "XMP dc:rights=XMP Photo Holder"], Texts(jpeg));
        Assert.Null(RightsTags.Read(jpeg).Problem);
        Assert.Empty(Texts(RightsFixtures.Jpeg()));
    }

    [Fact]
    public void Gltf_and_glb_asset_copyright_is_read()
    {
        Assert.Equal(["asset.copyright=Model Holder"], Texts(RightsFixtures.GltfJson("Model Holder"), "tree.gltf"));
        Assert.Equal(["asset.copyright=Model Holder"], Texts(RightsFixtures.Glb("Model Holder")));
        Assert.Empty(Texts(RightsFixtures.Glb(null)));
        Assert.Empty(Texts(RightsFixtures.GltfJson("Model Holder"), "tree.json"));   // a .json isn't taken for glTF
    }

    /// <summary>A237: a GLB whose header lies about its version or its length isn't read as if it were whole: the scan says so.</summary>
    [Fact]
    public void A_glb_with_a_wrong_version_or_length_is_an_incomplete_read()
    {
        var good = RightsFixtures.Glb("Holder");
        Assert.Null(RightsTags.Read(good, "tree.glb").Problem);
        var version = (byte[])good.Clone();
        version[4] = 3;
        Assert.Contains("version 3", RightsTags.Read(version, "tree.glb").Problem);
        var length = (byte[])good.Clone();
        length[8]++;
        Assert.Contains("header says", RightsTags.Read(length, "tree.glb").Problem);
        Assert.False(RightsTags.Read(length, "tree.glb").Complete);
    }

    [Fact]
    public void Font_name_table_copyright_and_licence_are_read()
    {
        var font = RightsFixtures.Font((1, "Family"), (0, "Copyright 2026 Type Holder"), (13, "Licensed under the Example Licence"), (14, "https://example.invalid/licence"));
        var scan = RightsTags.Read(font);
        Assert.Equal("font", scan.Format);
        Assert.Equal(["name 0=Copyright 2026 Type Holder", "name 13=Licensed under the Example Licence", "name 14=https://example.invalid/licence"], Texts(font));
        Assert.Equal([RightsKind.Copyright, RightsKind.Licence, RightsKind.Licence], scan.Tags.Select(t => t.Kind));
    }

    [Fact]
    public void Obj_and_unknown_files_say_nothing()
    {
        var obj = RightsTags.Read("# Copyright someone\nv 0 0 0\n"u8, "a.obj");
        Assert.Null(obj.Format);
        Assert.Empty(obj.Tags);
    }

    [Fact]
    public void Every_truncation_of_every_format_is_a_clean_result()
    {
        byte[][] files =
        [
            RightsFixtures.Png(RightsFixtures.Text("Copyright", "Holder"), RightsFixtures.ZText("Copyright", "Z"), RightsFixtures.IText("XML:com.adobe.xmp", RightsFixtures.Xmp("X"))),
            RightsFixtures.Jpeg("Holder", RightsFixtures.Xmp("X"), "Pat"),
            RightsFixtures.Glb("Holder"),
            RightsFixtures.Font((0, "Holder"), (13, "L")),
        ];
        foreach (var file in files)
            for (var n = 0; n < file.Length; n++)
            {
                var scan = RightsTags.Read(file.AsSpan(0, n), "x.gltf");   // never throws
                Assert.NotNull(scan.Tags);
            }
        Assert.NotNull(RightsTags.Read(files[0].AsSpan(0, files[0].Length - 12)).Problem);   // no IEND: said, not hidden
    }

    [Fact]
    public void Lengths_that_point_past_the_end_are_refused_cleanly()
    {
        var png = RightsFixtures.Png(RightsFixtures.Text("Copyright", "Holder"));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(8), 0xFFFFFFF0);   // IHDR claims ~4 GB
        Assert.Contains("more than the file holds", RightsTags.Read(png).Problem);

        var jpeg = RightsFixtures.Jpeg("Holder");
        jpeg[4] = 0xFF; jpeg[5] = 0xFF;   // first segment claims 65535 bytes
        Assert.Contains("more than the file holds", RightsTags.Read(jpeg).Problem);

        var exif = RightsFixtures.Jpeg("A long enough holder");
        var tiffAt = exif.AsSpan().IndexOf("Exif\0\0"u8) + 6;
        BinaryPrimitives.WriteUInt32LittleEndian(exif.AsSpan(tiffAt + 4), 0x7FFFFFFF);   // IFD0 far past the end
        Assert.Empty(RightsTags.Read(exif).Tags);

        var glb = RightsFixtures.Glb("Holder");
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(12), 0xFFFFFFFF);
        Assert.Contains("more than the file holds", RightsTags.Read(glb).Problem);

        var font = RightsFixtures.Font((0, "Holder"));
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(4), 0xFFFF);
        Assert.Contains("more than the file has room for", RightsTags.Read(font).Problem);

        byte[] ttc = [.. "ttcf"u8, 0, 1, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF];
        Assert.Contains("more than the file has room for", RightsTags.Read(ttc).Problem);

        var names = RightsFixtures.Font((0, "Holder"));
        BinaryPrimitives.WriteUInt16BigEndian(names.AsSpan(28 + 2), 0xFFFF);   // name table claims 65535 records
        Assert.Contains("more than it has room for", RightsTags.Read(names).Problem);
    }

    [Fact]
    public void A_decompression_bomb_is_capped_and_an_entity_bomb_never_expands()
    {
        var bomb = RightsFixtures.Chunk("zTXt", [.. "Copyright"u8, 0, 0, .. RightsFixtures.Zlib(new byte[50_000_000])]);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var scan = RightsTags.Read(RightsFixtures.Png(bomb));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20));
        Assert.True(scan.Tags.All(t => t.Text.Length <= RightsTags.MostTextLength));

        const string lol = """<?xml version="1.0"?><!DOCTYPE x [<!ENTITY a "aaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;"><!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;">]><x:xmpmeta xmlns:x="adobe:ns:meta/" xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:rights>&c;</dc:rights></x:xmpmeta>""";
        var xml = RightsTags.Read(RightsFixtures.Png(RightsFixtures.IText("XML:com.adobe.xmp", lol)));
        Assert.Empty(xml.Tags);   // DTDs are refused, so nothing expands
    }

    [Fact]
    public void Seeded_garbage_after_each_signature_never_throws()
    {
        var random = new Random(20261107);
        byte[][] heads = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], [0xFF, 0xD8, 0xFF], [.. "glTF"u8], [0, 1, 0, 0], [.. "ttcf"u8], [.. "OTTO"u8]];
        for (var i = 0; i < 600; i++)
        {
            var body = new byte[random.Next(0, 300)];
            random.NextBytes(body);
            var scan = RightsTags.Read([.. heads[i % heads.Length], .. body], "g.gltf");
            Assert.NotNull(scan.Tags);
        }
    }

    [Theory]
    [InlineData(RightsKind.Copyright, "Someone", true)]
    [InlineData(RightsKind.Licence, "Example Licence", true)]
    [InlineData(RightsKind.Author, "Sam", false)]
    [InlineData(RightsKind.Author, "(c) Sam", true)]
    [InlineData(RightsKind.Author, "all rights reserved", true)]
    public void What_counts_as_a_claim(RightsKind kind, string text, bool claim) =>
        Assert.Equal(claim, RightsTags.IsClaim(new RightsTag("png", "f", kind, text)));
}

/// <summary>Content rights: imports declare an origin, contradicting tags refuse, declarations change only by command.</summary>
public sealed class AssetImportRightsTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("rights-").FullName;
    private static readonly JsonElement NoSettings = JsonDocument.Parse("{}").RootElement;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string Put(string name, byte[] bytes) { var p = Path.Combine(dir, name); File.WriteAllBytes(p, bytes); return p; }

    private static byte[] TaggedPng(string text) => RightsFixtures.Png(RightsFixtures.Text("Copyright", text));

    private static AssetImportResult Import(string path, AssetOrigin origin, string licence = "", string source = "", IReadOnlyCollection<string>? ours = null) =>
        AssetImport.Import(path, "png", 1, NoSettings, new Provenance("", licence, source, "", origin), AiUse.Unknown, ours);

    [Fact]
    public void No_declaration_no_import_and_nothing_written()
    {
        var path = Put("a.png", RightsFixtures.Png());
        var r = Import(path, AssetOrigin.Undeclared);
        Assert.False(r.Ok);
        Assert.Contains("no origin is declared", Assert.Single(r.Issues).Message);
        Assert.False(File.Exists(AssetRecord.SidecarFor(path)));
    }

    [Theory]
    [InlineData(AssetOrigin.Licensed, "", "https://example.invalid", "licence's name")]
    [InlineData(AssetOrigin.Licensed, "CC-BY-4.0", "", "needs its source")]
    [InlineData(AssetOrigin.PublicDomain, "", "", "needs its source")]
    public void A_declaration_needs_what_it_promises(AssetOrigin origin, string licence, string source, string said)
    {
        var path = Put("a.png", RightsFixtures.Png());
        var r = Import(path, origin, licence, source);
        Assert.False(r.Ok);
        Assert.Contains(r.Issues, i => i.Message.Contains(said));
        Assert.False(File.Exists(AssetRecord.SidecarFor(path)));
    }

    [Fact]
    public void Ours_with_a_copyright_tag_is_refused_quoting_it()
    {
        var path = Put("a.png", TaggedPng("© 2026 Another Studio. All rights reserved."));
        var r = Import(path, AssetOrigin.Ours);
        Assert.False(r.Ok);
        var message = Assert.Single(r.Issues).Message;
        Assert.Contains("declared ours", message);
        Assert.Contains("png tEXt Copyright \"© 2026 Another Studio. All rights reserved.\"", message);
        Assert.False(File.Exists(AssetRecord.SidecarFor(path)));
    }

    [Fact]
    public void Public_domain_with_a_rights_holder_is_refused_but_a_cc0_tag_is_not()
    {
        Assert.False(Import(Put("a.jpg", RightsFixtures.Jpeg("Photo Holder")), AssetOrigin.PublicDomain, source: "a museum").Ok);
        Assert.True(Import(Put("b.jpg", RightsFixtures.Jpeg("CC0 1.0: public domain dedication")), AssetOrigin.PublicDomain, source: "a museum").Ok);
    }

    [Fact]
    public void Ours_is_accepted_when_the_tag_names_the_project_itself()
    {
        var path = Put("a.png", TaggedPng("© 2026 Our Studio"));
        Assert.False(Import(path, AssetOrigin.Ours).Ok);
        Assert.True(Import(path, AssetOrigin.Ours, ours: ["Our Studio"]).Ok);
    }

    [Fact]
    public void Licensed_keeps_the_found_tags_in_the_record()
    {
        var path = Put("font.ttf", RightsFixtures.Font((0, "Copyright 2026 Type Holder"), (13, "Example Licence")));
        var r = Import(path, AssetOrigin.Licensed, "OFL-1.1", "https://example.invalid/font");
        Assert.True(r.Ok);
        var text = File.ReadAllText(AssetRecord.SidecarFor(path));
        Assert.Contains("\"declared\": \"licensed\"", text);
        Assert.Contains("Copyright 2026 Type Holder", text);
        var read = AssetRecord.Read(text);
        Assert.Equal(2, read.RightsFound!.Count);
        Assert.Equal(text, read.Write());   // deterministic round trip
        Assert.Empty(AssetIndex.Scan(dir).Issues);
    }

    [Fact]
    public void Ours_with_no_tags_imports_and_needs_no_licence()
    {
        var path = Put("model.glb", RightsFixtures.Glb(null));
        var r = Import(path, AssetOrigin.Ours);
        Assert.True(r.Ok);
        Assert.Null(r.Record!.RightsFound);
        Assert.Empty(AssetIndex.Scan(dir).Issues);   // no "no licence recorded" for our own work
    }

    [Fact]
    public void Declarations_change_by_command_with_undo_and_contradictions_are_refused()
    {
        var path = Put("a.png", TaggedPng("Someone Else"));
        Assert.True(Import(path, AssetOrigin.Licensed, "CC-BY-4.0", "https://example.invalid/a").Ok);
        var before = File.ReadAllText(AssetRecord.SidecarFor(path));
        var session = AssetImport.Open(path);
        Assert.False(session.Execute(new DeclareAssetOrigin(AssetOrigin.Ours)));   // the found tag contradicts it
        Assert.Contains(session.LastIssues, i => i.Message.Contains("Someone Else"));
        Assert.False(session.Execute(new DeclareAssetOrigin(AssetOrigin.Undeclared)));
        Assert.True(session.Execute(new DeclareAssetOrigin(AssetOrigin.Licensed, "CC-BY-SA-4.0")));
        Assert.Equal("CC-BY-SA-4.0", session.Current.Provenance.Licence);
        Assert.Equal("https://example.invalid/a", session.Current.Provenance.Origin);   // blank keeps the source
        Assert.True(session.Undo());
        Assert.Equal(before, session.Format.Write(session.Current));
        Assert.True(session.Redo());
        session.Save();
        Assert.Contains("CC-BY-SA-4.0", File.ReadAllText(AssetRecord.SidecarFor(path)));
        Assert.Equal("asset.declare_origin", AssetImport.Catalog().Commands.Single().Id);
    }

    [Fact]
    public void An_old_record_without_a_declaration_still_opens()
    {
        var path = Put("old.png", RightsFixtures.Png());
        var old = AssetRecord.Create(path, "png", 1, NoSettings, new Provenance("t", "self-authored", "t"), AiUse.Unknown);
        Assert.DoesNotContain("declared", old.Write());
        var session = AssetImport.Open(path);
        Assert.False(session.Repairing);
        Assert.True(session.Execute(new DeclareAssetOrigin(AssetOrigin.Ours)));
    }
}

/// <summary>Content rights: protected names, refused alike by the editor's session, the agent, the CLI and MCP; and the check.</summary>
public sealed class ProtectedNamesAndRightsCheckTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("names-").FullName;
    private string Data => Path.Combine(root, "data");
    private string Schemas => Path.Combine(root, "schemas");
    private string Assets => Path.Combine(root, "assets");

    public ProtectedNamesAndRightsCheckTests()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Schemas);
        Directory.CreateDirectory(Assets);
        File.WriteAllText(Path.Combine(Schemas, "cards.schema.json"), """{"type":"object","properties":{"cards":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"}}}}}}""");
        File.WriteAllText(Path.Combine(Data, "cards.json"), "{\n  \"cards\": [\n    { \"name\": \"Lucky Seven\" }\n  ]\n}\n");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private DataProject Project()
    {
        var p = DataProject.Open(Data, Schemas);
        p.ProtectedNames = new ProtectedNames(["Mega Mouse", "Zorbo"]);
        return p;
    }

    [Theory]
    [InlineData("Mega Mouse", "Mega Mouse")]
    [InlineData("the MEGA-mouse returns!", "Mega Mouse")]
    [InlineData("méga   mouse", "Mega Mouse")]
    [InlineData("Zorbo's hat", "Zorbo")]
    [InlineData("Zorbonaut", null)]
    [InlineData("megamouse", null)]
    public void Names_match_as_whole_words_after_normalising(string text, string? match) =>
        Assert.Equal(match, new ProtectedNames(["Mega Mouse", "Zorbo", " "]).Match(text));

    [Fact]
    public void The_editor_session_refuses_a_protected_name_at_its_field()
    {
        var file = Project().Find("cards.json")!;
        Assert.True(file.Open());
        Assert.False(file.Session!.Execute(new SetValue("/cards/0/name", JsonDocument.Parse("\"Zorbo Deluxe\"").RootElement)));
        var issue = Assert.Single(file.Session.LastIssues, i => i.Severity == Severity.Error);
        Assert.Contains("\"Zorbo\"", issue.Message);
        Assert.Equal("$.cards[0].name", issue.Path);
        Assert.Equal("/cards/0", issue.ObjectId);
        Assert.True(file.Session.Execute(new SetValue("/cards/0/name", JsonDocument.Parse("\"Zorbonaut\"").RootElement)));
    }

    [Fact]
    public void The_agent_and_mcp_refuse_alike()
    {
        var agent = new AgentSession(Project());
        var set = agent.Set("cards.json", "/cards/0/name", "\"mega mouse\"");
        Assert.Contains("Mega Mouse", set["refused"]!.ToJsonString());
        var mcp = new McpServer(new AgentSession(Project()));
        var call = mcp.Handle("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"set","arguments":{"file":"cards.json","at":"/cards/0/name","value":"\"Zorbo\""}}}""")!;
        Assert.Contains("protected names", call["result"]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void An_adapters_names_reach_the_project()
    {
        var project = Bed.Editor.ImGui.Adapters.ApplyProtectedNames(new NamesAdapter(), DataProject.Open(Data, Schemas));
        Assert.Equal(["Zorbo"], project.ProtectedNames!.Names);
        Assert.Contains(new AgentSession(project).Set("cards.json", "/cards/0/name", "\"Zorbo\"")["refused"]!.AsArray(), r => r!.GetValue<string>().Contains("Zorbo"));
    }

    private sealed class NamesAdapter : Bed.Editor.ImGui.IProjectAdapter
    {
        public string Name => "names";
        public DataProject Open(Bed.Editor.ImGui.ProjectPaths project) => throw new NotSupportedException();
        public void Configure(Bed.Editor.EditorModel<DataDocument> editor, Bed.Editor.ImGui.DataTableView table) { }
        public IEnumerable<string> ProtectedNames => ["Zorbo"];
    }

    private string Put(string name, byte[] bytes) { var p = Path.Combine(Assets, name); File.WriteAllBytes(p, bytes); return p; }

    [Fact]
    public void The_check_lists_assets_and_flags_undeclared_unimported_and_contradicted_ones()
    {
        var empty = JsonDocument.Parse("{}").RootElement;
        AssetRecord.Create(Put("old.png", RightsFixtures.Png()), "png", 1, empty, new Provenance("t", "self-authored", "t"), AiUse.Unknown);
        Put("loose.png", RightsFixtures.Png());
        Assert.True(AssetImport.Import(Put("ok.glb", RightsFixtures.Glb(null)), "glb", 1, empty, new Provenance("", "", "", "", AssetOrigin.Ours), AiUse.Unknown).Ok);
        Assert.True(AssetImport.Import(Put("lic.ttf", RightsFixtures.Font((0, "Type Holder"))), "ttf", 1, empty, new Provenance("", "OFL-1.1", "a shop", "", AssetOrigin.Licensed), AiUse.Unknown).Ok);
        var swapped = Put("swap.png", RightsFixtures.Png());
        Assert.True(AssetImport.Import(swapped, "png", 1, empty, new Provenance("", "", "", "", AssetOrigin.Ours), AiUse.Unknown).Ok);
        File.WriteAllBytes(swapped, RightsFixtures.Png(RightsFixtures.Text("Copyright", "Swapped In Holder")));   // changed after import

        var agent = new AgentSession(DataProject.Open(Data, Schemas)).UseAssets(Assets);
        var result = agent.RunCheck("rights.assets");
        Assert.Equal("Completed", result["state"]!.GetValue<string>());
        Assert.False(result["clean"]!.GetValue<bool>());
        var findings = result["findings"]!.AsArray().Select(f => f!["message"]!.GetValue<string>()).ToList();
        Assert.Equal(3, findings.Count);
        Assert.Contains(findings, m => m.Contains("loose.png isn't imported"));
        Assert.Contains(findings, m => m.StartsWith("old.png: undeclared") && m.Contains("no declared origin"));
        Assert.Contains(findings, m => m.StartsWith("swap.png: ours") && m.Contains("Swapped In Holder"));
        var summary = result["summary"]!.GetValue<string>();
        Assert.Contains("lic.ttf: licensed, licence \"OFL-1.1\", source \"a shop\"; tags: font name 0 \"Type Holder\"", summary);
        Assert.Contains("ok.glb: ours; no rights tags", summary);
    }

    [Fact]
    public void Cli_and_mcp_import_go_through_the_same_refusals()
    {
        Put("tagged.png", RightsFixtures.Png(RightsFixtures.Text("Copyright", "© Big Holder")));
        Put("mine.png", RightsFixtures.Png());
        string[] common = ["--data", Data, "--schemas", Schemas, "--assets", Assets];
        var o = new StringWriter();
        Assert.Equal(1, Bed.Agent.Cli.Program.Run([.. common, "import", "tagged.png", "ours"], TextReader.Null, o, TextWriter.Null));
        Assert.Contains("Big Holder", o.ToString());
        Assert.False(File.Exists(Path.Combine(Assets, "tagged.png.asset.json")));
        Assert.Equal(1, Bed.Agent.Cli.Program.Run([.. common, "import", "mine.png", "licensed"], TextReader.Null, new StringWriter(), TextWriter.Null));
        Assert.Equal(0, Bed.Agent.Cli.Program.Run([.. common, "import", "mine.png", "ours"], TextReader.Null, new StringWriter(), TextWriter.Null));
        Assert.True(File.Exists(Path.Combine(Assets, "mine.png.asset.json")));
        Assert.Equal(1, Bed.Agent.Cli.Program.Run([.. common, "import", "../data/cards.json", "ours"], TextReader.Null, new StringWriter(), TextWriter.Null));

        var mcp = new McpServer(new AgentSession(DataProject.Open(Data, Schemas)).UseAssets(Assets));
        var tools = mcp.Handle("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")!["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToList();
        Assert.Contains("import", tools);
        Assert.Contains("declare", tools);
        var refused = mcp.Handle("""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"import","arguments":{"file":"tagged.png","origin":"publicDomain","source":"somewhere"}}}""")!;
        Assert.Contains("Big Holder", refused["result"]!["content"]![0]!["text"]!.GetValue<string>());
        var licensed = mcp.Handle("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"import","arguments":{"file":"tagged.png","origin":"licensed","licence":"CC-BY-4.0","source":"https://example.invalid"}}}""")!;
        Assert.Contains("imported", licensed["result"]!["content"]![0]!["text"]!.GetValue<string>());
        var declare = mcp.Handle("""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"declare","arguments":{"file":"tagged.png","origin":"ours"}}}""")!;
        Assert.Contains("Big Holder", declare["result"]!["content"]![0]!["text"]!.GetValue<string>());

        var plain = new McpServer(new AgentSession(DataProject.Open(Data, Schemas)));
        Assert.DoesNotContain("import", plain.Handle("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")!["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()));
        Assert.NotNull(plain.Handle("""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"import","arguments":{"file":"mine.png","origin":"ours"}}}""")!["error"]);
    }
}

/// <summary>Content rights, review round: bounded decompression per file, stricter claim judging, re-import, read-only
/// and file checks for asset writes, keys and inserts, the adapter route, and old records' bytes.</summary>
public sealed class ContentRightsHardeningTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("rights2-").FullName;
    private static readonly JsonElement NoSettings = JsonDocument.Parse("{}").RootElement;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Put(string name, byte[] bytes) { var p = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, bytes); return p; }

    [Fact]
    public void Thousands_of_small_bombs_cost_bounded_time_and_memory()
    {
        var bomb = RightsFixtures.Zlib(new byte[1024 * 1024]);   // ~1 KB that inflates to 1 MiB
        var chunks = new List<byte[]>();
        for (var i = 0; i < 3000; i++)
        {
            chunks.Add(RightsFixtures.Chunk("zTXt", [.. "Copyright"u8, 0, 0, .. bomb]));
            chunks.Add(RightsFixtures.Chunk("zTXt", [.. "Comment"u8, 0, 0, .. bomb]));
            chunks.Add(RightsFixtures.Chunk("iTXt", [.. "XML:com.adobe.xmp"u8, 0, 1, 0, 0, 0, .. bomb]));
        }
        var png = RightsFixtures.Png([.. chunks]);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var scan = RightsTags.Read(png);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 200L * 1024 * 1024, $"allocated {allocated} bytes");   // tens of GB before the per-file budget
        Assert.Equal("png", scan.Format);
    }

    [Theory]
    [InlineData("© Big Co, ripped by Tyler", "Tyler", true)]   // another holder remains
    [InlineData("© 2026 Tyler. All rights reserved.", "Tyler", false)]
    [InlineData("© Ty", "Ty", true)]   // too short to clear anything
    [InlineData("© Tylerson Games", "Tyler", true)]   // not a whole word
    [InlineData("Copyright (c) 2026 Our Studio Ltd", "Our Studio", false)]
    public void Own_names_clear_a_tag_only_when_they_are_all_it_says(string text, string own, bool claim) =>
        Assert.Equal(claim, RightsTags.IsClaim(new RightsTag("png", "tEXt Copyright", RightsKind.Copyright, text), [own]));

    [Theory]
    [InlineData("not public domain", true)]
    [InlineData("Copyright 2020 Someone; public domain elsewhere", true)]
    [InlineData("Public domain", false)]
    [InlineData("CC0 1.0 Universal public domain dedication", false)]
    [InlineData("No rights reserved", false)]
    public void Only_a_plain_public_domain_statement_clears(string text, bool claim) =>
        Assert.Equal(claim, RightsTags.IsClaim(new RightsTag("jpeg", "EXIF 0x8298 Copyright", RightsKind.Copyright, text), publicDomainOk: true));

    [Fact]
    public void A_gltf_with_a_byte_order_mark_is_read() =>
        Assert.Equal("Holder", Assert.Single(RightsTags.Read([0xEF, 0xBB, 0xBF, .. RightsFixtures.GltfJson("Holder")], "m.gltf").Tags).Text);

    [Fact]
    public void Reimport_reads_tags_again_and_refuses_a_contradiction()
    {
        var path = Put("a.png", RightsFixtures.Png());
        var record = AssetImport.Import(path, "png", 1, NoSettings, new Provenance("", "", "", "", AssetOrigin.Ours), AiUse.Unknown).Record!;
        var sidecar = File.ReadAllText(AssetRecord.SidecarFor(path));
        File.WriteAllBytes(path, RightsFixtures.Png(RightsFixtures.Text("Copyright", "© Someone Else")));
        var e = Assert.Throws<InvalidOperationException>(() => record.Reimport(path, 2));
        Assert.Contains("Someone Else", e.Message);
        Assert.Equal(sidecar, File.ReadAllText(AssetRecord.SidecarFor(path)));   // nothing written

        var lic = Put("b.png", RightsFixtures.Png());
        var licensed = AssetImport.Import(lic, "png", 1, NoSettings, new Provenance("", "CC-BY-4.0", "a site", "", AssetOrigin.Licensed), AiUse.Unknown).Record!;
        File.WriteAllBytes(lic, RightsFixtures.Png(RightsFixtures.Text("Copyright", "Site Holder")));
        Assert.Equal("Site Holder", Assert.Single(licensed.Reimport(lic, 2).RightsFound!).Text);
    }

    [Fact]
    public void An_old_sidecar_reads_and_writes_back_byte_for_byte()
    {
        const string old = "{\n  \"kind\": \"asset\",\n  \"version\": 1,\n  \"guid\": \"0123456789abcdef0123456789abcdef\",\n  \"source\": \"a.png\",\n"
            + "  \"sourceHash\": \"00\",\n  \"importer\": {\n    \"id\": \"png\",\n    \"version\": 1\n  },\n  \"settings\": {},\n"
            + "  \"provenance\": {\n    \"author\": \"t\",\n    \"licence\": \"self-authored\",\n    \"origin\": \"t\",\n    \"notes\": \"made by hand\"\n  },\n  \"aiUse\": \"unknown\"\n}\n";
        var record = AssetRecord.Read(old);
        Assert.Equal(AssetOrigin.Undeclared, record.Provenance.Declared);
        Assert.Equal(old, record.Write());
    }

    private (AgentSession Agent, string Assets) Agent(bool readOnly = false, Func<FileChange, string?>? fileCheck = null)
    {
        var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
        var schemas = Directory.CreateDirectory(Path.Combine(root, "schemas")).FullName;
        var assets = Directory.CreateDirectory(Path.Combine(root, "assets")).FullName;
        var project = DataProject.Open(data, schemas, readOnly);
        project.FileCheck = fileCheck;
        return (new AgentSession(project).UseAssets(assets), assets);
    }

    [Fact]
    public void Asset_writes_are_refused_read_only_and_meet_the_file_check()
    {
        var (readOnly, assets) = Agent(readOnly: true);
        File.WriteAllBytes(Path.Combine(assets, "a.png"), RightsFixtures.Png());
        Assert.Contains("read-only", readOnly.Import("a.png", "ours")["refused"]!.ToJsonString());
        Assert.False(File.Exists(Path.Combine(assets, "a.png.asset.json")));

        var asked = new List<FileChange>();
        var (checkedAgent, _) = Agent(fileCheck: c => { asked.Add(c); return c.Text!.Contains("\"licensed\"") ? "licensed art needs a sign-off" : null; });
        Assert.Contains("sign-off", checkedAgent.Import("a.png", "licensed", "CC-BY-4.0", "a site")["refused"]!.ToJsonString());
        Assert.False(File.Exists(Path.Combine(assets, "a.png.asset.json")));
        Assert.NotNull(checkedAgent.Import("a.png", "ours")["imported"]);
        Assert.Contains("sign-off", checkedAgent.Declare("a.png", "licensed", "CC-BY-4.0", "a site")["refused"]!.ToJsonString());
        Assert.Contains("\"ours\"", File.ReadAllText(Path.Combine(assets, "a.png.asset.json")));
        Assert.All(asked, c => Assert.Equal((FileChangeKind.Replace, "a.png.asset.json"), (c.Kind, c.Name)));

        var (thrower, _) = Agent(fileCheck: _ => throw new InvalidOperationException("broken rule"));
        Assert.Contains("broken rule", thrower.Declare("a.png", "publicDomain", source: "x")["refused"]!.ToJsonString());
    }

    [Fact]
    public void Own_names_reach_the_import_through_the_cli()
    {
        var (_, assets) = Agent();
        File.WriteAllBytes(Path.Combine(assets, "mine.png"), RightsFixtures.Png(RightsFixtures.Text("Copyright", "© 2026 Our Studio")));
        string[] common = ["--data", Path.Combine(root, "data"), "--schemas", Path.Combine(root, "schemas"), "--assets", assets];
        var o = new StringWriter();
        Assert.Equal(1, Bed.Agent.Cli.Program.Run([.. common, "import", "mine.png", "ours"], TextReader.Null, o, TextWriter.Null));
        Assert.Contains("--own-name", o.ToString());
        Assert.Equal(0, Bed.Agent.Cli.Program.Run([.. common, "--own-name", "Our Studio", "import", "mine.png", "ours"], TextReader.Null, new StringWriter(), TextWriter.Null));
    }

    [Fact]
    public void Protected_names_are_checked_in_keys_and_on_insert()
    {
        var names = new ProtectedNames(["Zorbo"]);
        var issue = Assert.Single(names.Validate(new DataDocument(JsonDocument.Parse("""{"cards":{"Zorbo the Great":{"cost":1}}}""").RootElement)));
        Assert.Contains("as a key", issue.Message);
        Assert.Contains("Zorbo the Great", issue.Path);
        Assert.Equal("/cards", issue.ObjectId);

        var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
        var schemas = Directory.CreateDirectory(Path.Combine(root, "schemas")).FullName;
        File.WriteAllText(Path.Combine(schemas, "cards.schema.json"), """{"type":"object"}""");
        File.WriteAllText(Path.Combine(data, "cards.json"), "{\n  \"cards\": []\n}\n");
        var project = DataProject.Open(data, schemas);
        project.ProtectedNames = names;
        var agent = new AgentSession(project);
        Assert.Contains("Zorbo", agent.Insert("cards.json", "/cards", 0, """{"name":"ZORBO!"}""")["refused"]!.ToJsonString());
        Assert.NotNull(agent.Insert("cards.json", "/cards", 0, """{"name":"Zorbonaut"}""")["applied"]);
    }

    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(Environment.CurrentDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Bed.slnx"))) return d.FullName;
        throw new InvalidOperationException("run from inside the repository");
    }

    [Fact]
    public void An_adapters_protected_names_are_refused_through_the_cli_adapter_route()
    {
        var repo = RepoRoot();
        var bin = Path.Combine(repo, "samples", "StarterAdapter", "bin");
        Assert.True(Directory.Exists(bin), "build the solution's existing StarterAdapter project before this test");
        var dll = Directory.GetFiles(bin, "StarterAdapter.dll", SearchOption.AllDirectories).Order().First();
        var data = Directory.CreateDirectory(Path.Combine(root, "starter", "data")).FullName;
        var schemas = Directory.CreateDirectory(Path.Combine(root, "starter", "schemas")).FullName;
        foreach (var f in Directory.GetFiles(Path.Combine(repo, "samples", "StarterProject", "data"))) File.Copy(f, Path.Combine(data, Path.GetFileName(f)));
        foreach (var f in Directory.GetFiles(Path.Combine(repo, "samples", "StarterProject", "schemas"))) File.Copy(f, Path.Combine(schemas, Path.GetFileName(f)));
        string[] common = ["--data", data, "--schemas", schemas, "--adapter", dll];
        var o = new StringWriter();
        Assert.Equal(1, Bed.Agent.Cli.Program.Run([.. common, "set", "wares.json", "/wares/0/name", "\"acme wonder-flour\""], TextReader.Null, o, TextWriter.Null));
        Assert.Contains("Acme Wonder Flour", o.ToString());
        o = new StringWriter();
        Assert.Equal(1, Bed.Agent.Cli.Program.Run([.. common, "insert", "wares.json", "/wares", "0", """{"id":"acme","name":"ACME Wonder Flour","price":1,"kind":"food"}"""], TextReader.Null, o, TextWriter.Null));
        Assert.Contains("protected names", o.ToString());
    }
}

/// <summary>A237 (Codex's attack): evidence past a cap, or in a part that couldn't be read, makes the scan incomplete, and
/// an incomplete scan refuses ours and public domain. Synthetic bytes only.</summary>
public sealed class ContentRightsCompletenessTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("rights3-").FullName;
    private static readonly JsonElement NoSettings = JsonDocument.Parse("{}").RootElement;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string Put(string name, byte[] bytes) { var p = Path.Combine(dir, name); File.WriteAllBytes(p, bytes); return p; }

    private static AssetImportResult Import(string path, AssetOrigin origin, string licence = "", string source = "", IReadOnlyCollection<string>? ours = null) =>
        AssetImport.Import(path, "x", 1, NoSettings, new Provenance("", licence, source, "", origin), AiUse.Unknown, ours);

    private static byte[] Compressed33() =>
        RightsFixtures.Png([.. Enumerable.Range(0, 32).Select(i => RightsFixtures.ZText("Author", $"Innocent {i}")), RightsFixtures.ZText("Copyright", "Hidden Holder")]);

    private static byte[] Tag65() =>
        RightsFixtures.Png([.. Enumerable.Range(0, 64).Select(i => RightsFixtures.Text("Author", $"Innocent {i}")), RightsFixtures.Text("Copyright", "Hidden Holder")]);

    /// <summary>A collection of <paramref name="count"/> fonts, only the last naming a rights holder.</summary>
    private static byte[] Collection(int count)
    {
        var fonts = Enumerable.Range(0, count).Select(i => i == count - 1 ? RightsFixtures.Font((0, "Hidden Type Holder")) : RightsFixtures.Font((1, $"Family {i}"))).ToList();
        var head = 12 + 4 * count;
        var bytes = new List<byte>();
        var header = new byte[head];
        "ttcf"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), (uint)count);
        var at = head;
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12 + 4 * i), (uint)at);
            var font = fonts[i].ToArray();
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(20), BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(20)) + (uint)at);   // table offsets are from the file's start
            fonts[i] = font;
            at += font.Length;
        }
        return [.. header, .. fonts.SelectMany(f => f)];
    }

    [Fact]
    public void A_holder_after_32_compressed_chunks_makes_the_scan_incomplete_and_refuses_ours()
    {
        var scan = RightsTags.Read(Compressed33());
        Assert.Contains("compressed rights chunks", scan.Problem);
        Assert.False(scan.Complete);
        var path = Put("c33.png", Compressed33());
        var refused = Import(path, AssetOrigin.Ours);
        Assert.False(refused.Ok);
        Assert.Contains("couldn't read all of this file's rights tags", Assert.Single(refused.Issues, i => i.Severity == Severity.Error).Message);
        Assert.False(Import(path, AssetOrigin.PublicDomain, source: "somewhere").Ok);
        var licensed = Import(path, AssetOrigin.Licensed, "CC-BY-4.0", "a site");
        Assert.True(licensed.Ok);
        Assert.Contains(licensed.Issues, i => i.Severity == Severity.Warning && i.Message.Contains("couldn't all be read"));
        var record = AssetRecord.Read(File.ReadAllText(AssetRecord.SidecarFor(path)));
        Assert.Contains("compressed rights chunks", record.RightsUnread);
        Assert.Equal(File.ReadAllText(AssetRecord.SidecarFor(path)), record.Write());
        var session = AssetImport.Open(path);
        Assert.False(session.Execute(new DeclareAssetOrigin(AssetOrigin.Ours)));   // the unread part is remembered
    }

    [Fact]
    public void A_holder_after_64_tags_makes_the_scan_incomplete_and_refuses_ours()
    {
        Assert.Contains($"more than {RightsTags.MostTags} rights tags", RightsTags.Read(Tag65()).Problem);
        Assert.False(Import(Put("t65.png", Tag65()), AssetOrigin.Ours).Ok);
    }

    [Fact]
    public void A_holder_in_font_17_of_a_collection_is_read_and_a_collection_past_the_cap_is_incomplete()
    {
        var scan = RightsTags.Read(Collection(17));
        Assert.Null(scan.Problem);
        Assert.Equal("Hidden Type Holder", Assert.Single(scan.Tags).Text);
        Assert.False(Import(Put("f17.ttc", Collection(17)), AssetOrigin.Ours).Ok);
        Assert.Contains("more than the 64", RightsTags.Read(Collection(RightsTags.MostFonts + 1)).Problem);
    }

    [Fact]
    public void Judging_reads_the_whole_tag_not_its_shown_start()
    {
        var spaced = "(c) Synthetic Studio" + new string(' ', 520) + "Other Holder";
        var png = RightsFixtures.Png(RightsFixtures.Text("Copyright", spaced));
        var tag = Assert.Single(RightsTags.Read(png).Tags);
        Assert.EndsWith("Other Holder", tag.Text);
        Assert.True(RightsTags.IsClaim(tag, ["Synthetic Studio"]));
        Assert.False(Import(Put("spaced.png", png), AssetOrigin.Ours, ours: ["Synthetic Studio"]).Ok);
        Assert.Contains("...", tag.ToString());   // only the display is shortened

        var author = RightsFixtures.Png(RightsFixtures.Text("Author", new string('a', 510) + " © Late Holder"));
        Assert.True(RightsTags.IsClaim(Assert.Single(RightsTags.Read(author).Tags)));

        var huge = RightsFixtures.Png(RightsFixtures.Text("Author", new string('a', RightsTags.MostTextLength + 10)));
        Assert.Contains("longer than", RightsTags.Read(huge).Problem);
    }

    private static byte[] ExifWith(Action<byte[], int> spoil)
    {
        var jpeg = RightsFixtures.Jpeg("A long enough holder");
        spoil(jpeg, jpeg.AsSpan().IndexOf("Exif\0\0"u8) + 6);
        return jpeg;
    }

    private static byte[] FontWith(Action<byte[]> spoil)
    {
        var font = RightsFixtures.Font((0, "Holder"));
        spoil(font);
        return font;
    }

    public static TheoryData<string, byte[]> Malformed() => new()
    {
        { "invalid zlib", RightsFixtures.Png(RightsFixtures.Chunk("zTXt", [.. "Copyright"u8, 0, 0, 1, 2, 3, 4, 5])) },
        { "XMP with a DTD", RightsFixtures.Png(RightsFixtures.IText("XML:com.adobe.xmp", """<?xml version="1.0"?><!DOCTYPE x [<!ENTITY a "b">]><x/>""")) },
        { "unclosed XMP", RightsFixtures.Png(RightsFixtures.IText("XML:com.adobe.xmp", """<x:xmpmeta xmlns:x="adobe:ns:meta/"><unclosed>""")) },
        { "invalid UTF-8 iTXt", RightsFixtures.Png(RightsFixtures.Chunk("iTXt", [.. "Copyright"u8, 0, 0, 0, 0, 0, 0xC3, 0x28])) },
        { "unknown zTXt method", RightsFixtures.Png(RightsFixtures.Chunk("zTXt", [.. "Copyright"u8, 0, 7, 1, 2])) },
        { "decompression past a chunk's cap", RightsFixtures.Png(RightsFixtures.ZText("Copyright", new string('x', 2 * 1024 * 1024))) },
        { "EXIF IFD past its block", ExifWith((b, t) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(t + 4), 0x7FFFFFFF)) },
        { "EXIF entry count past its block", ExifWith((b, t) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(t + 8), 0xFFFF)) },
        { "EXIF value past its block", ExifWith((b, t) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(t + 8 + 2 + 8), 0x7FFFFFF0)) },
        { "EXIF bad byte order", ExifWith((b, t) => { b[t] = (byte)'X'; b[t + 1] = (byte)'X'; }) },
        { "font name record past its table", FontWith(f => BinaryPrimitives.WriteUInt16BigEndian(f.AsSpan(28 + 6 + 10), 0xFFF0)) },
        { "font name with an odd UTF-16 length", FontWith(f => BinaryPrimitives.WriteUInt16BigEndian(f.AsSpan(28 + 6 + 8), 11)) },
        { "font name in an unread encoding", FontWith(f => BinaryPrimitives.WriteUInt16BigEndian(f.AsSpan(28 + 6), 2)) },
        { "glTF copyright that isn't text", Encoding.UTF8.GetBytes("""{"asset":{"version":"2.0","copyright":5}}""") },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void Every_malformed_part_is_said_and_refuses_ours(string kind, byte[] bytes)
    {
        var name = kind.StartsWith("glTF") ? "m.gltf" : kind.StartsWith("EXIF") ? "m.jpg" : kind.StartsWith("font") ? "m.ttf" : "m.png";
        var scan = RightsTags.Read(bytes, name);
        Assert.NotNull(scan.Problem);
        Assert.False(Import(Put(name, bytes), AssetOrigin.Ours).Ok);
    }

    [Fact]
    public void An_origin_that_isnt_one_is_refused()
    {
        var path = Put("a.png", RightsFixtures.Png());
        var r = Import(path, (AssetOrigin)999);
        Assert.False(r.Ok);
        Assert.Contains("999 isn't an origin", Assert.Single(r.Issues).Message);
        Assert.False(File.Exists(AssetRecord.SidecarFor(path)));
    }

    [Fact]
    public void The_cli_and_mcp_refuse_an_incomplete_scan_alike()
    {
        var data = Directory.CreateDirectory(Path.Combine(dir, "data")).FullName;
        var schemas = Directory.CreateDirectory(Path.Combine(dir, "schemas")).FullName;
        var assets = Directory.CreateDirectory(Path.Combine(dir, "assets")).FullName;
        File.WriteAllBytes(Path.Combine(assets, "c33.png"), Compressed33());
        var o = new StringWriter();
        Assert.Equal(1, Bed.Agent.Cli.Program.Run(["--data", data, "--schemas", schemas, "--assets", assets, "import", "c33.png", "ours"], TextReader.Null, o, TextWriter.Null));
        Assert.Contains("couldn't read all", o.ToString());
        var mcp = new McpServer(new AgentSession(DataProject.Open(data, schemas)).UseAssets(assets));
        var reply = mcp.Handle("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"import","arguments":{"file":"c33.png","origin":"publicDomain","source":"somewhere"}}}""")!;
        var answer = JsonNode.Parse(reply["result"]!["content"]![0]!["text"]!.GetValue<string>())!;   // the text is JSON, its apostrophes escaped
        Assert.Contains("couldn't read all", answer["refused"]![0]!.GetValue<string>());
        Assert.False(File.Exists(Path.Combine(assets, "c33.png.asset.json")));
    }
}
