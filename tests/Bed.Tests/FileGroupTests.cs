using Bed;
using Xunit;

/// <summary>Writing several files as one, with the disk failing and other programs at work at each moment it matters.</summary>
public sealed class FileGroupTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("file-group-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Make(string name, string text)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void All_of_them_go_when_nothing_is_wrong()
    {
        var (a, b) = (Make("a", "old a"), Make("b", "old b"));
        var result = FileGroup.Write([new(a, "old a", "new a"), new(b, "old b", "new b")]);
        Assert.Null(result.Failed);
        Assert.All(result.States, s => Assert.Equal(GroupFileState.Written, s));
        Assert.Equal("new a", File.ReadAllText(a));
        Assert.Equal("new b", File.ReadAllText(b));
    }

    [Fact]
    public void A_write_that_fails_puts_back_what_went_before_it_and_touches_nothing_after_it()
    {
        var (a, b, c) = (Make("a", "old a"), Make("b", "old b"), Make("c", "old c"));
        var result = FileGroup.Write([new(a, "old a", "new a"), new(b, "old b", "new b"), new(c, "old c", "new c")], (path, bytes) =>
        {
            if (path == b) throw new IOException("the disk said no");
            AtomicFile.Write(path, bytes);
        });
        Assert.Equal(1, result.Failed);
        Assert.Contains("disk said no", result.Why);
        Assert.Equal([GroupFileState.AsItWas, GroupFileState.AsItWas, GroupFileState.AsItWas], result.States);
        Assert.Equal(["old a", "old b", "old c"], new[] { a, b, c }.Select(File.ReadAllText));
    }

    [Fact]
    public void A_file_changed_outside_just_before_its_write_isnt_overwritten()
    {
        var (a, b) = (Make("a", "old a"), Make("b", "theirs"));
        var result = FileGroup.Write([new(a, "old a", "new a"), new(b, "old b", "new b")]);
        Assert.Equal(1, result.Failed);
        Assert.Contains("outside", result.Why);
        Assert.Equal("theirs", File.ReadAllText(b));
        Assert.Equal("old a", File.ReadAllText(a));
    }

    [Fact]
    public void A_file_changed_outside_after_its_write_isnt_overwritten_by_the_put_back()
    {
        var (a, b) = (Make("a", "old a"), Make("b", "old b"));
        var result = FileGroup.Write([new(a, "old a", "new a"), new(b, "old b", "new b")], (path, bytes) =>
        {
            if (path != b) { AtomicFile.Write(path, bytes); return; }
            File.WriteAllText(a, "theirs, since");
            throw new IOException("no");
        });
        Assert.Equal([GroupFileState.Unknown, GroupFileState.AsItWas], result.States);
        Assert.Equal("theirs, since", File.ReadAllText(a));
    }

    [Fact]
    public void A_put_back_that_fails_says_the_new_text_is_still_there()
    {
        var (a, b) = (Make("a", "old a"), Make("b", "old b"));
        var calls = 0;
        var result = FileGroup.Write([new(a, "old a", "new a"), new(b, "old b", "new b")], (path, bytes) =>
        {
            if (++calls > 1) throw new IOException("no");
            AtomicFile.Write(path, bytes);
        });
        Assert.Equal(1, result.Failed);
        Assert.Equal(GroupFileState.Written, result.States[0]);
        Assert.Equal("new a", File.ReadAllText(a));
    }

    [Fact]
    public void Bytes_come_back_exactly_a_byte_order_mark_and_line_endings_too()
    {
        byte[] exact = [0xEF, 0xBB, 0xBF, (byte)'o', (byte)'l', (byte)'d', 13, 10];
        var a = Path.Combine(root, "a");
        File.WriteAllBytes(a, exact);
        var b = Make("b", "old b");
        var result = FileGroup.Write([new(a, "old\r\n", "new a"), new(b, "old b", "new b")], (path, bytes) =>
        {
            if (path == b) throw new IOException("no");
            AtomicFile.Write(path, bytes);
        });
        Assert.Equal(GroupFileState.AsItWas, result.States[0]);
        Assert.Equal(exact, File.ReadAllBytes(a));
    }

    [Fact]
    public void No_temporary_files_are_left_when_it_fails()
    {
        var (a, b) = (Make("a", "old a"), Make("b", "old b"));
        using (new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.NotNull(FileGroup.Write([new(a, "old a", "new a"), new(b, "old b", "new b")]).Failed);
        Assert.Equal(["a", "b"], Directory.GetFiles(root).Select(Path.GetFileName).Order());
    }
}
